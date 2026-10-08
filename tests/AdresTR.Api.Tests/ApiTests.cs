using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AdresTR.Api.Tests;

public class ApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Health_endpoint_is_healthy()
    {
        HttpResponseMessage response = await _client.GetAsync("/health/live", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Normalize_returns_folded_and_title_forms()
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/v1/text/normalize", new { text = "  KADIKÖY’DE  caferağa " }, TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<NormalizeResponse>(TestContext.Current.CancellationToken);
        Assert.Equal(new NormalizeResponse("KADIKÖY'DE caferağa", "kadikoy'de caferaga", "Kadıköy'de Caferağa"), body);
    }

    [Fact]
    public async Task OpenApi_document_is_served()
    {
        HttpResponseMessage response = await _client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
