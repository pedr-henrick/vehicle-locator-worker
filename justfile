set shell := ["bash", "-euo", "pipefail", "-c"]

cluster      := "fleet"
ns           := "fleet"
ctx          := "kind-fleet"
wait_timeout := "600s"
image        := "vehicle-locator-worker:e2e"
job_timeout  := "300"

# Endereços internos do cluster (usados pelos Jobs de mock).
pg := "Host=postgres.fleet.svc.cluster.local;Port=5432;Username=fleet;Password=fleet;Database=fleet"
vk := "valkey.fleet.svc.cluster.local:6379"

# Prefixo único por execução do e2e: ids não colidem em reexecuções no mesmo cluster.
run_id := "e2e" + `date +%d%H%M%S`

infra: cluster namespace configs image deploy wait create-topics

up: infra connector

cluster:
    kind get clusters | grep -qx {{cluster}} \
      || kind create cluster --config deploy/kind/kind-config.yaml --wait 120s

namespace:
    kubectl --context {{ctx}} create namespace {{ns}} --dry-run=client -o yaml \
      | kubectl --context {{ctx}} apply -f -

# ConfigMaps gerados a partir de arquivos, antes do deploy
configs:
    kubectl --context {{ctx}} -n {{ns}} create configmap postgres-init \
      --from-file=deploy/k8s/postgres-init.sql --dry-run=client -o yaml \
      | kubectl --context {{ctx}} apply -f -

# Constrói a imagem única (worker + mockwriters) e carrega no kind.
# Plano B se o `kind load docker-image` falhar (containerd image store do Docker Desktop).
image:
    docker build -t {{image}} .
    kind load docker-image {{image}} --name {{cluster}} \
      || { docker save {{image}} -o /tmp/vehicle-locator-image.tar \
           && kind load image-archive /tmp/vehicle-locator-image.tar --name {{cluster}}; }

deploy:
    devspace deploy --kube-context {{ctx}} --namespace {{ns}}

# Timeout alto: o cold start baixa várias imagens ao mesmo tempo (Connect ≈ 700 MB).
wait:
    kubectl --context {{ctx}} -n {{ns}} rollout status statefulset/postgres --timeout={{wait_timeout}}
    kubectl --context {{ctx}} -n {{ns}} rollout status statefulset/kafka --timeout={{wait_timeout}}
    kubectl --context {{ctx}} -n {{ns}} rollout status deploy/valkey --timeout={{wait_timeout}}
    kubectl --context {{ctx}} -n {{ns}} rollout status deploy/connect --timeout={{wait_timeout}}
    kubectl --context {{ctx}} -n {{ns}} rollout status deploy/worker --timeout={{wait_timeout}}

# Tópicos criados antes do connector: 3 partições, retenção infinita, sem compaction
create-topics:
    for t in fleet.public.vehicles fleet.public.positions; do \
      kubectl --context {{ctx}} -n {{ns}} exec kafka-0 -- \
        /opt/kafka/bin/kafka-topics.sh --bootstrap-server localhost:9092 \
        --create --if-not-exists --topic $t \
        --partitions 3 --replication-factor 1 \
        --config retention.ms=-1 --config retention.bytes=-1 \
        --config cleanup.policy=delete; \
    description=$(kubectl --context {{ctx}} -n {{ns}} exec kafka-0 -- \
        /opt/kafka/bin/kafka-topics.sh --bootstrap-server localhost:9092 \
        --describe --topic $t); \
      echo "$description"; \
      if ! grep -Eq 'PartitionCount: 3([[:space:]]|$)' <<< "$description"; then \
        echo "Topic $t must have exactly 3 partitions" >&2; \
        exit 1; \
      fi; \
    done

# Registra (ou atualiza) o connector do Debezium. PUT /config é idempotente.
connector:
    kubectl --context {{ctx}} -n {{ns}} exec deploy/connect -- \
      curl -s --retry 20 --retry-delay 3 --retry-connrefused \
      -o /dev/null -w "%{http_code}\n" -X PUT \
      -H "Content-Type: application/json" \
      --data "$(jq -c .config deploy/connect/connector.json)" \
      http://localhost:8083/connectors/fleet-connector/config

down:
    kind delete cluster --name {{cluster}}

# ---------------------------------------------------------------- e2e

# Fluxo completo. Cada etapa é uma recipe; falha em qualquer uma interrompe com código != 0.
e2e: infra snapshot seed-a seed-stream-restart seed-b assert replay
    @echo "E2E OK: view correta (db 0) e replay idêntico (db 1)."


