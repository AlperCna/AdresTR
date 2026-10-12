using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace AdresTR.Integrations.Tests;

public class IntegrationTests
{
    public sealed class Order
    {
        [TurkishAddress]
        public string? Address { get; set; }
    }

    private sealed class StrictOrder
    {
        [Required]
        [TurkishAddress(RequireDoorNumber = true, MinimumConfidence = 0.8)]
        public string? Address { get; set; }
    }

    private sealed class OrderValidator : AbstractValidator<Order>
    {
        public OrderValidator() => RuleFor(x => x.Address).MustBeTurkishAddress();
    }

    private static List<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void Resolvable_address_is_valid() =>
        Assert.Empty(Validate(new Order { Address = "Caferağa Mah. Moda Cad. No:12 Kadıköy/İstanbul" }));

    [Fact]
    public void Ambiguous_address_is_rejected_with_a_helpful_message()
    {
        ValidationResult error = Assert.Single(Validate(new Order { Address = "Cumhuriyet Mah. Atatürk Cad. No:5" }));
        Assert.Contains("ambiguous", error.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains(nameof(Order.Address), error.MemberNames);
    }

    [Fact]
    public void Gibberish_is_rejected() =>
        Assert.Single(Validate(new Order { Address = "asdf qwer zxcv" }));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_values_are_valid_unless_required(string? value)
    {
        Assert.Empty(Validate(new Order { Address = value }));
        Assert.NotEmpty(Validate(new StrictOrder { Address = value }));
    }

    [Fact]
    public void Options_are_honoured()
    {
        Assert.Contains(Validate(new StrictOrder { Address = "Caferağa Mah. Moda Cad. Kadıköy" }), e => e.ErrorMessage!.Contains("door", StringComparison.Ordinal));
        Assert.Empty(Validate(new StrictOrder { Address = "Caferağa Mah. Moda Cad. No:12 Kadıköy İstanbul" }));
    }

    [Fact]
    public void Fluent_rule_validates()
    {
        var validator = new OrderValidator();
        Assert.True(validator.Validate(new Order { Address = "Caferağa Mah. Moda Cad. No:12 Kadıköy/İstanbul" }).IsValid);
        Assert.True(validator.Validate(new Order { Address = null }).IsValid);

        FluentValidation.Results.ValidationResult bad = validator.Validate(new Order { Address = "Cumhuriyet Mah. No:5" });
        Assert.False(bad.IsValid);
        Assert.Contains("ambiguous", bad.Errors[0].ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Fluent_rule_accepts_options()
    {
        var validator = new InlineValidator<Order>();
        validator.RuleFor(x => x.Address).MustBeTurkishAddress(o => o.RequireUnit = false);
        Assert.True(validator.Validate(new Order { Address = "Cumhuriyet Mah. No:5" }).IsValid);
    }

    [Fact]
    public void AddAdresTR_registers_services()
    {
        using ServiceProvider sp = new ServiceCollection().AddAdresTR().BuildServiceProvider();
        Assert.Same(sp.GetRequiredService<AddressParser>(), sp.GetRequiredService<AddressParser>());
        Assert.Equal(MatchLevel.SameUnit, sp.GetRequiredService<AddressMatcher>().Compare("Moda Kadıköy İstanbul", "Caferağa Mah. Kadıköy").Level);
    }

    [Fact]
    public async Task Minimal_api_validation_rejects_bad_addresses()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAdresTR().AddValidation();
        await using WebApplication app = builder.Build();
        app.MapPost("/orders", (Order order) => Results.Ok(order.Address));
        await app.StartAsync(TestContext.Current.CancellationToken);

        HttpClient client = app.GetTestClient();
        HttpResponseMessage ok = await client.PostAsJsonAsync("/orders", new { address = "Caferağa Mah. Moda Cad. No:12 Kadıköy İstanbul" }, TestContext.Current.CancellationToken);
        HttpResponseMessage bad = await client.PostAsJsonAsync("/orders", new { address = "Cumhuriyet Mah. No:5" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }
}
