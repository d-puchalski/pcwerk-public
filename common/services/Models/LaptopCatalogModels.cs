namespace Services.Models;

public sealed record CatalogLaptopComponent(string CategoryCode, string Name);

public sealed record CatalogLaptop(
    long ProductId,
    string Name,
    string? Manufacturer,
    string? ManufacturerPartNumber,
    string? ImageUrl,
    string SourceUrl,
    decimal Price,
    string Currency,
    int Rank,
    IReadOnlyList<CatalogLaptopComponent> Components);
