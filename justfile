set shell := ["bash", "-euo", "pipefail", "-c"]

cluster := "fleet"
ns      := "fleet"
ctx     := "kind-fleet"

up: cluster namespace configs deploy wait

cluster:
    kind get clusters | grep -qx {{cluster}} \
      || kind create cluster --config deploy/kind/kind-config.yaml

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
    kubectl --context {{ctx}} -n {{ns}} rollout status statefulset/postgres --timeout=180s

down:
    kind delete cluster --name {{cluster}}

# Atalho: just psql "SELECT * FROM vehicles"
psql query:
    kubectl --context {{ctx}} -n {{ns}} exec postgres-0 -- \
      psql -U fleet -d fleet -c "{{query}}"

e2e:
    @echo "e2e ainda não implementado"