# Spec e plano de implementação: cache de veículos via CDC

Decisões e alternativas estão no ADR (`docs/adr/0001-cdc-vehicle-cache.md`). Aqui ficam o desenho, os contratos e o sequenciamento. Comportamentos testáveis estão em `docs/test-behaviors.md` e os diagramas em `docs/c4.md`.

## 1. Objetivo e restrições

Manter no Valkey, por veículo, a **última posição** e o **estado atual**, lidos por um único `HGETALL vehicle:{id}`.

- O cache é uma projeção pura do CDC. O worker **nunca** consulta o PostgreSQL.
- Replay do offset 0 reconstrói um cache idêntico.
- Correto sob: fora de ordem, duplicatas e reentrega, posição antes do veículo, snapshot, delete, restart.

## 2. Fluxo de dados

```
Mock writers -> PostgreSQL -> Debezium (Connect) -> Kafka
    fleet.public.vehicles   (3 partições, key = id)
    fleet.public.positions  (3 partições, key = vehicle_id)
        -> Worker (.NET 10) -> Valkey (db 0)
```

Os mock writers escrevem **apenas** no PostgreSQL. Kafka e Valkey só são tocados pelo CDC e pelo worker.

## 3. Contrato dos eventos CDC

Conversor JSON sem schema. Envelope observado no ambiente:

| Campo | Uso |
|---|---|
| `op` | `c` (insert), `u` (update), `d` (delete), `r` (snapshot) |
| `after` | linha nova (c/u/r) |
| `before` | linha antiga; completa no delete (`REPLICA IDENTITY FULL` em `vehicles`) |
| `source.lsn` | posição no log; numérico; desempate |
| value `null` | tombstone após delete; ignorado |

Tabelas:

- `vehicles(id text PK, state text, updated_at timestamptz)`. `state` em `REGISTERED, ACTIVE, IDLE, MAINTENANCE, DECOMMISSIONED`.
- `positions(id bigint identity, vehicle_id text, lat double precision, lng double precision, recorded_at timestamptz)`. Sem FK e sem unique de propósito.

Datas chegam em ISO-8601 UTC com microssegundos e são convertidas por `DebeziumTime.ToMicros` (µs desde a época; nunca ms).

## 4. Regras de ordenação

Versão de estado = `(updated_at µs, LSN)`. Versão de posição = `(recorded_at µs, LSN)`.

**Veículos**

| Evento | Situação | Decisão |
|---|---|---|
| Delete | marcador existe | IgnoreDuplicate |
| Delete | senão | Remove (DEL + marcador), sem comparar versão |
| Upsert | marcador existe | IgnoreDeleted |
| Upsert | sem estado atual | Apply |
| Upsert | `updated_at` maior / menor | Apply / IgnoreStale |
| Upsert | `updated_at` igual; LSN maior / menor / igual | Apply / IgnoreStale / IgnoreDuplicate |

**Posições:** marcador existe → IgnoreDeleted; sem posição atual → Apply; `recorded_at` maior/menor → Apply/IgnoreStale; empate → LSN maior/menor/igual → Apply/IgnoreStale/IgnoreDuplicate.

## 5. Schema do Valkey

```
vehicle:{id}  (hash)   state, stateTs(µs), stateLsn
                       lat, lng, posTs(µs), posLsn
deleted:{id}  (string) "1"          sem TTL; interno ao worker
```

- Campos de estado e de posição são disjuntos: cada slice só escreve os seus. O merge é comutativo.
- Hash ausente = veículo inexistente. Hash sem `state` = posição conhecida, estado UNKNOWN (nunca presumido).
- Replay grava no mesmo formato em outro banco lógico (db 1).

## 6. Escrita condicional

1. Lê os campos de versão (e o marcador) do veículo.
2. O decider (função pura) decide.
3. Se `Apply`/`Remove`, abre transação com `AddCondition` sobre os valores lidos e executa.
4. Se a condição falhar (escrita concorrente), repete do passo 1, até 5 vezes. Esgotado: exceção, offset não commitado.

Isso fecha a janela de pod pausado ou rebalanceamento: um escritor atrasado não consegue gravar sobre dado mais novo.

