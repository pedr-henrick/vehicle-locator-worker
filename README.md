# Vehicle Locator Worker: cache de veículos via CDC

Worker .NET 10 que consome dois tópicos CDC (Debezium → Kafka), `vehicles` e `positions`, e mantém no Valkey a **última posição** e o **estado atual** de cada veículo. O cache é construído **somente a partir do CDC**: o worker nunca consulta o PostgreSQL. Um replay do offset 0 reconstrói um cache idêntico.

Desafio técnico da equipe Data Platform da NsTech.

## Os dois comandos que importam

```bash
devenv shell -- just up     # sobe o ambiente completo
devenv shell -- just e2e    # roda o fluxo ponta a ponta (já inclui `up`)
```

Ou, dentro de `devenv shell`, apenas `just up` e `just e2e`.

`just e2e` termina com código diferente de zero em qualquer falha e, ao final, imprime `E2E OK: view correta (db 0) e replay idêntico (db 1).`

Para remover tudo: `just down`.

## Pré-requisitos

- **Docker** em execução (Docker Desktop ou Docker Engine).
- **Nix** com flakes habilitados e **devenv**. O `devenv` fornece o resto: .NET SDK 10, kind, kubectl, devspace, just, helm, jq e psql. Não instale essas ferramentas à parte.
- Windows: use **WSL2** (Ubuntu) com a integração do Docker Desktop, e clone o repositório no sistema de arquivos do Linux (por exemplo `~/projetos/`), não em `/mnt/c/`. Em `/mnt/c/` o build é lento e o Visual Studio do Windows disputa `bin/` e `obj/` com o dotnet do WSL.
- Memória: o cluster roda Postgres, Kafka, Kafka Connect, Valkey e o worker. Reserve alguns GB para o Docker.

O primeiro `just up` baixa imagens grandes (a do Kafka Connect tem cerca de 700 MB) e leva vários minutos. Os `rollout status` esperam até 600 s por recurso. As execuções seguintes são bem mais rápidas.

## O que o `just e2e` faz

1. `up`: cluster kind `fleet`, namespace, ConfigMaps, build e carga da imagem única (worker + mock writers), deploy (devspace), espera, criação dos tópicos (3 partições, retenção infinita) e registro do connector Debezium.
2. `seed-a` (Job): veículos (`lifecycle`, `out-of-order`, `delete`) e `volume` (200 posições embaralhadas, com duplicatas).
3. `restart-worker`: apaga o pod do worker logo após o grupo A; o Deployment o recria e ele retoma dos offsets commitados.
4. `seed-b` (Job): cenários de posição (fora de ordem, duplicada, antes do veículo, depois do delete).
5. `assert`: Job `assert-view` espera o db 0 convergir (até 180 s) e compara com os valores esperados.
6. `replay`: limpa o db 1, sobe um worker em modo replay (grupo novo, offset 0, db 1), roda `verify-replay` (db 0 × db 1) e remove o worker de replay, mesmo em caso de falha.

O replay só é comparado depois de o db 0 bater com o esperado; senão "idêntico" provaria pouco.

## Documentos

| Documento | Conteúdo |
|---|---|
| [`docs/c4.md`](docs/c4.md) | C4 em Mermaid: Context, Container, Component do worker |
| [`docs/spec.md`](docs/spec.md) | Read-model, contrato CDC, schema do Valkey, componentes, sequenciamento e cortes |
| [`docs/test-behaviors.md`](docs/test-behaviors.md) | Given/When/Then dos comportamentos obrigatórios e status de automação |
| [`docs/adr/0001-cdc-vehicle-cache.md`](docs/adr/0001-cdc-vehicle-cache.md) | Decisões, alternativas e trade-offs |

## Em uma frase, as decisões principais

