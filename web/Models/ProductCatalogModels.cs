namespace PcSell.Models;

public sealed record Review(string Name, string Location, int Rating, string Text, string Build);

public enum ProductCategory
{
    Processor, GraphicsCard, Motherboard, Memory, Storage, CpuCooling, PowerSupply, Case, OperatingSystem
}

public sealed record Product(Guid Id,
    string Name,
    ProductCategory Category,
    string Manufacturer,
    ComponentSpecifications Specifications,
    decimal SellingPrice,
    string? Image,
    IReadOnlyList<SupplierOffer> Suppliers);

public sealed record SupplierOffer(string Supplier,
    string SupplierSku,
    Uri SupplierUrl,
    decimal PurchasePrice,
    bool InStock,
    DateTimeOffset LastUpdated);

public sealed record ComponentSpecifications(
    string? Socket = null,
    int? TdpWatts = null,
    string? RamType = null,
    string? FormFactor = null,
    string? DdrGeneration = null,
    int? GpuLengthMm = null,
    int? RecommendedPsuWatts = null,
    IReadOnlyList<string>? MotherboardFormFactors = null,
    int? MaxGpuLengthMm = null,
    IReadOnlyList<int>? RadiatorSupportMm = null,
    int? PsuWattage = null,
    IReadOnlyList<string>? SupportedSockets = null,
    int? RadiatorSizeMm = null,
    int? CoolerHeightMm = null);

public sealed record ConfiguratorPricingSettings(bool AssemblyIncluded, decimal AssemblyAndTestingFee, string PriceChangeNotice);

public static class SiteContentSettings
{
    public const string WarrantyDe = "Klare Garantiebedingungen und eine Anlaufstelle für dein komplettes System.";
    public const string WarrantyEn = "Clear warranty terms and one point of contact for your complete system.";
}
