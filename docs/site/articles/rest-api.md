# REST API and self-hosting

```bash
docker run -p 8080:8080 ghcr.io/alpercna/adrestr-api:latest
```

Interactive documentation is served at `/scalar/v1` and the OpenAPI document at `/openapi/v1.json`.

| Method | Path | Purpose |
|---|---|---|
| POST | `/v1/parse` (`?trace=true`) | One address → components, resolved il/ilçe/birim, candidates, corrections, confidence |
| POST | `/v1/parse/batch` | Up to 1,000 addresses |
| POST | `/v1/parse/csv?column=adres` | Clean the address column of a CSV (≤ 5 MB) |
| POST | `/v1/validate` | Check a structured il / ilçe / mahalle / postal code |
| POST | `/v1/match`, `/v1/dedupe` | Same place? Duplicate groups |
| GET | `/v1/provinces`, `/v1/provinces/{plaka}/districts`, `/v1/districts/{id}/units`, `/v1/units/{id}` | Reference data |
| GET | `/v1/autocomplete` | Prefix suggestions |

Per-IP rate limiting (`RateLimiting__PermitLimit`, default 60 per minute), no request logging, non-root chiseled image
(linux-x64 and linux-arm64). Deployment options (Azure Container Apps, Render): [docs/deploy.md](https://github.com/AlperCna/AdresTR/blob/main/docs/deploy.md).
Clients: [Python](https://github.com/AlperCna/AdresTR/tree/main/clients/python), [TypeScript](https://github.com/AlperCna/AdresTR/tree/main/clients/typescript).
