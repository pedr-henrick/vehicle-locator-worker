# ADR 0001: Cache de veículos alimentado somente por CDC

| Campo | Valor |
|---|---|
| Status | Aceito (implementação parcialmente validada; ver "Pendências e cortes") |
| Data | 2026-11-17 |
| Autor | Pedro |
| Escopo | Worker .NET 10, Debezium, Kafka, Valkey |
| Documentos relacionados | `docs/spec.md`, `docs/test-behaviors.md`, `docs/c4.md` |

## Contexto

Um sistema de frota guarda `vehicles` (estado mutável; insert, update, delete) e `positions` (insert-only, alta frequência) no PostgreSQL. Leitores de baixa latência precisam saber, para qualquer veículo, **onde ele está agora e em que estado está**, sem consultar o banco.

Restrição central: o cache é construído **somente a partir do CDC**. O worker nunca lê o banco. Replay do offset 0 deve reconstruir um cache idêntico.

Problemas reais a resolver:

- Entrega fora de ordem (evento antigo não pode sobrescrever o novo).
- At-least-once e reentrega (escrita idempotente).
- Corrida entre streams (posição chega antes do veículo).
- Snapshot (`op=r`) versus streaming.
- Delete e tombstone.
- Replay determinístico.

Evidências coletadas no ambiente real:

- A key de `positions` é `vehicle_id` (`message.key.columns`), então cada veículo fica sempre na mesma partição.
- Envelope Debezium sem `schema`: `before/after/source/op`. Datas em ISO-8601 UTC com microssegundos. `source.lsn` numérico.
- Delete = evento `op:"d"` com `before` completo, seguido de tombstone (value null).
- Inserts em lote compartilham o mesmo `recorded_at`, portanto há empate real de timestamp.

## Decisão

### 1. Arquitetura

Projeção (read-model) com dois consumers independentes e loop serial. Vertical slice (`Vehicles`, `Positions`) em `Application` e `Worker`, com `Shared` mínimo. Parser → decider (função pura) → writer (I/O) → use case. Sem Mediator. Resultado das decisões por enum (`Apply`, `IgnoreStale`, `IgnoreDuplicate`, `IgnoreDeleted`, `Remove`).

### 2. Chave de ordenação

- Estado: `(updated_at em µs, LSN)`.
- Posição: `(recorded_at em µs, LSN)`.
- Maior vence; empate de timestamp desempata por LSN; mesmo timestamp **e** mesmo LSN = duplicata (reentrega) e é ignorada.
- Microssegundos nunca são convertidos para milissegundos (perderia empates e ordem).
- Não há coluna `version` no banco: exigiria mudar o modelo de origem e todos os escritores.

### 3. Escrita condicional

Transação otimista em C# (StackExchange.Redis): lê os campos de versão, decide, e executa `CreateTransaction` com `AddCondition` sobre os valores lidos. Se outro escritor alterou entre a leitura e o `EXEC`, a transação aborta e repete (até 5 tentativas). Esgotadas, lança `OptimisticWriteExhaustedException` e o offset **não** é commitado, então a mensagem é reprocessada.

### 4. Idempotência

Vem da regra de versão: reaplicar o mesmo evento é no-op. Consumo at-least-once; **offset commitado só depois de gravar no Valkey**. Em exceção do handler, `Seek` para o offset da mensagem e repete.

### 5. Schema do Valkey

```
vehicle:{id}  (hash)   state, stateTs(µs), stateLsn     <- slice Vehicles
                       lat, lng, posTs(µs), posLsn      <- slice Positions
deleted:{id}  (string) "1"                              <- marcador interno, sem TTL
```

- Estado e posição ficam em **campos disjuntos**, então o merge entre streams é comutativo: a ordem de chegada entre tópicos não altera o resultado final.
- Sem prefixo de chave; as chaves são idênticas em qualquer banco lógico.
- Leitor: hash ausente = veículo inexistente; hash sem `state` = posição conhecida e estado UNKNOWN.

