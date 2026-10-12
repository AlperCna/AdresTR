/** TypeScript client for the AdresTR REST API (Turkish address parsing, matching and validation). */

export type MatchLevel = "Different" | "Unknown" | "SameDistrict" | "SameUnit" | "SameStreet" | "SameBuilding" | "SameFlat";
export type StreetType = "Cadde" | "Sokak" | "Bulvar" | "Yol" | "Meydan" | "Cikmaz" | "KumeEvler";
export type UnitKind = "Mahalle" | "Koy" | "Belde" | "Mezra" | "Mevki" | "Yayla" | "KumeEvler" | "Osb" | "Site" | "Diger";
export type CorrectionKind = "Diacritics" | "Typo" | "Semt" | "HistoricName" | "Alias" | "Split" | "Inferred" | "PostalCodeConflict" | "Duplicate";

export interface Component { value: string; text: string; start: number; end: number }
export interface Components {
  il: Component | null; ilce: Component | null; mahalle: Component | null; semt: Component | null;
  street: Component | null; streetType: StreetType | null; site: Component | null; blok: Component | null;
  doorNumber: Component | null; floor: Component | null; flat: Component | null;
  postalCode: Component | null; landmark: Component | null;
}
export interface Province { plaka: number; name: string }
export interface District { id: number; name: string; plaka: number }
export interface Unit {
  id: number; name: string; kind: UnitKind; parentName: string | null; postalCode: string | null;
  districtId: number; district: string; province: string; nviId: number | null; wikidata: string | null;
  latitude: number | null; longitude: number | null;
}
export interface Candidate { id: number; name: string; district: string; province: string; score: number }
export interface Correction { kind: CorrectionKind; field: string; from: string | null; to: string }
export interface TraceToken { text: string; start: number; end: number; folded: string; glued: boolean }
export interface TraceComponent { role: string; value: string; text: string; start: number; end: number; local: number }
export interface TraceAlternative {
  rank: number; score: number; probability: number; components: TraceComponent[];
  province: string | null; district: string | null; unit: string | null; unitCandidates: number;
}
export interface ParseTrace { tokens: TraceToken[]; hypothesisCount: number; alternatives: TraceAlternative[] }

export interface ParseResponse {
  input: string;
  canonical: string;
  confidence: number;
  components: Components;
  resolved: { il: Province | null; ilce: District | null; birim: Unit | null };
  candidates: Candidate[];
  corrections: Correction[];
  dataVersion: string;
  trace: ParseTrace | null;
}
export interface ValidateRequest { il?: string; ilce?: string; mahalle?: string; postaKodu?: string }
export interface ValidateResponse { valid: boolean; il: Province | null; ilce: District | null; birim: Unit | null; problems: string[] }
export interface MatchResponse { level: MatchLevel; score: number; reasons: string[]; a: ParseResponse; b: ParseResponse }
export interface Suggestion { level: string; id: number; name: string; context: string | null }
export type AutocompleteLevel = "province" | "district" | "unit";

/** Thrown for non-2xx responses; carries the RFC 7807 problem details when present. */
export class AdresTRError extends Error {
  constructor(
    public readonly status: number,
    public readonly title: string,
    public readonly detail?: string,
    public readonly errors: Record<string, string[]> = {},
  ) {
    super(`${status} ${title}${detail ? `: ${detail}` : ""}`);
    this.name = "AdresTRError";
  }
}

export interface ClientOptions {
  /** API base URL, default `http://localhost:8080`. */
  baseUrl?: string;
  /** Custom fetch (tests, proxies, older runtimes). Defaults to the global `fetch`. */
  fetch?: typeof fetch;
  /** Extra headers sent with every request. */
  headers?: Record<string, string>;
  /** Request timeout in milliseconds, default 30000. */
  timeoutMs?: number;
}

export class AdresTRClient {
  private readonly baseUrl: string;
  private readonly fetchImpl: typeof fetch;
  private readonly headers: Record<string, string>;
  private readonly timeoutMs: number;

