using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AdresTR.Api.Tests;

public class ApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.WithWebHostBuilder(b => b.UseSetting("RateLimiting:PermitLimit", "10000")).CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<JsonElement> PostJson(string url, object body)
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync(url, body, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    [Fact]
    public async Task Health_endpoints_are_healthy()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/health/live", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/health/ready", Ct)).StatusCode);
    }

    [Fact]
    public async Task Parse_returns_components_ids_and_corrections()
    {
        JsonElement r = await PostJson("/v1/parse", new { text = "kadikoy caferaga mh moda cd no:12 d3 istanbul" });

        Assert.Equal("Caferağa Mah. Moda Cad. No:12 D:3 34710 Kadıköy/İstanbul", r.GetProperty("canonical").GetString());
        Assert.Equal(34230005, r.GetProperty("resolved").GetProperty("birim").GetProperty("id").GetInt32());
        Assert.Equal("Mahalle", r.GetProperty("resolved").GetProperty("birim").GetProperty("kind").GetString());
        Assert.Equal("Cadde", r.GetProperty("components").GetProperty("streetType").GetString());
        Assert.Equal("12", r.GetProperty("components").GetProperty("doorNumber").GetProperty("value").GetString());
        Assert.Contains(r.GetProperty("corrections").EnumerateArray(), c => c.GetProperty("kind").GetString() == "Diacritics");
        Assert.Equal("2026.10", r.GetProperty("dataVersion").GetString());
    }

    [Fact]
    public async Task Parse_of_an_ambiguous_address_returns_candidates_but_no_unit()
    {
        JsonElement r = await PostJson("/v1/parse", new { text = "Cumhuriyet Mah. Atatürk Cad. No:5" });
        Assert.Equal(JsonValueKind.Null, r.GetProperty("resolved").GetProperty("birim").ValueKind);
        Assert.Equal(5, r.GetProperty("candidates").GetArrayLength());
    }

    [Fact]
    public async Task Parse_rejects_missing_and_too_long_text()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/v1/parse", new { }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/v1/parse", new { text = new string('a', 600) }, Ct)).StatusCode);
    }

    [Fact]
    public async Task Batch_parses_in_order_and_enforces_limits()
    {
        JsonElement r = await PostJson("/v1/parse/batch", new { items = new[] { "Moda Kadıköy", "Alsancak Mah. Konak İzmir" } });
        JsonElement[] results = [.. r.GetProperty("results").EnumerateArray()];
        Assert.Equal(34230005, results[0].GetProperty("resolved").GetProperty("birim").GetProperty("id").GetInt32());
        Assert.Equal(35210010, results[1].GetProperty("resolved").GetProperty("birim").GetProperty("id").GetInt32());

        HttpResponseMessage tooMany = await _client.PostAsJsonAsync("/v1/parse/batch", new { items = Enumerable.Repeat("Moda", 1001).ToArray() }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);

        HttpResponseMessage tooLong = await _client.PostAsJsonAsync("/v1/parse/batch", new { items = new[] { new string('a', 600) } }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    [Fact]
    public async Task Csv_cleaning_appends_columns()
    {
        const string csv = "id,adres\n1,kadikoy caferaga mh moda cd no:12\n2,\"ALSANCAK MAH. 1453 SK. NO:5, KONAK/İZMİR\"\n";
        HttpResponseMessage response = await _client.PostAsync("/v1/parse/csv", new StringContent(csv, Encoding.UTF8, "text/csv"), Ct);
        string body = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string[] lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("id,adres,il,ilce,mahalle,posta_kodu,birim_id,adres_kanonik,guven", lines[0]);
        Assert.Contains("İstanbul,Kadıköy,Caferağa,34710,34230005", lines[1], StringComparison.Ordinal);
        Assert.Contains("İzmir,Konak,Alsancak", lines[2], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Csv_cleaning_reports_a_missing_column()
    {
        HttpResponseMessage response = await _client.PostAsync("/v1/parse/csv?column=address", new StringContent("id,adres\n1,x\n", Encoding.UTF8, "text/csv"), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("İstanbul", "Kadıköy", "Caferağa Mah.", "34710", true)]
    [InlineData("İstanbul", "Kadıköy", "Caferağa", "06100", false)]
    [InlineData("İzmir", "Kadıköy", null, null, false)]
    [InlineData(null, null, "Cumhuriyet", null, false)]
    [InlineData("Urfa", "Haliliye", null, null, true)]
    public async Task Validate_checks_consistency(string? il, string? ilce, string? mahalle, string? postaKodu, bool valid)
    {
        JsonElement r = await PostJson("/v1/validate", new { il, ilce, mahalle, postaKodu });
        Assert.Equal(valid, r.GetProperty("valid").GetBoolean());
        Assert.Equal(valid, r.GetProperty("problems").GetArrayLength() == 0);
    }

    [Fact]
    public async Task Reference_endpoints_list_the_hierarchy()
    {
        JsonElement provinces = await _client.GetFromJsonAsync<JsonElement>("/v1/provinces", Ct);
        Assert.Equal(81, provinces.GetArrayLength());

        JsonElement districts = await _client.GetFromJsonAsync<JsonElement>("/v1/provinces/34/districts", Ct);
        Assert.Equal(39, districts.GetArrayLength());

        JsonElement units = await _client.GetFromJsonAsync<JsonElement>("/v1/districts/3423/units", Ct);
        Assert.Contains(units.EnumerateArray(), u => u.GetProperty("name").GetString() == "Caferağa");

        JsonElement unit = await _client.GetFromJsonAsync<JsonElement>("/v1/units/34230005", Ct);
        Assert.Equal("34710", unit.GetProperty("postalCode").GetString());

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/v1/provinces/99/districts", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/v1/units/1", Ct)).StatusCode);
    }

    [Fact]
    public async Task Autocomplete_matches_prefixes_ignoring_diacritics()
    {
        JsonElement districts = await _client.GetFromJsonAsync<JsonElement>("/v1/autocomplete?q=kadi&level=district&plaka=34", Ct);
        Assert.Contains(districts.EnumerateArray(), d => d.GetProperty("name").GetString() == "Kadıköy");

        JsonElement units = await _client.GetFromJsonAsync<JsonElement>("/v1/autocomplete?q=cafer&districtId=3423", Ct);
        Assert.Equal("Caferağa", Assert.Single(units.EnumerateArray()).GetProperty("name").GetString());

        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/v1/autocomplete?q=k", Ct)).StatusCode);
    }

    [Fact]
    public async Task Normalize_returns_folded_and_title_forms()
    {
        JsonElement r = await PostJson("/v1/text/normalize", new { text = "  KADIKÖY’DE  caferağa " });
        Assert.Equal("kadikoy'de caferaga", r.GetProperty("folded").GetString());
        Assert.Equal("Kadıköy'de Caferağa", r.GetProperty("title").GetString());
    }

    [Fact]
    public async Task OpenApi_and_privacy_are_served()
    {
        string openApi = await _client.GetStringAsync("/openapi/v1.json", Ct);
        Assert.Contains("/v1/parse", openApi, StringComparison.Ordinal);
        Assert.Contains("not stored", await _client.GetStringAsync("/privacy", Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rate_limit_returns_429_with_retry_after()
    {
        HttpClient limited = factory.WithWebHostBuilder(b => b.UseSetting("RateLimiting:PermitLimit", "2")).CreateClient();
        HttpResponseMessage last = null!;
        for (int i = 0; i < 3; i++)
        {
            last = await limited.PostAsJsonAsync("/v1/parse", new { text = "Moda" }, Ct);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last.StatusCode);
        Assert.True(last.Headers.Contains("Retry-After"));
    }
}