### 6. Corrida posição-antes-do-veículo

A posição é aplicada normalmente e cria o hash só com campos de posição. `state` fica ausente (UNKNOWN). **O worker nunca presume estado.** Quando o evento do veículo chega, preenche os campos de estado sem tocar na posição.

### 7. Delete

- Na mesma transação: `DEL vehicle:{id}` e `SET deleted:{id} 1`.
- Delete não compara versão e é terminal.
- Estado e posição posteriores são ignorados enquanto o marcador existir (`IgnoreDeleted`).
- O marcador não tem TTL: um TTL permitiria ressuscitar o veículo durante um replay.
- Tombstone do Kafka é apenas ignorado; a informação já veio no `op:"d"`.
- `DECOMMISSIONED` é um estado comum e não equivale a DELETE físico.
- Premissa aceita: **ids são únicos e imutáveis** (um id deletado nunca é reutilizado).

### 8. Snapshot versus streaming

Eventos `op=r` (snapshot) e `op=c/u` são tratados pela mesma regra de versão. No snapshot, linhas podem ter LSN repetido; o desempate fica determinístico (primeiro aplicado vence quando timestamp e LSN são iguais), o que também vale no replay.

O E2E verifica se o conector já existe, semeia `snapshot-pre` e, em cluster novo, registra o conector Debezium antes de confirmar eventos `op=r` nos dois tópicos; depois aplica `snapshot-post` por streaming. Se o conector já existir no cluster reutilizado, a recipe avisa que o snapshot inicial não será repetido e continua os cenários por streaming. Para validar o snapshot novamente, use um cluster limpo (`just down && just e2e`).

### 9. Replay

- Replay **no mesmo processo e no mesmo código** de projeção: `Replay:Enabled=true` altera apenas o group id (`<grupo>-replay-<sufixo>`, sufixo padrão = timestamp UTC + GUID, garantindo grupo novo por execução e leitura do offset 0) e o banco lógico do Valkey.
- Destino do replay: **banco lógico do Valkey** (`Valkey:Database`), não prefixo de chave. Motivo: não altera `ValkeyKeys` nem os writers, e as chaves ficam idênticas dos dois lados, tornando a comparação trivial.
- Trava: com `Replay:Enabled=true` e `Database=0`, o worker **recusa subir**, para nunca sobrescrever o cache real.
- Retenção infinita nos tópicos, sem compaction, para o log ser fonte completa.
- `verify-replay` compara db 0 e db 1 (`vehicle:*` e `deleted:*`), exige **duas leituras consecutivas idênticas** (evita comparar no meio da convergência) e **falha se a origem estiver vazia** (evita "idêntico" vazio).
- Limitação aceita: bancos lógicos não existem em Valkey Cluster (só db 0). O ambiente é single node.

### 10. Particionamento

3 partições por tópico, key `vehicle_id`. A ordem é garantida **apenas por veículo dentro de um tópico**. Entre `vehicles` e `positions` não há garantia nenhuma, e o design não depende dela.

### 11. Poison message

Log, skip e commit. Em produção, enviar a uma DLQ. JSON malformatado. campos ausentes/nulos. operações ou estados não suportados, timestamps inválidos e lat/lng fora de faixa são inválidos e não podem travar a partição. Tombstone Kafka (value `null`) é tratado separadamente e ignorado.

### 12. Bibliotecas e ambiente

| Escolha | Motivo |
|---|---|
| PostgreSQL 17, `wal_level=logical`, `REPLICA IDENTITY FULL` em `vehicles` | `before` completo no delete |
| Debezium 3.0 (Connect), pgoutput, JSON sem schema | Menos peso no payload; envelope já validado |
| Kafka 3.9 KRaft, 1 nó | Suficiente para o ambiente local |
| Valkey 8 sem persistência (`--save "" --appendonly no`) | O cache é derivado; a fonte da verdade é o log |
| Confluent.Kafka | Controle explícito de commit e `Seek`; KafkaFlow esconderia exatamente o que é preciso defender |
| StackExchange.Redis | Transações com `AddCondition`; compatível com Valkey |
| xUnit v3 + NSubstitute | Runner Microsoft.Testing.Platform (`global.json`) |
| kind + devspace + just (devenv) | Exigido pelo enunciado; docker-compose não atende |