## 7. Componentes

| Componente | Responsabilidade |
|---|---|
| `VehicleConsumer`, `PositionConsumer` | Um por tópico, grupos distintos, loop serial |
| `ConsumerLoop` | Poll, handler, **commit só após o handler**; em exceção, `Seek` e repete |
| `KafkaConsumerFactory` | `EnableAutoCommit=false`, `AutoOffsetReset=Earliest`, CooperativeSticky |
| `*DebeziumParser` | JSON → `VehicleChange` / `PositionChange`; classifica entrada inválida (poison: log, skip, commit) |
| `*Decider` | Regras da seção 4 (puro, testado com todas as permutações) |
| `*ViewWriter` | Seção 6; único ponto de I/O com o Valkey |
| `Apply*ChangeUseCase` | Parser → leitura → decider → writer |
| `ReplayConfiguration` | Sufixo de grupo e validação (recusa `Replay:Enabled` com db 0) |
| `ViewReader`, `ViewComparer` | Leitura via SCAN e comparação pura db 0 × db 1 |

## 8. Replay

Com `Replay__Enabled=true`: group id vira `<grupo>-replay-<sufixo>` (grupo novo, offset 0) e a gravação vai para `Valkey__Database`. O `verify-replay` compara `vehicle:*` e `deleted:*` entre db 0 e db 1, com duas leituras idênticas seguidas, e falha se a origem estiver vazia.

## 9. Ambiente e `just e2e`

Um comando, a partir de checkout limpo: `just e2e`.

1. `up`: kind, namespace, ConfigMaps, build e carga da imagem única (worker + mockwriters), deploy via devspace, espera, tópicos (3 partições, retenção infinita), connector.
2. `seed-a` (Job): veículos (`lifecycle`, `out-of-order`, `delete`) e `volume` (200 posições embaralhadas com duplicatas).
3. `restart-worker`: apaga o pod do worker logo após o grupo A.
4. `seed-b` (Job): cenários de posição (fora de ordem, duplicada, antes do veículo, depois do delete).
5. `assert`: Job `assert-view` com polling até 180 s contra os valores esperados no db 0.
6. `replay`: FLUSHDB do db 1, Deployment `worker-replay`, Job `verify-replay`, remoção do Deployment (também em falha).

O replay só é comparado depois de o db 0 bater com o esperado; sem isso, "idêntico" provaria pouco. Todo passo falho devolve código diferente de zero.

## 10. Sequenciamento

1. Infra local (kind, Postgres com publication, Kafka, Connect, Valkey) e observação do envelope real.
2. Slice de veículos: parser, decider, writer, use case, testes de convergência por permutação.
3. Consumer loop com commit após gravar.
4. Slice de posições e testes de convergência entre streams.
5. Validação manual: lifecycle, fora de ordem, delete, restart, posições.
6. Replay no mesmo processo, `ViewComparer` e `verify-replay`.
7. Imagem única, Jobs de mock, `assert-view` e `just e2e`.
8. Documentos (ADR, Spec, test-behaviors, C4), README, PR.

Decisão de ordem: correção primeiro (deciders puros e testados), infra de e2e depois, para provar o que importa mesmo com tempo curto.

## 11. Cortes e pendências

- **Não validado no cluster:** imagem única, `worker.yaml`, `worker-replay.yaml`, recipes `image`, `mock-job`, `replay`, `e2e`. Compila e os testes unitários passam; o e2e não rodou.
- **Cortado:** testes de integração com Testcontainers (writers contra Valkey real, `ConsumerLoop` contra Kafka real). A concorrência entre leitura e `EXEC` é coberta só por mock.
- `assert-view` não confere `posTs`/`stateTs` e usa chaves em literal.
- Mocks sem taxa configurável nem duplicatas de veículo em volume.
- Replay a partir de uma data (offset por timestamp): não implementado.
- Desempate de posição com mesmo `recorded_at` e mesmo LSN usa "primeiro vence"; `id` da linha como 3º critério fica como alternativa.
- Restart do e2e prova o resultado final, não que houve reentrega.