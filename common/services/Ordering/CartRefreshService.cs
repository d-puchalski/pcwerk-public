using EFDB;
using Services.Catalog;
using Services.Configurator;
using Services.Models;

namespace Services.Ordering;

public enum CartRefreshFailure
{
    None,
    PriceChanged,
    ProductUnavailable
}

public sealed record CartRefreshResult(
    IReadOnlyList<CartItem> Items,
    CartRefreshFailure Failure,
    string? ItemName = null);

public sealed class CartRefreshService(
    EfProductCatalog productCatalog,
    PcPresetCatalogService presetCatalog,
    LaptopCatalogService laptopCatalog,
    PcBuilderService builderService)
{
    public async Task<CartRefreshResult> RefreshAsync(
        IReadOnlyList<CartItem> items,
        CancellationToken cancellationToken = default)
    {
        var customProductIds = items
            .Where(x => x.Type == CartItemType.CustomConfiguration)
            .SelectMany(x => x.Components ?? [])
            .Select(x => x.ProductId)
            .Distinct()
            .ToArray();
        var currentProducts = await productCatalog.GetCurrentProductsByIdsAsync(
            customProductIds,
            cancellationToken);
        var productsById = currentProducts.ToDictionary(x => x.DatabaseId);
        var refreshedItems = new List<CartItem>(items.Count);
        var priceChanged = false;

        foreach (var item in items)
        {
            CartItem? refreshed;
            if (item.Type == CartItemType.PcPreset)
            {
                refreshed = await RefreshPresetAsync(item, cancellationToken);
            }
            else if (item.Type == CartItemType.Laptop)
            {
                refreshed = await RefreshLaptopAsync(item, cancellationToken);
            }
            else
            {
                refreshed = RefreshConfiguration(item, productsById, builderService);
            }

            if (refreshed is null)
            {
                return new(items, CartRefreshFailure.ProductUnavailable, item.Name);
            }

            priceChanged |= refreshed.UnitPrice != item.UnitPrice;
            refreshedItems.Add(refreshed);
        }

        return new(
            refreshedItems,
            priceChanged ? CartRefreshFailure.PriceChanged : CartRefreshFailure.None);
    }

    private async Task<CartItem?> RefreshLaptopAsync(
        CartItem item,
        CancellationToken cancellationToken)
    {
        var productId = item.Components?.FirstOrDefault()?.ProductId;
        if (productId is null)
        {
            return null;
        }

        var laptop = await laptopCatalog.GetAvailableLaptopAsync(productId.Value, cancellationToken);
        if (laptop is null)
        {
            return null;
        }

        return item with
        {
            Name = laptop.Name,
            ImagePath = laptop.ImageUrl,
            UnitPrice = laptop.Price,
            Details = LaptopDetails(laptop),
            Components = [new CartItemComponent(
                laptop.ProductId,
                CatalogCodes.Laptop,
                laptop.Name,
                laptop.Manufacturer,
                laptop.ManufacturerPartNumber,
                "PCWerk",
                laptop.Price,
                laptop.Currency,
                SourceUrl: laptop.SourceUrl)]
        };
    }

    private static IReadOnlyList<CartItemDetail> LaptopDetails(CatalogLaptop laptop)
    {
        var details = new List<CartItemDetail>();
        if (!string.IsNullOrWhiteSpace(laptop.Manufacturer))
        {
            details.Add(new("Manufacturer", laptop.Manufacturer, "LaptopCatalog.Manufacturer"));
        }
        if (!string.IsNullOrWhiteSpace(laptop.ManufacturerPartNumber))
        {
            details.Add(new("Manufacturer part number", laptop.ManufacturerPartNumber, "LaptopCatalog.ManufacturerPartNumber"));
        }
        return details;
    }

    private async Task<CartItem?> RefreshPresetAsync(
        CartItem item,
        CancellationToken cancellationToken)
    {
        if (item.PcPresetId is not { } presetId)
        {
            return null;
        }

        var preset = await presetCatalog.GetAvailablePresetAsync(presetId, cancellationToken);
        if (preset is null)
        {
            return null;
        }

        return item with
        {
            Name = preset.Name,
            ImagePath = preset.ImageUrl,
            UnitPrice = preset.Price,
            Components = preset.Components.Select(component => new CartItemComponent(
                component.ProductId,
                component.CategoryCode,
                component.ProductName,
                component.Manufacturer,
                component.ManufacturerPartNumber,
                component.Retailer,
                component.UnitPrice,
                component.Currency,
                component.Quantity,
                component.OfferUrl)).ToArray()
        };
    }

    internal static CartItem? RefreshConfiguration(
        CartItem item,
        IReadOnlyDictionary<long, BuilderProduct> productsById)
    {
        var components = item.Components?.ToArray() ?? [];
        if (components.Length == 0 || components.Any(x => !productsById.ContainsKey(x.ProductId)))
        {
            return null;
        }

        var refreshedComponents = components
            .Select(component => RefreshComponent(component, productsById[component.ProductId]))
            .ToArray();
        var previousHardwarePrice = components.Sum(x => x.UnitPrice * x.Quantity);
        var currentHardwarePrice = refreshedComponents.Sum(x => x.UnitPrice * x.Quantity);
        var currentUnitPrice = Math.Max(0, item.UnitPrice - previousHardwarePrice + currentHardwarePrice);

        return item with
        {
            UnitPrice = currentUnitPrice,
            Components = refreshedComponents
        };
    }

    internal static CartItem? RefreshConfiguration(
        CartItem item,
        IReadOnlyDictionary<long, BuilderProduct> productsById,
        PcBuilderService builderService)
    {
        var refreshed = RefreshConfiguration(item, productsById);
        var servicePrice = CurrentServicePrice(item, builderService);
        if (refreshed is null || servicePrice is null)
        {
            return null;
        }

        return refreshed with
        {
            UnitPrice = refreshed.Components!.Sum(x => x.UnitPrice * x.Quantity) + servicePrice.Value
        };
    }

    private static decimal? CurrentServicePrice(CartItem item, PcBuilderService builderService)
    {
        if (item.ConfigurationOrderType == "components_only")
        {
            return builderService.CompatibilityCheckPrice;
        }

        if (item.ConfigurationOrderType != "pcwerk_build" || item.ServicePackageCode is null)
        {
            return null;
        }

        var servicePackage = builderService.ServicePackages.FirstOrDefault(x =>
            x.Id.Equals(item.ServicePackageCode, StringComparison.OrdinalIgnoreCase));
        if (servicePackage is null)
        {
            return null;
        }

        var build = new BuilderBuild
        {
            OrderType = BuildOrderType.PcWerkBuild,
            ServicePackage = servicePackage
        };
        foreach (var addonCode in item.ServiceAddonCodes ?? [])
        {
            var addon = builderService.OptionalServices.FirstOrDefault(x =>
                x.Id.Equals(addonCode, StringComparison.OrdinalIgnoreCase));
            if (addon is null)
            {
                return null;
            }

            build.ServiceAddons.Add(addon);
        }

        var pricing = builderService.CalculatePricing(build);
        return pricing.ServicePackagePrice + pricing.AddonsTotal;
    }

    private static CartItemComponent RefreshComponent(
        CartItemComponent component,
        BuilderProduct product) =>
        component with
        {
            ProductName = product.Name,
            Manufacturer = product.Brand,
            ManufacturerPartNumber = product.ManufacturerPartNumber,
            RetailerName = product.Offer.Retailer,
            UnitPrice = product.Price,
            Currency = product.Offer.Currency,
            SourceUrl = product.SourceUrl
        };
}
