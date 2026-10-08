using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using AdresTR;
using AdresTR.Api;
using AdresTR.Data;
using AdresTR.Text;
using Microsoft.AspNetCore.HttpOverrides;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Privacy by design (ADR-0009): request bodies are never logged; addresses are processed in memory only.
builder.Logging.AddFilter("Microsoft.AspNetCore.HttpLogging", LogLevel.None);

builder.Services.AddSingleton<Gazetteer>(_ => TurkishGazetteer.Default);
builder.Services.AddSingleton<AddressParser>(_ => TurkishGazetteer.Parser);

builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
{
    document.Info.Title = "AdresTR API";
    document.Info.Description = "Turkish free-text address parsing, normalization and validation. Nothing is stored or logged.";
    return Task.CompletedTask;
}));
builder.Services.AddProblemDetails();
builder.Services.AddValidation();
builder.Services.AddHealthChecks();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().WithMethods("GET", "POST")));
builder.Services.AddOutputCache(o => o.AddPolicy("reference", p => p.Expire(TimeSpan.FromHours(6))));
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = Endpoints.MaxCsvBytes + 64 * 1024);

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    // The demo runs behind a single reverse proxy (Azure Container Apps ingress / Render).
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.ForwardLimit = 1;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

int permitLimit = builder.Configuration.GetValue("RateLimiting:PermitLimit", 60);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("per-ip", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = permitLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.OnRejected = (context, _) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        return ValueTask.CompletedTask;
    };
});

var app = builder.Build();

// Load the gazetteer and parser index at startup, so the first request is fast and /health/ready means ready.
_ = app.Services.GetRequiredService<AddressParser>().Parse("Caferağa Mah. Kadıköy İstanbul");

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();
app.UseRateLimiter();
app.UseOutputCache();

app.MapOpenApi();
app.MapScalarApiReference(o => o.WithTitle("AdresTR API"));
app.MapGet("/", () => Results.Redirect("/scalar/v1")).ExcludeFromDescription();
app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");

app.MapGet("/privacy", () => Results.Text(
    """
    AdresTR API — privacy notice

    Addresses you send are parsed in memory and returned. They are not stored, logged or shared.
    Do not send personal data you are not allowed to process. For production use, self-host the
    open-source library or container (MIT): https://github.com/AlperCna/AdresTR
    """,
    "text/plain; charset=utf-8")).WithTags("Info").ExcludeFromDescription();

app.MapAdresTR();

app.MapPost("/v1/text/normalize", (NormalizeRequest request) =>
    {
        string normalized = TurkishText.Normalize(request.Text);
        return TypedResults.Ok(new NormalizeResponse(normalized, TurkishText.Fold(normalized), TurkishText.ToTitleTr(normalized)));
    })
    .RequireRateLimiting("per-ip")
    .WithName("NormalizeText").WithTags("Text")
    .WithSummary("Normalizes Turkish text and returns its ASCII-folded matching key and title-cased form.");

app.Run();

/// <summary>Text to normalize.</summary>
public sealed record NormalizeRequest(string Text);

/// <summary>Normalized text, its folded matching key and its Turkish title-cased form.</summary>
public sealed record NormalizeResponse(string Normalized, string Folded, string Title);

/// <summary>Entry point marker for <c>WebApplicationFactory</c>.</summary>
public partial class Program;
