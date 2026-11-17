# Test-behaviors (Given/When/Then)

Cada comportamento indica **onde é provado** e o **status real**. Convenções:

- **Unit:** xUnit v3 + NSubstitute (175 testes aprovados na execução atual).
- **Manual:** executado contra o cluster com o worker local e `just forward`.
- **E2E:** cenário implementado no fluxo do `just e2e` (Jobs de mock, `assert-view` e `verify-replay`). Ainda não validado ponta a ponta no cluster.
- **Integração:** Testcontainers (Valkey/Kafka reais). Não incluído por decisão consciente.

Em "duplicado", o que se afirma é o **valor final do cache**, não uma linha de log.

## Resumo

| # | Comportamento | Unit | Manual | E2E | Integração |
|---|---|---|---|---|---|
| 1 | Estado atualizado reflete no cache | sim | sim | escrito | cortado |
| 2 | Nova posição reflete no cache | sim | sim | escrito | cortado |
| 3 | Fora de ordem é ignorado | sim (todas as permutações) | sim | escrito | cortado |
| 4 | Duplicado é no-op | sim | sim | escrito | cortado |
| 5 | Delete/tombstone | sim | sim | escrito | cortado |
| 6 | Posição antes do veículo | sim (cross-stream) | sim | escrito | cortado |
| 7 | Restart no meio do stream | sim (`ConsumerLoop`) | sim | escrito | cortado |
| 8 | Snapshot CDC e streaming subsequente | sim (parser/decider) | parcial | escrito | cortado |
| 9 | Replay do offset 0 idêntico | sim (`ViewComparer`) | sim (3 casos) | escrito | cortado |

## 1. Atualização de estado

- **Given** um veículo `REGISTERED` já projetado.
- **When** chegam updates para `ACTIVE`, `IDLE` e `MAINTENANCE` com `updated_at` crescente.
- **Then** `vehicle:{id}.state = MAINTENANCE` e `stateTs` é o do último evento.
- Prova: `VehicleDeciderTests`, `ApplyVehicleChangeUseCaseTests`; manual `lifecycle`; E2E `lifecycle`.

## 2. Nova posição

- **Given** um veículo `REGISTERED`.
- **When** chegam posições com `recorded_at` crescente (t+1, t+2, t+3 min).
- **Then** `lat/lng` são os da posição de t+3 e `state` segue `REGISTERED`.
- Prova: `PositionDeciderTests`; manual `positions` (lat -9.62, lng -35.72); E2E `positions`.

## 3. Fora de ordem é ignorado

- **Given** um estado aplicado com `updated_at = t+10`.
- **When** chega, depois (LSN maior), um update com `updated_at = t+5`.
- **Then** o estado permanece o de t+10 (`ACTIVE`); a decisão é `IgnoreStale`.
- O mesmo vale para posições: `recorded_at = t+5` depois de `t+10` não altera `lat/lng`.
- Prova: deciders; `ConvergenceTests` aplicam **todas as permutações** de uma lista de eventos e exigem o mesmo resultado; manual `out-of-order` e `position-out-of-order`; E2E `out-of-order`, `position-out-of-order` e `volume` (200 posições embaralhadas, esperado = a de maior `recorded_at`).

## 4. Duplicado é no-op

- **Given** um evento já aplicado.
- **When** o mesmo evento chega de novo (reentrega) ou uma linha idêntica é inserida duas vezes.
- **Then** o cache fica idêntico ao de antes; nenhuma escrita altera valor.
- Nuance: duas linhas idênticas no banco têm LSNs diferentes, então o log mostra `Apply` duas vezes (o LSN maior vence com o mesmo resultado). `IgnoreDuplicate` ocorre só com mesmo timestamp **e** mesmo LSN, isto é, reentrega do mesmo evento. Por isso a asserção é sobre o valor final.
- Prova: deciders (caso `IgnoreDuplicate`); manual `position-duplicate` (valor final correto); E2E `position-duplicate`, duplicatas no `volume` e duplicatas no cenário `stream`.

## 5. Delete e tombstone

- **Given** um veículo com estado projetado.
- **When** chega `op:"d"` e, em seguida, o tombstone (value null).
- **Then** `vehicle:{id}` não existe, `deleted:{id}` existe, e o tombstone é ignorado.
- **And When** chega depois um update de estado ou uma posição desse id, **Then** são ignorados (`IgnoreDeleted`) e `vehicle:{id}` continua ausente.
- Prova: `VehicleDeciderTests`, `PositionDeciderTests`, `CrossStreamConvergenceTests`; manual `delete` e `position-after-delete`; E2E `delete` e `position-after-delete`.
- Limite assumido: ids únicos e imutáveis.

## 6. Posição antes do veículo

- **Given** nenhum evento do veículo foi processado.
- **When** chega uma posição para o id.
- **Then** o hash existe só com `lat/lng/posTs/posLsn`; `state` ausente (UNKNOWN), nunca presumido.
- **And When** depois chega o evento do veículo, **Then** o hash tem os dois grupos de campos, sem perder a posição.
- Prova: `CrossStreamConvergenceTests` (todas as ordens de intercalação entre os dois streams); manual `position-before-vehicle`; E2E `position-before-vehicle` (espera `state=REGISTERED` com `lat/lng`).

