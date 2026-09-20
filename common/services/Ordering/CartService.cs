using EFDB;
using Services.Configurator;
using Services.Models;

namespace Services.Ordering;

public sealed class CartService(PcBuilderService builderService)
{
    private readonly List<CartItem> _items = [];

    public event Action? Changed;

    public IReadOnlyList<CartItem> Items => _items;
    public int ItemCount => _items.Sum(item => item.Quantity);
    public decimal Total => _items.Sum(item => item.Total);

    public void AddLaptop(CatalogLaptop laptop)
    {
        ArgumentNullException.ThrowIfNull(laptop);
        ArgumentException.ThrowIfNullOrWhiteSpace(laptop.Name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(laptop.Price);

        var existing = _items.FirstOrDefault(item =>
            item.Type == CartItemType.Laptop
            && item.Components?.FirstOrDefault()?.ProductId == laptop.ProductId);
        if (existing is not null)
        {
            SetQuantity(existing.Id, existing.Quantity + 1);
            return;
        }

        var details = new List<CartItemDetail>();
        if (!string.IsNullOrWhiteSpace(laptop.Manufacturer))
        {
            details.Add(new("Manufacturer", laptop.Manufacturer, "LaptopCatalog.Manufacturer"));
        }
        if (!string.IsNullOrWhiteSpace(laptop.ManufacturerPartNumber))
        {
            details.Add(new("Manufacturer part number", laptop.ManufacturerPartNumber, "LaptopCatalog.ManufacturerPartNumber"));
        }

        _items.Add(new(
            Guid.NewGuid(),
            CartItemType.Laptop,
            laptop.Name,
            laptop.ImageUrl,
            details,
            laptop.Price,
            1,
            [new CartItemComponent(
                laptop.ProductId,
                CatalogCodes.Laptop,
                laptop.Name,
                laptop.Manufacturer,
                laptop.ManufacturerPartNumber,
                "PCWerk",
                laptop.Price,
                laptop.Currency,
                SourceUrl: laptop.SourceUrl)]));
        Changed?.Invoke();
    }

    public void AddPcPreset(CatalogPcPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentException.ThrowIfNullOrWhiteSpace(preset.Name);
        ArgumentOutOfRangeException.ThrowIfNegative(preset.Price);

        var existing = _items.FirstOrDefault(item =>
            item.Type == CartItemType.PcPreset
            && item.PcPresetId == preset.PresetId);
        if (existing is not null)
        {
            SetQuantity(existing.Id, existing.Quantity + 1);
            return;
        }

        _items.Add(new(
            Guid.NewGuid(),
            CartItemType.PcPreset,
            preset.Name,
            preset.ImageUrl,
            preset.Specifications
                .Select(x => new CartItemDetail(x.Label, x.Value, SpecificationTranslationKey(x.Label)))
                .Append(new("Components", $"{preset.Components.Sum(x => x.Quantity)}", "PcCard.Components"))
                .ToArray(),
            preset.Price,
            1,
            preset.Components.Select(component => new CartItemComponent(
                component.ProductId,
                component.CategoryCode,
                component.ProductName,
                component.Manufacturer,
                component.ManufacturerPartNumber,
                component.Retailer,
                component.UnitPrice,
                component.Currency,
                component.Quantity,
                component.OfferUrl)).ToArray(),
            PcPresetId: preset.PresetId));
        Changed?.Invoke();
    }

    public void AddConfiguration(BuilderBuild build)
    {
        ArgumentNullException.ThrowIfNull(build);

        var status = builderService.Evaluate(build);
        var serviceDecisionComplete = build.OrderType == BuildOrderType.ComponentsOnly
            || (build.OrderType == BuildOrderType.PcWerkBuild && build.ServicePackage is not null);
        if (status.SelectedCount != status.RequiredCount || !status.IsCompatible || !serviceDecisionComplete)
        {
            throw new InvalidOperationException("The PC configuration is incomplete or incompatible.");
        }

        var details = Enum.GetValues<BuilderCategory>()
            .Where(category => builderService.GetSelected(build, category) is not null)
            .Select(category => new CartItemDetail(
                CategoryLabel(category),
                builderService.GetSelected(build, category)!.Name,
                CategoryTranslationKey(category)))
            .ToList();
        if (build.GraphicsMode == BuildGraphicsMode.Integrated)
        {
            details.Add(new(
                "GPU",
                "Integrated graphics",
                "ConfiguratorPreview.GraphicsCard",
                "ConfiguratorPreview.IntegratedGraphics"));
        }
        details.Add(new(
            "Order type",
            build.OrderType == BuildOrderType.ComponentsOnly
                ? "Compatibility check without assembly"
                : "PCWerk build",
            "Cart.OrderType",
            build.OrderType == BuildOrderType.ComponentsOnly
                ? "ConfiguratorPreview.NoAssemblySelected"
                : "ConfiguratorPreview.PcWerkBuild"));
        if (build.OrderType == BuildOrderType.ComponentsOnly)
        {
            details.Add(new("Component ordering", "Directly from partner stores", "Cart.ComponentOrdering", "Cart.DirectlyFromPartnerStores"));
            details.Add(new("Delivery and warranty", "Handled by the respective store", "Cart.DeliveryAndWarranty", "Cart.HandledByRespectiveStore"));
        }
        if (build.ServicePackage is not null)
        {
            details.Add(new("Service package", build.ServicePackage.Id, "Cart.ServicePackage", build.ServicePackage.NameTranslationKey));
        }
        foreach (var addon in build.ServiceAddons)
        {
            details.Add(new("Optional service", addon.Id, "Cart.OptionalService", addon.NameTranslationKey));
        }

        var components = Enum.GetValues<BuilderCategory>()
            .Select(category => builderService.GetSelected(build, category))
            .Where(product => product is not null)
            .Cast<BuilderProduct>()
            .Select(product => new CartItemComponent(
                product.DatabaseId,
                CategoryCode(product.Category),
                product.Name,
                product.Brand,
                product.ManufacturerPartNumber,
                product.Offer.Retailer,
                product.Price,
                product.Offer.Currency,
                SourceUrl: product.SourceUrl))
            .ToArray();

        _items.Add(new(
            Guid.NewGuid(),
            CartItemType.CustomConfiguration,
            "Custom PC configuration",
            build.Case?.Image,
            details,
            builderService.CalculatePricing(build).Total,
            1,
            components,
            build.OrderType == BuildOrderType.ComponentsOnly ? "components_only" : "pcwerk_build",
            build.ServicePackage?.Id,
            NameTranslationKey: "Cart.CustomPcConfiguration",
            ServiceAddonCodes: build.ServiceAddons.Select(x => x.Id).ToArray()));
        Changed?.Invoke();
    }

    public void SetQuantity(Guid itemId, int quantity)
    {
        var index = _items.FindIndex(item => item.Id == itemId);
        if (index < 0)
        {
            return;
        }
        if (quantity <= 0)
        {
            Remove(itemId);
            return;
        }

        _items[index] = _items[index] with { Quantity = Math.Min(quantity, 10) };
        Changed?.Invoke();
    }

    public void Remove(Guid itemId)
    {
        if (_items.RemoveAll(item => item.Id == itemId) > 0)
        {
            Changed?.Invoke();
        }
    }

    public void Clear()
    {
        if (_items.Count == 0)
        {
            return;
        }

        _items.Clear();
        Changed?.Invoke();
    }

    public void ReplaceWith(IReadOnlyList<CartItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items.Clear();
        _items.AddRange(items);
        Changed?.Invoke();
    }

    private static string CategoryLabel(BuilderCategory category) => category switch
    {
        BuilderCategory.Cpu => "CPU",
        BuilderCategory.Gpu => "GPU",
        BuilderCategory.Motherboard => "Motherboard",
        BuilderCategory.Ram => "RAM",
        BuilderCategory.Storage => "Storage",
        BuilderCategory.Cooling => "CPU cooling",
        BuilderCategory.Psu => "Power supply",
        BuilderCategory.Case => "Case",
        BuilderCategory.Monitor => "Monitor",
        BuilderCategory.Mouse => "Mouse",
        BuilderCategory.Keyboard => "Keyboard",
        _ => category.ToString()
    };

    private static string CategoryTranslationKey(BuilderCategory category) => category switch
    {
        BuilderCategory.Cpu => "ConfiguratorPreview.Processor",
        BuilderCategory.Gpu => "ConfiguratorPreview.GraphicsCard",
        BuilderCategory.Motherboard => "ConfiguratorPreview.Motherboard",
        BuilderCategory.Ram => "ConfiguratorPreview.Memory",
        BuilderCategory.Storage => "ConfiguratorPreview.Storage",
        BuilderCategory.Cooling => "ConfiguratorPreview.CpuCooling",
        BuilderCategory.Psu => "ConfiguratorPreview.PowerSupply",
        BuilderCategory.Case => "ConfiguratorPreview.Case",
        BuilderCategory.Monitor => "ConfiguratorPreview.Monitor",
        BuilderCategory.Mouse => "ConfiguratorPreview.Mouse",
        BuilderCategory.Keyboard => "ConfiguratorPreview.Keyboard",
        _ => category.ToString()
    };

    private static string? SpecificationTranslationKey(string label) => label switch
    {
        "Storage" => "ConfiguratorPreview.Storage",
        _ => null,
    };

    private static string CategoryCode(BuilderCategory category) => category switch
    {
        BuilderCategory.Cpu => "CPU",
        BuilderCategory.Gpu => "GPU",
        BuilderCategory.Motherboard => "MOTHERBOARD",
        BuilderCategory.Ram => "RAM",
        BuilderCategory.Storage => "STORAGE",
        BuilderCategory.Cooling => "COOLING",
        BuilderCategory.Psu => "PSU",
        BuilderCategory.Case => "CASE",
        BuilderCategory.Monitor => "MONITOR",
        BuilderCategory.Mouse => "MOUSE",
        BuilderCategory.Keyboard => "KEYBOARD",
        _ => throw new ArgumentOutOfRangeException(nameof(category))
    };
}