Sem FK em `positions.vehicle_id` (necessário para gerar a corrida) e sem unique em `positions` (necessário para gerar duplicatas).

## Alternativas consideradas

| Alternativa | Por que foi rejeitada |
|---|---|
| Ordenar só por LSN | LSN reflete a ordem de commit, não o tempo do evento de negócio; `updated_at` e `recorded_at` dizem qual é o dado mais novo para o leitor. O LSN fica como desempate |
| `SET` cego | Um evento antigo sobrescreveria o novo |
| Lua / Valkey Functions | Não dominado pelo autor, testabilidade pior, segunda linguagem. Mantido como **fallback**: números double têm 53 bits e o LSN tem 64; exigiria cuidado com cluster/slots, cache de EVALSHA e depuração |
| Lock distribuído, flag ou semáforo | TTL reabre a janela do pod pausado; mais idas e voltas; não decide ordenação |
| `DEL` puro no delete | Evento tardio recriaria o veículo |
| `deleted=1` dentro do hash | Hash sem estado seria ambíguo com "posição antes do veículo" |
| TTL no marcador `deleted:` | Quebraria o replay |
| Prefixo de chave para replay | Mudaria `ValkeyKeys` e todos os writers; comparação mais complexa |
| Segundo pod para o replay (ideia inicial) | Substituído por replay no mesmo processo: mesmo código, só mudam group id e banco |
| Coluna `version` | Altera o modelo de origem |
| Mediator | Indireção sem ganho neste tamanho |

## Consequências e trade-offs aceitos

- Cada escrita no Valkey custa leitura + transação (duas idas e voltas). Aceito pela simplicidade e testabilidade.
- Disputa muito alta no mesmo veículo pode esgotar os 5 retries e atrasar a partição (a mensagem é reprocessada, não perdida).
- Ids reutilizados após delete ficariam bloqueados pelo marcador. Premissa de unicidade aceita.
- Valkey sem persistência: após perda do Valkey, recuperação é por replay do Kafka. A retenção infinita é o que torna isso possível; em produção seria preciso avaliar custo de armazenamento ou compaction com snapshot.
- Replay por banco lógico não funciona em cluster.
- Replay "a partir de uma data" (offset por timestamp) não foi implementado.

## Pendências e cortes

Registrado de forma explícita porque nem tudo foi validado:

- **Não validado ponta a ponta no cluster:** `Dockerfile` (imagem única worker + mockwriters), `worker.yaml`, `worker-replay.yaml` e recipes `image`, `mock-job`, `replay`, `e2e` do `justfile`. O build da solução passa e a execução atual teve 175 testes unitários aprovados e 0 falhas.
- **Testes de integração não incluídos por decisão consciente:** O enunciado não os exige. O risco aceito é que a atomicidade de `AddCondition` contra o Valkey real e o `ConsumerLoop` contra o kafka real não foram validados com esses serviços. Os testes unitários usam mocks.
- `assert-view` não confere `posTs`/`stateTs` e usa chaves em literal (não `ValkeyKeys`).
- Em `positions`, mesmo `recorded_at` **e** mesmo LSN (possível no snapshot) é tratado como duplicata. Alternativa em aberto: usar o `id` da linha como terceiro desempate.
- O snapshot inicial só é exercitado com o connector novo; em cluster reutilizado a recipe avisa e segue pelo streaming.
- O E2E reinicia o worker durante `seed-stream` (`rate=40`, após o primeiro log `progresso`), mas não é determinístico quanto a haver reentrega; o e2e prova o resultado final, não que `IgnoreDuplicate` ocorreu.
- Nível de log de `IgnoreDeleted` em veículos (hoje Warning) é ruído no replay.