# Sample Module 1 (test module)

Minimal module used to exercise the platform's **install lifecycle** (publish → install → seed → gateway routes → shell menu) and **permission-gated UI**. Its page shows an open "Get sample data" button and a "Get protected data" button that is only enabled when the user holds `SAMPLE_ONE_DATA_PROTECTED`.

| Part | Location | Port |
|------|----------|------|
| API | `src/Modules/Sample1/Sample1.Api` | 5202 |
| Remote SPA (federation) | `modules/sample1/web` | 5176 (`http://localhost:5176/assets/remoteEntry.js`) |
| Manifest | `modules/sample1/manifest.yml` | — |

API endpoints: `GET /healthz`, `GET /readyz`, `POST /internal/seed:preview`, `POST /internal/migrate`, `GET /api/v1/data`, `GET /api/v1/protected`.
Through the gateway (:5100): `/api/v1/sample1/data`, `/api/v1/sample1/protected`.

## Run

Aspire (`dotnet run --project src/Aspire/ModulePlatform.AppHost`) starts both parts. Standalone:

```bash
dotnet run --project src/Modules/Sample1/Sample1.Api
cd modules/sample1/web && npm install && npm run serve   # build + vite preview on :5176
```

Config: `Jwt:SecretKey` (required outside Development), `Platform:BaseUrl` (default `http://localhost:5121`), `Auth:BaseUrl` (default `http://localhost:5122`).
Docker: `docker compose up sample1 sample1-web`.

## Publish & install

```bash
# Platform admin token
TOKEN=...            # platform admin bearer token
TENANT_ID=...        # target tenant id

# Publish (manifest file contents JSON-encoded)
curl -X POST http://localhost:5100/api/v1/platform/modules \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d "$(jq -n --rawfile m modules/sample1/manifest.yml '{manifestYaml: $m, artifactApiUrl: "http://localhost:5202"}')"

# Install for a tenant
curl -X POST http://localhost:5100/api/v1/platform/tenants/$TENANT_ID/modules/sample1/install \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" -d '{}'
```

Seeded on install:
- Permissions: `SAMPLE_ONE_DATA_VIEW`, `SAMPLE_ONE_DATA_PROTECTED`
- Role: **Sample 1 Viewer** (holds both permissions)

## Test

1. Open Tenant Admin at http://localhost:5175 → **Home** → **Sample Module 1**.
2. "Get sample data" works for any signed-in user; "Get protected data" is disabled.
3. In **Users**, assign the **Sample 1 Viewer** role to your user, sign in again → the protected button is enabled and `/api/v1/sample1/protected` returns data.

Full walkthrough: [quickstart](../../../specs/001-sample-test-modules/quickstart.md).