- **Ordenação:** estado por `(updated_at µs, LSN)`; posição por `(recorded_at µs, LSN)`. Escritas condicionais (transação otimista com `AddCondition`), nunca `SET` cego.
- **Idempotência:** vem da regra de versão; consumo at-least-once e offset commitado só **depois** de gravar.
- **Merge entre streams:** estado e posição em campos disjuntos do mesmo hash, então a ordem de chegada entre tópicos não importa. Posição antes do veículo não presume estado.
- **Delete:** `DEL` do hash + marcador `deleted:{id}` sem TTL, na mesma transação.
- **Replay:** mesmo código, em banco lógico separado do Valkey, com trava contra sobrescrever o db 0.

## Testes

```bash
devenv shell -- dotnet test
```

xUnit v3 com Microsoft.Testing.Platform (`global.json`) e NSubstitute. Cobrem parsers com mensagens reais, deciders, convergência por **todas as permutações** de eventos (inclusive entre os dois streams), casos de uso, `ConsumerLoop` e o comparador de replay.

## Estrutura

```
src/VehicleLocator.Application/   parsers, deciders, writers, casos de uso, replay (comparação)
src/VehicleLocator.Worker/        consumers, ConsumerLoop, configuração Kafka/Valkey/replay
src/VehicleLocator.MockWriters/   cenários, assert-view, verify-replay (escrevem só no PostgreSQL)
tests/VehicleLocator.UnitTests/   testes unitários
deploy/                           kind, manifests Kubernetes, connector Debezium
docs/                             C4, spec, test-behaviors, ADR
Dockerfile                        imagem única (worker + mock writers)
justfile, devspace.yaml, devenv.nix
```

## Utilitários de depuração

Exigem o cluster no ar. `just forward` (em outro terminal) expõe Kafka (9094), Valkey (6379) e Postgres (5432) em localhost.

| Comando | Para quê |
|---|---|
| `just psql "SELECT * FROM vehicles"` | consulta no Postgres |
| `just topics` / `just topic fleet.public.vehicles` | listar e ler tópicos |
| `just valkey "HGETALL vehicle:<id>"` | ver a view de um veículo |
| `just connector-status` | status do connector (procure `RUNNING`) |
| `just writer <cenario> <prefixo>` | roda um cenário a partir da sua máquina |
| `just verify-replay` | compara db 0 e db 1 a partir da sua máquina |

## Problemas comuns

- **`kind load docker-image` falha** ("content digest not found", comum com o containerd image store do Docker Desktop): a recipe `image` já tenta o plano B (`docker save` + `kind load image-archive`).
- **`Unable to find fallback package folder C:\Program Files...`**: restos de `bin/` e `obj/` do Visual Studio. Apague-os (`find . -type d \( -name obj -o -name bin \) -prune -exec rm -rf {} +`) e rode `dotnet restore` no WSL. Mover o repo para o Linux evita o problema.
- **Timeout no `wait`** no primeiro `up`: baixa de imagens lenta. Rode `just up` de novo; é idempotente.

## Status de validação e pendências

> **Atualize esta seção antes de abrir o PR.**

- Código compila e os testes unitários passam (130 na última execução).
- Validado manualmente com o worker local e port-forward: veículos (`lifecycle`, `out-of-order`, `delete`, restart), posições (todos os cenários) e replay em db 1 com `verify-replay` provado nos três casos (idêntico, chave ausente, conteúdo diferente).
- **Ainda não validado no cluster:** imagem única, `worker.yaml`, `worker-replay.yaml`, Jobs de mock e as recipes `image`, `mock-job`, `replay` e `e2e`. O `just e2e` completo ainda não foi executado de ponta a ponta, nem o cold start do zero.
- **Cortado por tempo:** testes de integração com Testcontainers (writers contra Valkey real e `ConsumerLoop` contra Kafka real). A concorrência entre leitura e `EXEC` é coberta só por mock.
- Outras limitações e ideias (replay a partir de uma data, `assert-view` sem `posTs`/`stateTs`, taxa configurável nos mocks) estão em "Pendências e cortes" no ADR e na seção 11 da Spec.