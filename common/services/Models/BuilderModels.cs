namespace Services.Models;

public enum BuilderCategory
{
    Cpu,
    Gpu,
    Motherboard,
    Ram,
    Storage,
    Cooling,
    Psu,
    Case,
    Monitor,
    Mouse,
    Keyboard
}

public sealed record VideoOutput(
    string OutputType,
    string Version,
    int Quantity)
{
    public string DisplayName => $"{(Quantity > 1 ? $"{Quantity} × " : string.Empty)}{OutputType}{(string.IsNullOrWhiteSpace(Version) ? string.Empty : $" {Version}")}";
}

public sealed record ProductCompatibility(
    string? Socket = null,
    string? CpuGeneration = null,
    IReadOnlyList<string>? SupportedCpuGenerations = null,
    string? RamGeneration = null,
    int? RamModuleCount = null,
    int? RamSlotCount = null,
    string? FormFactor = null,
    decimal? PowerDrawWatts = null,
    int? GpuLengthMm = null,
    int? GpuHeightMm = null,
    decimal? GpuSlotWidth = null,
    int? MaxGpuLengthMm = null,
    int? MaxGpuHeightMm = null,
    decimal? MaxGpuSlotWidth = null,
    int? PsuWattage = null,
    string? PsuFormFactor = null,
    int? PsuLengthMm = null,
    int? MaxPsuLengthMm = null,
    int? CoolerHeightMm = null,
    int? MaxCoolerHeightMm = null,
    int? RadiatorSizeMm = null,
    IReadOnlyList<int>? RadiatorSupportMm = null,
    IReadOnlyList<string>? SupportedFormFactors = null,
    IReadOnlyList<string>? SupportedPsuFormFactors = null,
    IReadOnlyList<string>? SupportedSockets = null,
    string? StorageInterface = null,
    IReadOnlyList<string>? SupportedStorageInterfaces = null,
    int? RecommendedPsuWatts = null,
    int? Pcie8Pin = null,
    int? Pcie12Vhpwr = null,
    int? Pcie12V2X6 = null,
    bool? HasIntegratedGraphics = null,
    IReadOnlyList<VideoOutput>? VideoOutputs = null);

public sealed record ProductOffer(
    decimal Price,
    string Currency,
    string Retailer,
    int DeliveryMinBusinessDays,
    int DeliveryMaxBusinessDays,
    DateTimeOffset ObservedAt);

public enum ProductSort
{
    Recommended,
    PriceAscending,
    PriceDescending,
    TopRank,
    Name
}

public sealed record ProductFilterValue(string Key, string Label, string Value);

public sealed record ProductFilterDefinition(string Key, string Label, IReadOnlyList<string> Values);

public sealed record ProductSpecificationDetail(string Label, string Value);

public sealed record BuilderProduct(
    long DatabaseId,
    string Id,
    string Name,
    BuilderCategory Category,
    string Brand,
    string Image,
    ProductOffer Offer,
    IReadOnlyList<string> Specs,
    ProductCompatibility Compatibility,
    IReadOnlyList<string> Badges,
    bool Recommended = false,
    IReadOnlyList<string>? Filters = null,
    string? ManufacturerPartNumber = null,
    int TopRank = 0,
    IReadOnlyList<ProductFilterValue>? FilterValues = null,
    IReadOnlyList<ProductSpecificationDetail>? SpecificationDetails = null,
    string? SourceUrl = null)
{
    public decimal Price => Offer.Price;
}

public sealed class BuilderBuild
{
    public BuilderProduct? Cpu { get; set; }
    public BuilderProduct? Gpu { get; set; }
    public BuilderProduct? Motherboard { get; set; }
    public BuilderProduct? Ram { get; set; }
    public BuilderProduct? Storage { get; set; }
    public BuilderProduct? Cooling { get; set; }
    public BuilderProduct? Psu { get; set; }
    public BuilderProduct? Case { get; set; }
    public BuilderProduct? Monitor { get; set; }
    public BuilderProduct? Mouse { get; set; }
    public BuilderProduct? Keyboard { get; set; }
    public BuildGraphicsMode GraphicsMode { get; set; } = BuildGraphicsMode.Discrete;
    public BuildOrderType? OrderType { get; set; }
    public ServicePackage? ServicePackage { get; set; }
    public List<Service> ServiceAddons { get; } = [];
}

public enum BuildGraphicsMode
{
    Discrete,
    Integrated
}

public sealed record BuildStatus(
    int SelectedCount,
    int RequiredCount,
    bool IsCompatible,
    IReadOnlyList<BuilderCategory> MissingComponents,
    IReadOnlyList<string> Warnings,
    decimal TotalPrice);

public sealed record BuildPreset(
    string Id,
    string Name,
    string DescriptionTranslationKey,
    decimal StartingPrice,
    string Icon,
    IReadOnlyDictionary<BuilderCategory, string> ProductIds);

public enum BuildOrderType
{
    ComponentsOnly,
    PcWerkBuild
}

public sealed record ServicePackage(
    string Id,
    string NameTranslationKey,
    string DescriptionTranslationKey,
    decimal Price,
    bool Recommended,
    IReadOnlyList<string> IncludedServices,
    string OutcomeTranslationKey);

public sealed record Service(
    string Id,
    string NameTranslationKey,
    string DescriptionTranslationKey,
    decimal Price,
    string Category,
    IReadOnlyList<string> IncludedInPackages,
    bool IsOptional = false,
    bool IsFromPrice = false,
    string? AvailabilityNoteTranslationKey = null,
    string? SelectionGroup = null,
    string? DiscountedByIncludedServiceId = null,
    decimal IncludedServiceDiscount = 0);

public sealed record BuildPricing(
    decimal HardwareSubtotal,
    decimal ServicePackagePrice,
    decimal AddonsTotal,
    decimal DeliveryPrice,
    decimal Total);

public sealed record BuildDeliveryEstimate(
    int MinBusinessDays,
    int MaxBusinessDays);

public sealed record OrderSummary(
    decimal Hardware,
    decimal Services,
    decimal Delivery,
    decimal Total);
