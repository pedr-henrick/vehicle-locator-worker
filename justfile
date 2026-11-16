set shell := ["bash", "-euo", "pipefail", "-c"]

cluster := "fleet"
ns      := "fleet"
ctx     := "kind-fleet"

up: cluster namespace configs deploy wait create-topics connector

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

deploy:
    devspace deploy --kube-context {{ctx}} --namespace {{ns}}

wait:
    kubectl --context {{ctx}} -n {{ns}} rollout status statefulset/postgres --timeout=900s
    kubectl --context {{ctx}} -n {{ns}} rollout status statefulset/kafka --timeout=900s
    kubectl --context {{ctx}} -n {{ns}} rollout status deploy/valkey --timeout=900s
    kubectl --context {{ctx}} -n {{ns}} rollout status deploy/connect --timeout=900s

# Tópicos criados antes do connector: 3 partições, retenção infinita, sem compaction
create-topics:
    for t in fleet.public.vehicles fleet.public.positions; do \
      kubectl --context {{ctx}} -n {{ns}} exec kafka-0 -- \
        /opt/kafka/bin/kafka-topics.sh --bootstrap-server localhost:9092 \
        --create --if-not-exists --topic $t \
        --partitions 3 --replication-factor 1 \
        --config retention.ms=-1 --config retention.bytes=-1 \
        --config cleanup.policy=delete; \
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

e2e:
    @echo "e2e ainda não implementado"