  constructor(options: ClientOptions = {}) {
    this.baseUrl = (options.baseUrl ?? "http://localhost:8080").replace(/\/+$/, "");
    this.fetchImpl = options.fetch ?? ((...args) => fetch(...args));
    this.headers = options.headers ?? {};
    this.timeoutMs = options.timeoutMs ?? 30_000;
  }

  /** Parse one address. `trace: true` adds a `trace` object explaining the result. */
  parse(text: string, options: { trace?: boolean } = {}): Promise<ParseResponse> {
    return this.json("POST", `/v1/parse${options.trace ? "?trace=true" : ""}`, { text });
  }

  /** Parse up to 1,000 addresses; results are in request order. */
  async parseBatch(items: string[]): Promise<ParseResponse[]> {
    return (await this.json<{ results: ParseResponse[] }>("POST", "/v1/parse/batch", { items })).results;
  }

  /** Clean the address column (default `adres`) of a CSV; returns the CSV with appended columns. */
  parseCsv(csv: string, column?: string): Promise<string> {
    const query = column ? `?column=${encodeURIComponent(column)}` : "";
    return this.text("POST", `/v1/parse/csv${query}`, csv, "text/csv; charset=utf-8");
  }

  validate(request: ValidateRequest): Promise<ValidateResponse> {
    return this.json("POST", "/v1/validate", request);
  }

  /** Decide whether two addresses are the same place. */
  match(a: string, b: string): Promise<MatchResponse> {
    return this.json("POST", "/v1/match", { a, b });
  }

  /** Group duplicate addresses; returns groups of indexes into `items`. */
  async dedupe(items: string[], options: { minimum?: MatchLevel } = {}): Promise<number[][]> {
    return (await this.json<{ groups: number[][] }>("POST", "/v1/dedupe", { items, minimum: options.minimum })).groups;
  }

  provinces(): Promise<Province[]> { return this.json("GET", "/v1/provinces"); }
  districts(plaka: number): Promise<District[]> { return this.json("GET", `/v1/provinces/${plaka}/districts`); }
  units(districtId: number): Promise<Unit[]> { return this.json("GET", `/v1/districts/${districtId}/units`); }
  unit(id: number): Promise<Unit> { return this.json("GET", `/v1/units/${id}`); }

  autocomplete(q: string, options: { level?: AutocompleteLevel; plaka?: number; districtId?: number } = {}): Promise<Suggestion[]> {
    const params = new URLSearchParams({ q });
    if (options.level) params.set("level", options.level);
    if (options.plaka !== undefined) params.set("plaka", String(options.plaka));
    if (options.districtId !== undefined) params.set("districtId", String(options.districtId));
    return this.json("GET", `/v1/autocomplete?${params}`);
  }

  private async json<T>(method: string, path: string, body?: unknown): Promise<T> {
    const response = await this.send(method, path, body === undefined ? undefined : JSON.stringify(body), "application/json");
    return (await response.json()) as T;
  }

  private async text(method: string, path: string, body: string, contentType: string): Promise<string> {
    return (await this.send(method, path, body, contentType)).text();
  }

  private async send(method: string, path: string, body: string | undefined, contentType: string): Promise<Response> {
    const response = await this.fetchImpl(this.baseUrl + path, {
      method,
      body,
      headers: { ...(body === undefined ? {} : { "Content-Type": contentType }), Accept: "application/json, text/csv", ...this.headers },
      signal: AbortSignal.timeout(this.timeoutMs),
    });
    if (!response.ok) {
      let problem: { title?: string; detail?: string; errors?: Record<string, string[]> } = {};
      try {
        problem = await response.json();
      } catch {
        /* not JSON */
      }
      throw new AdresTRError(response.status, problem.title ?? (response.statusText || "Error"), problem.detail, problem.errors);
    }
    return response;
  }
}
