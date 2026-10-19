# Sample Module 2 (test module)

Minimal module used to exercise the platform's **install lifecycle** (publish → install → seed → gateway routes → shell menu) and **permission-gated UI**. Its page shows an open "Get sample data" button and a "Get protected data" button that is only enabled when the user holds `SAMPLE_TWO_DATA_PROTECTED`.

| Part | Location | Port |
|------|----------|------|
| API | `src/Modules/Sample2/Sample2.Api` | 5203 |
| Remote SPA (federation) | `modules/sample2/web` | 5177 (`http://localhost:5177/assets/remoteEntry.js`) |
| Manifest | `modules/sample2/manifest.yml` | — |

API endpoints: `GET /healthz`, `GET /readyz`, `POST /internal/seed:preview`, `POST /internal/migrate`, `GET /api/v1/data`, `GET /api/v1/protected`.
Through the gateway (:5100): `/api/v1/sample2/data`, `/api/v1/sample2/protected`.

## Run

Aspire (`dotnet run --project src/Aspire/ModulePlatform.AppHost`) starts both parts. Standalone:

```bash
dotnet run --project src/Modules/Sample2/Sample2.Api
cd modules/sample2/web && npm install && npm run serve   # build + vite preview on :5177
```

Config: `Jwt:SecretKey` (required outside Development), `Platform:BaseUrl` (default `http://localhost:5121`), `Auth:BaseUrl` (default `http://localhost:5122`).
Docker: `docker compose up sample2 sample2-web`.

## Publish & install

```bash
# Platform admin token
TOKEN=...            # platform admin bearer token
TENANT_ID=...        # target tenant id

# Publish (manifest file contents JSON-encoded)
curl -X POST http://localhost:5100/api/v1/platform/modules \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d "$(jq -n --rawfile m modules/sample2/manifest.yml '{manifestYaml: $m, artifactApiUrl: "http://localhost:5203"}')"

# Install for a tenant
curl -X POST http://localhost:5100/api/v1/platform/tenants/$TENANT_ID/modules/sample2/install \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" -d '{}'
```

Seeded on install:
- Permissions: `SAMPLE_TWO_DATA_VIEW`, `SAMPLE_TWO_DATA_PROTECTED`
- Role: **Sample 2 Viewer** (holds both permissions)

## Test

1. Open Tenant Admin at http://localhost:5175 → **Home** → **Sample Module 2**.
2. "Get sample data" works for any signed-in user; "Get protected data" is disabled.
3. In **Users**, assign the **Sample 2 Viewer** role to your user, sign in again → the protected button is enabled and `/api/v1/sample2/protected` returns data.

Full walkthrough: [quickstart](../../../specs/001-sample-test-modules/quickstart.md).
