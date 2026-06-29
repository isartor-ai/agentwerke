# Deploying Autofac in production

Two supported production paths. Both run with **real authentication only** (no
dev tokens / dev identity), secrets supplied from your environment or secret
manager, and resource limits set.

| Path | Use when | Where |
| --- | --- | --- |
| **Kubernetes (Helm)** | HA, autoscaling, stronger sandbox isolation | [`helm/autofac`](helm/autofac) |
| **Docker Compose** | Single host / simpler setups | [`../docker/docker-compose.prod.yml`](../docker/docker-compose.prod.yml) |

See also [`../docs/deployment.md`](../docs/deployment.md) and
[`../docs/deployment-auth-data-residency.md`](../docs/deployment-auth-data-residency.md)
(SSO/OIDC + data residency), and the observability assets in
[`grafana/`](grafana) and [`prometheus/`](prometheus).

> Put a TLS-terminating proxy/ingress in front of the API and Web — never expose
> the dev JWT settings or unencrypted endpoints in production.

## Kubernetes (Helm)

The chart deploys the API, the dispatch **worker**, the Web SPA, and (optionally)
Postgres, with HPA, RBAC, and an ingress with TLS.

```bash
# 1. Create the shared secret the chart references (values: secretName=autofac-secrets)
kubectl create namespace autofac
kubectl -n autofac create secret generic autofac-secrets \
  --from-literal=POSTGRES_PASSWORD='<db-password>' \
  --from-literal=JWT_SECRET='<hs256-key-or-omit-if-OIDC>' \
  --from-literal=ANTHROPIC_API_KEY='sk-ant-...' \
  # --from-literal=OPEN_SANDBOX_API_KEY='...'   # if sandbox.provider=opensandbox

# 2. Install / upgrade
helm upgrade --install autofac deploy/helm/autofac -n autofac \
  --set api.image.tag=<version> --set web.image.tag=<version> --set worker.image.tag=<version> \
  --set api.ingress.enabled=true --set api.ingress.host=autofac.example.com
```

Key `values.yaml` knobs:
- **Auth:** `ASPNETCORE_ENVIRONMENT=Production` by default (dev tokens off). For SSO,
  set `Jwt__Authority` via env/secret (see the auth doc); otherwise provide `JWT_SECRET`.
- **Ingress/TLS:** `api.ingress.enabled`, `.host`, `.tlsSecretName` (`autofac-tls`).
- **Scaling:** `api.autoscaling` / `worker.autoscaling` (HPA).
- **Database:** `postgres.enabled=false` to use a managed/external Postgres.
- **Sandbox:** `sandbox.provider` — use `opensandbox` with `sandbox.openSandbox.serverUrl`
  for real isolation (the `docker` provider is for local dev only). See ADR-003.

## Docker Compose (single host)

```bash
# Provide secrets via a root-owned .env file or your secret manager, then:
docker compose -f docker/docker-compose.prod.yml up -d
```

It pulls pinned published images (`ghcr.io/isartor-ai/autofac/{api,web}:${AUTOFAC_VERSION:-latest}`),
runs migrations, binds API/Web to `127.0.0.1` (put your TLS proxy in front), and
sets `Jwt__DevTokensEnabled=false` / `Jwt__DevIdentityEnabled=false`. Required env
is listed in the file header.

> The Compose path uses the Docker sandbox provider (or none); for kernel-level
> sandbox isolation prefer the Kubernetes path with OpenSandbox.
