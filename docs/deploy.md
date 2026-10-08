# Running the AdresTR API

The API is a stateless ASP.NET Core container (`ghcr.io/alpercna/adrestr-api`, linux-x64 and linux-arm64,
non-root, chiseled, ~190 MB). It stores and logs nothing (ADR-0009). Interactive docs are served at `/scalar/v1`,
the OpenAPI document at `/openapi/v1.json`.

## Self-host (recommended for production)

```bash
docker run -d --name adrestr -p 8080:8080 ghcr.io/alpercna/adrestr-api:0.1.0-preview.1
curl -s -X POST http://localhost:8080/v1/parse -H "Content-Type: application/json" -d "{\"text\":\"Moda Kadıköy\"}"
```

| Setting (environment variable) | Default | Meaning |
|---|---|---|
| `RateLimiting__PermitLimit` | `60` | Requests per IP per minute on `/v1/*` |
| `ASPNETCORE_HTTP_PORTS` | `8080` | Listening port |

Running inside your own network keeps addresses (personal data under KVKK) on your infrastructure.

## Endpoints

| Method | Path | Purpose |
|---|---|---|
| POST | `/v1/parse` | One address → components, resolved il/ilçe/birim, candidates, corrections, confidence |
| POST | `/v1/parse/batch` | Up to 1,000 addresses |
| POST | `/v1/parse/csv?column=adres` | Clean the address column of a CSV (≤ 5 MB) |
| POST | `/v1/validate` | Check a structured il / ilçe / mahalle / postal code combination |
| GET | `/v1/provinces`, `/v1/provinces/{plaka}/districts`, `/v1/districts/{id}/units`, `/v1/units/{id}` | Reference data |
| GET | `/v1/autocomplete?q=&level=province\|district\|unit&plaka=&districtId=` | Prefix suggestions |
| GET | `/health/live`, `/health/ready`, `/privacy` | Operations |

## Azure Container Apps (optional public demo, scale to zero)

Requires an Azure subscription. With the monthly free grant (180,000 vCPU-s, 2M requests) a low-traffic demo costs
$0; set a budget alert anyway. Hidden costs to avoid: Log Analytics ingestion (`--logs-destination none`) and a
private registry (pull the public GHCR image instead). Hosting outside Türkiye is a cross-border transfer under KVKK
Article 9; the playground (browser-only) is the privacy-friendly public demo.

```bash
RG=rg-adrestr; LOC=westeurope; ENV=cae-adrestr; APP=adrestr-api
az login
az extension add --name containerapp --upgrade
az provider register --namespace Microsoft.App --wait
az group create -n $RG -l $LOC
az containerapp env create -n $ENV -g $RG -l $LOC --logs-destination none
az containerapp create -n $APP -g $RG --environment $ENV \
  --image ghcr.io/alpercna/adrestr-api:0.1.0-preview.1 \
  --ingress external --target-port 8080 --cpu 0.25 --memory 0.5Gi \
  --min-replicas 0 --max-replicas 1 \
  --scale-rule-name http --scale-rule-type http --scale-rule-http-concurrency 50
```

New versions: `az containerapp update -n $APP -g $RG --image ghcr.io/alpercna/adrestr-api:<version>`.
(Automating this from `release.yml` needs a federated identity; see docs/research/teslimat-altyapisi.md §4.2.)

## Render (alternative)

Create a *Web Service* from the existing image `ghcr.io/alpercna/adrestr-api:<version>`, port 8080, free instance.
Free instances sleep after 15 minutes of inactivity.