# Semear antes do connector para validar snapshot inicial; em cluster reutilizado,
# os snapshots ainda serão consumidos como streaming, mas não haverá um novo snapshot.
snapshot:
    #!/usr/bin/env bash
    set -euo pipefail
    K="kubectl --context {{ctx}} -n {{ns}}"
    JUST="{{just_executable()}}"
    connector_status=$($K exec deploy/connect -- curl -s -o /dev/null -w "%{http_code}\n" http://localhost:8083/connectors/fleet-connector)
    "$JUST" mock-job seed-snap-pre seed {{run_id}} snapshot-pre
    if [ "$connector_status" = 200 ]; then
      echo "Aviso: o conector já existia; o snapshot inicial não será repetido nesse cluster."
    else
      "$JUST" connector
      snapshot_found=false
      for attempt in $(seq 1 20); do
        vehicles=$($K exec kafka-0 -- /opt/kafka/bin/kafka-console-consumer.sh \
          --bootstrap-server localhost:9092 --topic fleet.public.vehicles \
          --from-beginning --timeout-ms 1500  --property print.key=true 2>/dev/null || true)
        positions=$($K exec kafka-0 -- /opt/kafka/bin/kafka-console-consumer.sh \
          --bootstrap-server localhost:9092 --topic fleet.public.positions \
          --from-beginning --timeout-ms 1500  --property print.key=true 2>/dev/null || true)
        if grep -F '{{run_id}}-snapshot-only-1' <<< "$vehicles" \
        | grep -Eq '"op"[[:space:]]*:[[:space:]]*"r"' \
        && grep -F '{{run_id}}-snapshot-only-1' <<< "$positions" \
        | grep -Eq '"op"[[:space:]]*:[[:space:]]*"r"'; then
          snapshot_found=true
          break
        fi
        sleep 3
      done
      if [ "$snapshot_found" != true ]; then
        echo "Não foi possível confirmar eventos op=r do snapshot inicial nos tópicos Kafka" >&2
        exit 1
      fi
    fi
    "$JUST" mock-job seed-snap-post seed {{run_id}} snapshot-post

# Grupo A: veículos + volume.
seed-a: (mock-job "seed-a" "seed" run_id "a")

# Grupo B: posições (inclui posição antes do veículo e depois do delete).
seed-b: (mock-job "seed-b" "seed" run_id "b")

# Reinicia o worker depois do primeiro progresso do stream, enquanto o Job continua gravando.
seed-stream-restart:
    #!/usr/bin/env bash
    set -euo pipefail
    K="kubectl --context {{ctx}} -n {{ns}}"
    JUST="{{just_executable()}}"
    "$JUST" mock-job-start seed-stream stream {{run_id}} rate=40
    deadline=$((SECONDS + {{job_timeout}}))
    while [ "$SECONDS" -lt "$deadline" ]; do
      logs=$($K logs job/seed-stream 2>/dev/null || true)
      if grep -q '^progresso:' <<< "$logs"; then
         echo "Primeiro progresso do stream observado; reiniciando o worker."
         $K delete pod -l app=worker --wait=false
         "$JUST" mock-job-await seed-stream
         exit 0
      fi
      failed=$($K get job seed-stream -o jsonpath='{.status.failed}')
      if [ "${failed:-0}" -ge 1 ]; then
        $K logs job/seed-stream || true
        echo "Job seed-stream FALHOU antes do primeiro progresso"
        exit 1
      fi
      succeeded=$($K get job seed-stream -o jsonpath='{.status.succeeded}')
      if [ "${succeeded:-0}" -ge 1 ]; then
        $K logs job/seed-stream || true
        echo "Job seed-stream terminou antes do primeiro progresso"
        exit 1
      fi
      sleep 1
    done
    $K logs job/seed-stream || true
    echo "Timeout aguardando o primeiro progresso do stream." >&2
    exit 1

# Só compara com o esperado depois que o db 0 convergiu (polling de até 180s).
assert: (mock-job "assert-view" "assert-view" run_id "180" "0")

# Replay: limpa o db 1, sobe o worker de replay (offset 0, db 1), compara db 0 x db 1
# e remove o worker de replay, mesmo em caso de falha.
replay:
    #!/usr/bin/env bash
    set -euo pipefail
    K="kubectl --context {{ctx}} -n {{ns}}"
    trap '$K delete -f deploy/k8s/e2e/worker-replay.yaml --ignore-not-found' EXIT
    $K exec deploy/valkey -- valkey-cli -n 1 FLUSHDB
    $K apply -f deploy/k8s/e2e/worker-replay.yaml
    $K rollout status deploy/worker-replay --timeout={{wait_timeout}}
    {{just_executable()}} mock-job verify-replay verify-replay 120 0 1

# Cria um Job com a imagem única, espera terminar e mostra o log.
# Ex.: just mock-job seed-a seed e2e01 a
mock-job name *args:
    #!/usr/bin/env bash
    set -euo pipefail
    JUST="{{just_executable()}}"
    "$JUST" mock-job-start {{name}} {{args}}
    "$JUST" mock-job-await {{name}}

# Cria um job sem esperar sua conclução
mock-job-start name *args:
    #!/usr/bin/env bash
    set -euo pipefail
    K="kubectl --context {{ctx}} -n {{ns}}"
    $K delete job {{name}} --ignore-not-found
    $K create job {{name}} --image={{image}} --dry-run=client -o json \
      -- dotnet /app/mockwriters/VehicleLocator.MockWriters.dll {{args}} \
      | jq --arg pg "{{pg}}" --arg vk "{{vk}}" \
          '.spec.backoffLimit = 0 | .spec.template.spec.containers[0].env = [{name:"POSTGRES",value:$pg},{name:"VALKEY",value:$vk}]' \
      | $K apply -f -
    
# Espera um Job e mostra o log
mock-job-await name:
    #!/usr/bin/env bash
    set -euo pipefail
    K="kubectl --context {{ctx}} -n {{ns}}"
    deadline=$((SECONDS + {{job_timeout}}))
    while true; do
      ok=$($K get job {{name}} -o jsonpath='{.status.succeeded}')
      ko=$($K get job {{name}} -o jsonpath='{.status.failed}')
      if [ "${ok:-0}" -ge 1 ]; then break; fi
      if [ "${ko:-0}" -ge 1 ]; then
        $K logs job/{{name}} || true
        echo "Job {{name}} FALHOU"
        exit 1
      fi
      if [ "$SECONDS" -ge "$deadline" ]; then
        $K logs job/{{name}} || true
        echo "Job {{name}}: timeout"
        exit 1
      fi
      sleep 2
    done
    $K logs job/{{name}}

# ---------------------------------------------------------------- utilitários (uso local)

# Expõe Kafka (9094), Valkey (6379) e Postgres (5432) em localhost.
# Deixe rodando em um terminal separado.
forward:
    kubectl --context {{ctx}} -n {{ns}} port-forward pod/kafka-0 9094:9094 & \
    kubectl --context {{ctx}} -n {{ns}} port-forward deploy/valkey 6379:6379 & \
    kubectl --context {{ctx}} -n {{ns}} port-forward pod/postgres-0 5432:5432 & \
    wait

# Roda um cenário do mock writer local. Ex.: just writer out-of-order
writer scenario="lifecycle" prefix="":
    dotnet run --project src/VehicleLocator.MockWriters -- {{scenario}} {{prefix}}

# Compara db 0 (cache real) com db 1 (replay) a partir da sua máquina. Exige `just forward`.
verify-replay timeout="60":
    dotnet run --project src/VehicleLocator.MockWriters -- verify-replay {{timeout}}

# Ex.: just psql "SELECT * FROM vehicles"
psql query:
    kubectl --context {{ctx}} -n {{ns}} exec postgres-0 -- \
      psql -U fleet -d fleet -c "{{query}}"

# Ex.: just topics
topics:
    kubectl --context {{ctx}} -n {{ns}} exec kafka-0 -- \
      /opt/kafka/bin/kafka-topics.sh --bootstrap-server localhost:9092 --list

# Ex.: just topic fleet.public.vehicles
topic name:
    kubectl --context {{ctx}} -n {{ns}} exec kafka-0 -- \
      /opt/kafka/bin/kafka-console-consumer.sh \
      --bootstrap-server localhost:9092 --topic {{name}} \
      --from-beginning --property print.key=true --property print.partition=true \
      --timeout-ms 5000

# Status do connector (procure por RUNNING; erros aparecem em "trace")
connector-status:
    kubectl --context {{ctx}} -n {{ns}} exec deploy/connect -- \
      curl -s http://localhost:8083/connectors/fleet-connector/status

# Ex.: just valkey "HGETALL vehicle:v-1"
valkey cmd:
    kubectl --context {{ctx}} -n {{ns}} exec deploy/valkey -- valkey-cli {{cmd}}