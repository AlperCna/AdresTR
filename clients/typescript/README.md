# adrestr-client

TypeScript client (ESM, zero runtime dependencies, uses `fetch`) for the [AdresTR](https://github.com/AlperCna/AdresTR) REST API: parse, normalize, match and validate Turkish addresses.

```bash
docker run -p 8080:8080 ghcr.io/alpercna/adrestr-api:latest   # run the API yourself (addresses are personal data)
npm install adrestr-client
```

```ts
import { AdresTRClient } from "adrestr-client";

const client = new AdresTRClient({ baseUrl: "http://localhost:8080" });

const r = await client.parse("kadikoy caferaga mh moda cd no:12 d3 istanbul");
r.canonical;               // "Caferağa Mah. Moda Cad. No:12 D:3 34710 Kadıköy/İstanbul"
r.resolved.birim?.id;      // 34230005

(await client.match("Moda Kadıköy", "Caferağa Mahallesi Kadıköy")).level;   // "SameUnit"
await client.dedupe(["...", "...", "..."]);                                 // [[0, 2], [1]]
await client.validate({ il: "İzmir", ilce: "Konak", postaKodu: "35220" });
```

Methods: `parse(text, { trace })`, `parseBatch`, `parseCsv`, `validate`, `match`, `dedupe`, `provinces`, `districts`, `units`, `unit`,
`autocomplete`. Failures throw `AdresTRError` (`status`, `title`, `detail`, `errors`). Options: `baseUrl`, `fetch`, `headers`, `timeoutMs`. MIT licensed.
