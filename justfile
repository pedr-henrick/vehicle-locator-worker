set shell := ["bash", "-euo", "pipefail", "-c"]

cluster := "fleet"
ns      := "fleet"
ctx     := "kind-fleet"

# Sobe tudo do zero (idempotente)
up: cluster namespace deploy wait

cluster:
    kind get clusters | grep -qx {{cluster}} \
      || kind create cluster --config deploy/kind/kind-config.yaml

namespace:
    kubectl --context {{ctx}} create namespace {{ns}} --dry-run=client -o yaml \
      | kubectl --context {{ctx}} apply -f -

deploy:
    devspace deploy --kube-context {{ctx}} --namespace {{ns}}

wait:
    kubectl --context {{ctx}} -n {{ns}} rollout status deploy/smoke --timeout=120s

# Derruba o cluster inteiro
down:
    kind delete cluster --name {{cluster}}

# Placeholder; será preenchido no fim
e2e:
    @echo "e2e ainda não implementado"