## 7. Restart do worker no meio do stream

- **Given** o worker consumindo com offsets commitados apenas após gravar no Valkey.
- **When** o pod é removido enquanto o Job `seed-stream` está escrevendo e o Deployment recria o worker.
- **Then** nada se perde, o stream retoma dos offsets apropriados e eventos reentregues não corrompem a view.
- Prova: `ConsumerLoopTests` (commit só após o handler; `Seek` em exceção); manual (worker parado, escrita, subida: só eventos novos); E2E `seed-stream-restart` após o primeiro log `progresso`, entre `seed-a` e `seed-b`, com `assert-view` validando o estado final.
- Limite: o E2E prova o **resultado final** correto depois do restart, mas não garante que houve reentrega; isso depende do timing.

## 8. Snapshot CDC e streaming subsequente

- **Given** um conector novo, sem dados existentes nos tópicos.
- **When** `snapshot-pre` cria veículos e posições antes do registro do Debezium e `snapshot-post` altera essas mesmas linhas depois do início do streaming.
- **Then** `snapshot-only` é projetado pelos eventos `op=r`; `snapshot-then-stream` combina snapshot com updates/inserts posteriores e converge para `ACTIVE` com lat/lng `-9.41/-35.41`.
- Prova unitária: parser/decider, regra comum para snapshot e streaming, convergência por permutação e testes de replay.
- E2E: o fluxo `snapshot` confirma eventos `op=r` nos dois tópicos antes de executar `snapshot-post`; em cluster reutilizado, avisa que o snapshot inicial não será repetido e continua pelo streaming.
- Limite: a prova completa do snapshot depende de cluster novo, pois um conector já existente não dispara outro snapshot inicial.

## 9. Replay do offset 0 reconstrói cache idêntico

- **Given** o db 0 convergiu para os valores esperados (`assert-view` passou).
- **When** um worker em modo replay (`Replay__Enabled=true`, `Valkey__Database=1`, grupo novo) lê os tópicos desde o offset 0.
- **Then** `verify-replay` encontra `vehicle:*` e `deleted:*` idênticos entre db 0 e db 1, em duas leituras consecutivas.
- Falhas detectadas, provadas manualmente: chave ausente no replay e conteúdo diferente (ambos saem com código 1). Origem vazia sai com código 2.
- O replay processa em ordem diferente da original (por exemplo, posição antes do veículo) e chega ao mesmo resultado, que é a evidência de que a projeção é determinística.
- Prova: `ViewComparerTests`, `ReplayConfigurationTests` (trava contra db 0); manual com db 1; E2E etapa `replay`.

## Cenários E2E implementados

| Receita | Cenários cobertos | Asserção |
|---|---|---|
| `seed-a` | `lifecycle`, `out-of-order`, `delete`, `volume` | Via `assert-view` no db 0 |
| `seed-b` | `positions`, `position-out-of-order`, `position-duplicate`, `position-before-vehicle`, `position-after-delete` | Via `assert-view` no db 0 |
| `snapshot` | `snapshot-only`, `snapshot-then-stream-pre/post` | Evento `op=r` confirmado e `assert-view` |
| `seed-stream-restart` | Stream de 3 veículos por padrão, com fora de ordem e duplicatas | `assert-view` valida estado e posição dos veículos |
| `replay` | Reconstrução completa no db 1 | `verify-replay` compara db 0 e db 1 duas vezes consecutivas |

O `assert-view` verifica estado, latitude e longitude dos cenários individuais e dos veículos do stream. Neste momento, ele não verifica `stateTs`, `stateLsn`, `posTs` ou `posLsn`.

## Comportamentos extras cobertos

| Comportamento | Prova |
|---|---|
| Mensagem inválida (poison) não trava a partição | parsers com mensagens reais; log, skip e commit |
| Escrita concorrente entre leitura e `EXEC` aborta e repete | unit com mock; integração cortada |
| Retries esgotados não commitam o offset | unit (`OptimisticWriteExhaustedException`) |
| Replay recusa subir com db 0 | `ReplayConfigurationTests` |
| Entrada fora da faixa ou campo CDC inválido é rejeitada | testes dos parsers |

## Lacunas conhecidas

- Nenhum fluxo `just e2e` completo foi validado ponta a ponta no cluster.
- O `assert-view` não confere `posTs`/`stateTs` nem os campos LSN.
- Sabotagem pendente: trocar um valor esperado no `AssertViewCommand` e confirmar que `just assert` falha; também inverter uma comparação do decider e confirmar que a suíte unitária falha.
- Integração com Valkey/Kafka reais não existe; a atomicidade da transação otimista só é verificada contra mock.
- A etapa de restart não prova determinísticamente que houve reentrega; ela prova apenas a convergência do estado final.
- Em posições, timestamp e LSN iguais no snapshot são tratados como duplicata; o `id` da linha ainda não foi usado como terceiro desempate.
