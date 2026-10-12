# adrestr-client

Python client for the [AdresTR](https://github.com/AlperCna/AdresTR) REST API: parse, normalize, match and validate Turkish addresses.

Run the API yourself (recommended; addresses are personal data under KVKK):

```bash
docker run -p 8080:8080 ghcr.io/alpercna/adrestr-api:latest
pip install adrestr-client
```

```python
from adrestr_client import AdresTRClient

with AdresTRClient("http://localhost:8080") as client:
    r = client.parse("kadikoy caferaga mh moda cd no:12 d3 istanbul")
    r["canonical"]                      # 'Caferağa Mah. Moda Cad. No:12 D:3 34710 Kadıköy/İstanbul'
    r["resolved"]["birim"]["id"]        # 34230005
    client.match("Moda Kadıköy", "Caferağa Mahallesi Kadıköy")["level"]   # 'SameUnit'
    client.dedupe(["...", "...", "..."])                                  # [[0, 2], [1]]
    client.validate(il="İzmir", ilce="Konak", posta_kodu="35220")["valid"]
```

`AsyncAdresTRClient` has the same methods as coroutines. Errors raise `AdresTRError` (`status`, `title`, `detail`, `errors`).
Methods: `parse(text, trace=False)`, `parse_batch`, `parse_csv`, `validate`, `match`, `dedupe`, `provinces`, `districts`,
`units`, `unit`, `autocomplete`. MIT licensed.
