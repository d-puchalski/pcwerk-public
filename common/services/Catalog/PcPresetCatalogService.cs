using EFDB;
using Microsoft.EntityFrameworkCore;
using Services.Configurator;
using Services.Models;

namespace Services.Catalog;

public sealed class PcPresetCatalogService(IDbContextFactory<AppDbContext> contextFactory)
{
    public async Task<CatalogPcPreset?> GetAvailablePresetAsync(
        long presetId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var requiredCategoryCodes = await db.CatalogCategories
            .AsNoTracking()
            .Where(x => x.IsEnabled && x.IsRequired)
            .Select(x => x.Code)
            .ToArrayAsync(cancellationToken);
        var preset = await db.PcPresets
            .AsNoTracking()
            .Where(x => x.Id == presetId && x.IsEnabled)
            .Include(x => x.Products)
                .ThenInclude(x => x.Product)
                .ThenInclude(x => x.Category)
            .Include(x => x.Products)
                .ThenInclude(x => x.Product)
                .ThenInclude(x => x.Offers)
                .ThenInclude(x => x.Retailer)
            .Include(x => x.Products)
                .ThenInclude(x => x.Product)
                .ThenInclude(x => x.CpuSpecification)
            .Include(x => x.Products)
                .ThenInclude(x => x.Product)
                .ThenInclude(x => x.MotherboardSpecification)
                .ThenInclude(x => x!.VideoOutputs)
            .AsSplitQuery()
            .SingleOrDefaultAsync(cancellationToken);

        return preset is not null && IsCompleteAndAvailable(preset, requiredCategoryCodes)
            ? Map(preset)
            : null;
    }

    public async Task<IReadOnlyList<CatalogPcPreset>> GetAvailablePresetsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var requiredCategoryCodes = await db.CatalogCategories
            .AsNoTracking()
            .Where(x => x.IsEnabled && x.IsRequired)
            .Select(x => x.Code)
            .ToArrayAsync(cancellationToken);
        var presets = await db.PcPresets
            .AsNoTracking()
            .Where(x => x.IsEnabled)
            .Include(x => x.Products)
                .ThenInclude(x => x.Product)
                .ThenInclude(x => x.Category)
            .Include(x => x.Products)
                .ThenInclude(x => x.Product)
                .ThenInclude(x => x.Offers)
                .ThenInclude(x => x.Retailer)
            .Include(x => x.Products)
                .ThenInclude(x => x.Product)
                .ThenInclude(x => x.CpuSpecification)
            .Include(x => x.Products)
                .ThenInclude(x => x.Product)
                .ThenInclude(x => x.MotherboardSpecification)
                .ThenInclude(x => x!.VideoOutputs)
            .AsSplitQuery()
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Name)
            .ToArrayAsync(cancellationToken);

        return presets
            .Where(x => IsCompleteAndAvailable(x, requiredCategoryCodes))
            .Select(Map)
            .ToArray();
    }

    public async Task<IReadOnlyList<CatalogPcPreset>> GetShowcasePresetsAsync(
        int maximum = 3,
        CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(maximum, 1, 10);
        var presets = await GetAvailablePresetsAsync(cancellationToken);
        return presets
            .OrderBy(_ => Random.Shared.Next())
            .Take(take)
            .ToArray();
    }

    private static bool IsCompleteAndAvailable(
        PcPreset preset,
        IReadOnlyList<string> requiredCategoryCodes)
    {
        if (preset.Products.Count == 0 || !preset.Products.All(IsAvailable))
        {
            return false;
        }

        var categoryCodes = preset.Products
            .Select(x => x.Product.Category.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!requiredCategoryCodes.All(categoryCodes.Contains)) return false;

        var products = preset.Products.Select(x => x.Product).ToArray();
        if (products.Any(x => x.Category.Code == CatalogCodes.Gpu)) return true;

        return products.SingleOrDefault(x => x.Category.Code == CatalogCodes.Cpu)?.CpuSpecification?.HasIntegratedGraphics == true
            && products.SingleOrDefault(x => x.Category.Code == CatalogCodes.Motherboard)?.MotherboardSpecification?.VideoOutputs.Count > 0;
    }

    private static bool IsAvailable(PcPresetProduct presetProduct)
    {
        var product = presetProduct.Product;
        return presetProduct.Quantity > 0
            && product.IsVisible
            && product.Category.IsEnabled
            && product.LifecycleStatus == "active"
            && product.SpecificationStatus == "valid"
            && (product.Category.Code != CatalogCodes.Cpu || EfProductCatalog.IsBoxedCpu(product.Name))
            && product.Offers.Any(IsAvailableOffer);
    }

    private static bool IsAvailableOffer(Offer offer) =>
        offer.IsCurrent
        && offer.Currency == "CHF"
        && offer.Availability != "unavailable"
        && offer.TotalPrice > 0
        && offer.Retailer.IsEnabled;

    private static CatalogPcPreset Map(PcPreset preset)
    {
        var components = preset.Products
            .OrderBy(x => x.DisplayOrder)
            .Select(MapComponent)
            .ToArray();
        var specifications = components
            .Where(x => x.CategoryCode is CatalogCodes.Cpu or CatalogCodes.Gpu or CatalogCodes.Ram or CatalogCodes.Storage)
            .GroupBy(x => x.CategoryCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => new CatalogPresetSpecification(
                CategoryLabel(group.Key),
                string.Join(" + ", group.Select(ComponentSpecification))))
            .OrderBy(x => SpecificationOrder(x.Label))
            .ToList();
        if (components.All(x => x.CategoryCode != CatalogCodes.Gpu))
            specifications.Add(new CatalogPresetSpecification("GPU", "iGPU"));
        return new(
            preset.Id,
            preset.Slug,
            preset.Name,
            preset.DescriptionDe,
            preset.DescriptionEn,
            preset.DescriptionIt,
            preset.BadgeDe,
            preset.BadgeEn,
            preset.BadgeIt,
            preset.ImageUrl ?? components.FirstOrDefault(x => x.CategoryCode == CatalogCodes.Case)?.ImageUrl
                ?? "/images/pcs/werk-one.webp",
            components.Sum(x => x.UnitPrice * x.Quantity),
            "CHF",
            components,
            specifications.OrderBy(x => SpecificationOrder(x.Label)).ToArray());
    }

    private static CatalogPresetComponent MapComponent(PcPresetProduct presetProduct)
    {
        var product = presetProduct.Product;
        var offer = product.Offers
            .Where(IsAvailableOffer)
            .OrderBy(x => x.TotalPrice)
            .ThenBy(x => x.Retailer.Priority)
            .First();
        return new(
            product.Id,
            product.Category.Code,
            product.Category.Name,
            product.Name,
            product.Manufacturer,
            product.ManufacturerPartNumber,
            product.ImageUrl,
            offer.SourceUrl,
            offer.Retailer.Name,
            offer.TotalPrice,
            offer.Currency,
            presetProduct.Quantity);
    }

    private static string ComponentSpecification(CatalogPresetComponent component) =>
        component.Quantity > 1 ? $"{component.Quantity}× {component.ProductName}" : component.ProductName;

    private static string CategoryLabel(string code) => code switch
    {
        CatalogCodes.Cpu => "CPU",
        CatalogCodes.Gpu => "GPU",
        CatalogCodes.Ram => "RAM",
        CatalogCodes.Storage => "Storage",
        _ => code
    };

    private static int SpecificationOrder(string label) => label switch
    {
        "CPU" => 10,
        "GPU" => 20,
        "RAM" => 30,
        "Storage" => 40,
        _ => 100
    };
}
