# Test-behaviors (Given/When/Then)

Cada comportamento indica **onde é provado** e o **status real**. Convenções:

- **Unit:** xUnit v3 + NSubstitute (130 testes passando na última execução).
- **Manual:** executado contra o cluster com o worker local e `just forward`.
- **E2E:** cenário do `just e2e` (Jobs de mock + `assert-view`). **Escrito, ainda não executado no cluster.**
- **Integração:** Testcontainers (Valkey/Kafka reais). **Cortado por tempo.**

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
| 8 | Replay do offset 0 idêntico | sim (`ViewComparer`) | sim (3 casos) | escrito | cortado |

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
- Prova: deciders (caso `IgnoreDuplicate`); manual `position-duplicate` (valor final correto); E2E `position-duplicate` e duplicatas no `volume`.

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

- **Given** o worker consumindo com offsets commitados apenas após gravar.
- **When** o pod é morto enquanto há eventos em trânsito e o Deployment o recria.
- **Then** nada se perde (retoma do último offset commitado) e eventos reentregues não corrompem a view (idempotência pela regra de versão).
- Prova: `ConsumerLoopTests` (commit só após o handler; `Seek` em exceção); manual (worker parado, escrita, subida: só eventos novos); E2E `restart-worker` entre `seed-a` e `seed-b`.
- Limite: o e2e prova o **resultado final** correto depois do restart. Não prova que houve reentrega, porque isso depende do timing.

## 8. Replay do offset 0 reconstrói cache idêntico

- **Given** o db 0 convergiu para os valores esperados (`assert-view` passou).
- **When** um worker em modo replay (`Replay__Enabled=true`, `Valkey__Database=1`, grupo novo) lê os tópicos desde o offset 0.
- **Then** `verify-replay` encontra `vehicle:*` e `deleted:*` idênticos entre db 0 e db 1, em duas leituras consecutivas.
- Falhas detectadas, provadas manualmente: chave ausente no replay e conteúdo diferente (ambos saem com código 1). Origem vazia sai com código 2.
- O replay processa em ordem diferente da original (por exemplo, posição antes do veículo) e chega ao mesmo resultado, que é a evidência de que a projeção é determinística.
- Prova: `ViewComparerTests`, `ReplayConfigurationTests` (trava contra db 0); manual com db 1; E2E etapa `replay`.

## Comportamentos extras cobertos

| Comportamento | Prova |
|---|---|
| Mensagem inválida (poison) não trava a partição | parsers com mensagens reais; log, skip, commit |
| Escrita concorrente entre leitura e `EXEC` aborta e repete | unit com mock; **integração cortada** |
| Retries esgotados não commitam o offset | unit (`OptimisticWriteExhaustedException`) |
| Replay recusa subir com db 0 | `ReplayConfigurationTests` |

## Lacunas conhecidas

- Nenhum E2E rodou no cluster ainda. Antes da entrega: `just up`, `just e2e` e `just up` duas vezes.
- O `assert-view` não confere `posTs`/`stateTs`.
- Sabotagem pendente: trocar um valor esperado no `AssertViewCommand` e confirmar que `just assert` falha (prova que o assert pega erro). O mesmo vale para inverter `>` num decider e verificar que algum teste quebra.
- Integração com Valkey/Kafka reais não existe; a atomicidade da transação otimista só é verificada contra mock.