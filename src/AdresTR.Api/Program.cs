using AdresTR.Text;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseExceptionHandler();
app.MapOpenApi();
app.MapScalarApiReference(options => options.WithTitle("AdresTR API"));
app.MapHealthChecks("/health/live");

var v1 = app.MapGroup("/v1").WithTags("v1");

v1.MapPost("/text/normalize", (NormalizeRequest request) =>
    {
        string normalized = TurkishText.Normalize(request.Text);
        return TypedResults.Ok(new NormalizeResponse(normalized, TurkishText.Fold(normalized), TurkishText.ToTitleTr(normalized)));
    })
    .WithName("NormalizeText")
    .WithSummary("Normalizes Turkish text and returns its ASCII-folded matching key and title-cased form.");

app.Run();

/// <summary>Text to normalize.</summary>
public sealed record NormalizeRequest(string Text);

/// <summary>Normalized text, its folded matching key and its Turkish title-cased form.</summary>
public sealed record NormalizeResponse(string Normalized, string Folded, string Title);

/// <summary>Entry point marker for <c>WebApplicationFactory</c>.</summary>
public partial class Program;
