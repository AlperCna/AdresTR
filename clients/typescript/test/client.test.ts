import { describe, expect, it } from "vitest";
import { AdresTRClient, AdresTRError } from "../src/index.js";

interface Call { url: string; method: string; body?: string; headers: Record<string, string> }

function mock(respond: (call: Call) => Response) {
  const calls: Call[] = [];
  const fetchMock = (async (input: RequestInfo | URL, init?: RequestInit) => {
    const call: Call = { url: String(input), method: init?.method ?? "GET", body: init?.body as string | undefined, headers: init?.headers as Record<string, string> };
    calls.push(call);
    return respond(call);
  }) as typeof fetch;
  return { client: new AdresTRClient({ baseUrl: "http://test/", fetch: fetchMock }), calls };
}

const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { "Content-Type": "application/json" } });

describe("AdresTRClient", () => {
  it("parses with and without trace", async () => {
    const { client, calls } = mock(() => json({ input: "Moda", canonical: "x" }));
    await client.parse("Moda");
    await client.parse("Moda", { trace: true });
    expect(calls[0]).toMatchObject({ url: "http://test/v1/parse", method: "POST", body: '{"text":"Moda"}' });
    expect(calls[0].headers["Content-Type"]).toBe("application/json");
    expect(calls[1].url).toBe("http://test/v1/parse?trace=true");
  });

  it("batch, match, dedupe and validate", async () => {
    const { client, calls } = mock((c) =>
      c.url.endsWith("/batch") ? json({ results: [{ input: "a" }, { input: "b" }] })
      : c.url.endsWith("/dedupe") ? json({ groups: [[0, 2], [1]] })
      : c.url.endsWith("/match") ? json({ level: "SameFlat", score: 1, reasons: [] })
      : json({ valid: true, problems: [] }));
    expect((await client.parseBatch(["a", "b"])).map((r) => r.input)).toEqual(["a", "b"]);
    expect(await client.dedupe(["a", "b", "c"], { minimum: "SameStreet" })).toEqual([[0, 2], [1]]);
    expect(JSON.parse(calls[1].body!)).toEqual({ items: ["a", "b", "c"], minimum: "SameStreet" });
    expect((await client.match("a", "b")).level).toBe("SameFlat");
    expect((await client.validate({ il: "İzmir", postaKodu: "35220" })).valid).toBe(true);
    expect(JSON.parse(calls[3].body!)).toEqual({ il: "İzmir", postaKodu: "35220" });
  });

  it("cleans CSV as text", async () => {
    const { client, calls } = mock(() => new Response("id,adres,il\n1,x,İzmir\n", { headers: { "Content-Type": "text/csv" } }));
    expect(await client.parseCsv("id,adres\n1,x\n", "adres sütunu")).toBe("id,adres,il\n1,x,İzmir\n");
    expect(calls[0].url).toBe("http://test/v1/parse/csv?column=adres%20s%C3%BCtunu");
    expect(calls[0].headers["Content-Type"]).toBe("text/csv; charset=utf-8");
  });

  it("reads reference data and autocompletes", async () => {
    const { client, calls } = mock(() => json([{ id: 1 }]));
    await client.provinces();
    await client.districts(34);
    await client.units(3423);
    await client.unit(34230005);
    await client.autocomplete("cafer", { level: "unit", districtId: 3423 });
    expect(calls.map((c) => c.url.replace("http://test", ""))).toEqual([
      "/v1/provinces", "/v1/provinces/34/districts", "/v1/districts/3423/units", "/v1/units/34230005",
      "/v1/autocomplete?q=cafer&level=unit&districtId=3423",
    ]);
  });

  it("throws AdresTRError with problem details", async () => {
    const { client } = mock(() => json({ title: "Validation", errors: { text: ["too long"] } }, 400));
    await expect(client.parse("x")).rejects.toMatchObject({ name: "AdresTRError", status: 400, errors: { text: ["too long"] } });
    const plain = mock(() => new Response("Bad gateway", { status: 502, statusText: "Bad Gateway" }));
    await expect(plain.client.parse("x")).rejects.toBeInstanceOf(AdresTRError);
  });
});
