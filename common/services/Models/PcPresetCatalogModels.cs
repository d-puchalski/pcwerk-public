namespace Services.Models;

public sealed record CatalogPresetSpecification(string Label, string Value);

public sealed record CatalogPresetComponent(
    long ProductId,
    string CategoryCode,
    string CategoryName,
    string ProductName,
    string? Manufacturer,
    string? ManufacturerPartNumber,
    string? ImageUrl,
    string OfferUrl,
    string Retailer,
    decimal UnitPrice,
    string Currency,
    short Quantity);

public sealed record CatalogPcPreset(
    long PresetId,
    string Slug,
    string Name,
    string DescriptionDe,
    string DescriptionEn,
    string DescriptionIt,
    string? BadgeDe,
    string? BadgeEn,
    string? BadgeIt,
    string ImageUrl,
    decimal Price,
    string Currency,
    IReadOnlyList<CatalogPresetComponent> Components,
    IReadOnlyList<CatalogPresetSpecification> Specifications);
