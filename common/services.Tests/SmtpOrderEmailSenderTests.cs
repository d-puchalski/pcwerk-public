using Services.Ordering;
using Xunit;

namespace Services.Tests;

public sealed class SmtpOrderEmailSenderTests
{
    [Fact]
    public void BuildSubject_ForPresetIncludesComputerName()
    {
        var order = Order(CartItemType.PcPreset, "PCWerk Creator Pro");

        var subject = SmtpOrderEmailSender.BuildSubject(order);

        Assert.Contains(order.Number, subject);
        Assert.Contains("PCWerk Creator Pro", subject);
    }

    [Fact]
    public void BuildBody_RendersPresetNameAndLinkedComponentTable()
    {
        var order = Order(CartItemType.PcPreset, "PCWerk Creator Pro");

        var body = SmtpOrderEmailSender.BuildBody(order);

        Assert.Contains("Computer model", body);
        Assert.Contains("PCWerk Creator Pro", body);
        Assert.Contains("<th", body);
        Assert.Contains("Provider", body);
        Assert.Contains("Digitec", body);
        Assert.Contains("CHF 1,234.50", body);
        Assert.Contains("https://shop.example/product?id=42&amp;ref=pcwerk", body);
        Assert.Contains(">OPEN</a>", body);
    }

    [Fact]
    public void BuildBody_ForCustomBuildUsesCustomConfigurationLabel()
    {
        var order = Order(CartItemType.CustomConfiguration, "Custom PC configuration");

        var body = SmtpOrderEmailSender.BuildBody(order);

        Assert.Contains("Custom configuration", body);
        Assert.Contains("Individually configured PC", body);
        Assert.DoesNotContain("Computer model", body);
    }

    [Fact]
    public void BuildBody_DoesNotRenderUnsafeProductLink()
    {
        var order = Order(CartItemType.PcPreset, "PCWerk Creator Pro", "javascript:alert(1)");

        var body = SmtpOrderEmailSender.BuildBody(order);

        Assert.DoesNotContain("javascript:", body);
    }

    private static PlacedOrder Order(CartItemType type, string itemName, string sourceUrl = "https://shop.example/product?id=42&ref=pcwerk")
    {
        var component = new CartItemComponent(
            42,
            "GPU",
            "Example Graphics Card",
            "Example",
            "GPU-42",
            "Digitec",
            1234.50m,
            "CHF",
            SourceUrl: sourceUrl);
        var item = new CartItem(
            Guid.NewGuid(),
            type,
            itemName,
            null,
            [new("Service package", "complete")],
            1234.50m,
            1,
            [component]);
        var customer = new CustomerDetails
        {
            FullName = "Jan Kowalski",
            Email = "jan@example.com",
            Phone = "+41 44 123 45 67",
            Street = "Teststrasse 1",
            PostalCode = "8000",
            City = "Zürich",
            AcceptTerms = true
        };
        return new("PW-TEST-1", DateTimeOffset.Parse("2026-08-27T12:00:00Z"), "PL", customer, [item], item.Total);
    }
}
