using Services.Models;
using Services.Ordering;
using Xunit;

namespace Services.Tests;

public sealed class CartRefreshServiceTests
{
    [Fact]
    public void RefreshConfiguration_RepricesHardwareAndKeepsServicePrice()
    {
        var component = new CartItemComponent(
            7, "GPU", "Old GPU", "Old", "OLD-7", "Old shop", 100m, "CHF");
        var item = new CartItem(
            Guid.NewGuid(),
            CartItemType.CustomConfiguration,
            "PC",
            null,
            [],
            300m,
            1,
            [component]);
        var currentProduct = Product(7, "Current GPU", 130m);

        var refreshed = CartRefreshService.RefreshConfiguration(
            item,
            new Dictionary<long, BuilderProduct> { [7] = currentProduct });

        Assert.NotNull(refreshed);
        Assert.Equal(330m, refreshed.UnitPrice);
        Assert.Equal(130m, Assert.Single(refreshed.Components!).UnitPrice);
        Assert.Equal("Current GPU", Assert.Single(refreshed.Components!).ProductName);
    }

    [Fact]
    public void RefreshConfiguration_RejectsUnavailableComponent()
    {
        var item = new CartItem(
            Guid.NewGuid(),
            CartItemType.CustomConfiguration,
            "PC",
            null,
            [],
            100m,
            1,
            [new CartItemComponent(9, "CPU", "CPU", null, null, "Shop", 100m, "CHF")]);

        var refreshed = CartRefreshService.RefreshConfiguration(
            item,
            new Dictionary<long, BuilderProduct>());

        Assert.Null(refreshed);
    }

    [Fact]
    public void RefreshConfiguration_UsesCurrentHardwareAndServicePrices()
    {
        var item = new CartItem(
            Guid.NewGuid(),
            CartItemType.CustomConfiguration,
            "PC",
            null,
            [],
            999m,
            1,
            [new CartItemComponent(7, "GPU", "Old GPU", null, null, "Shop", 100m, "CHF")],
            "pcwerk_build",
            "assembly",
            ServiceAddonCodes: ["extended-stress"]);
        var builderService = new Services.Configurator.PcBuilderService(null!);

        var refreshed = CartRefreshService.RefreshConfiguration(
            item,
            new Dictionary<long, BuilderProduct> { [7] = Product(7, "Current GPU", 130m) },
            builderService);

        Assert.NotNull(refreshed);
        Assert.Equal(328m, refreshed.UnitPrice);
    }

    private static BuilderProduct Product(long id, string name, decimal price) => new(
        id,
        $"tp-{id}",
        name,
        BuilderCategory.Gpu,
        "Brand",
        "/gpu.webp",
        new ProductOffer(price, "CHF", "Current shop", 0, 0, DateTimeOffset.UtcNow),
        [],
        new ProductCompatibility(),
        [],
        ManufacturerPartNumber: $"MPN-{id}",
        SourceUrl: $"https://example.test/{id}");
}
