namespace ToppreiseCatalog.Import;

internal sealed record RankedProduct(
    long ToppreiseProductId,
    int Rank,
    string Name,
    string Url);

internal sealed record BrowserRankingRow(
    string? Url,
    string? Name);

internal sealed record ScrapedProduct(
    string Name,
    string? Manufacturer,
    string? ManufacturerPartNumber,
    string? Ean,
    string? ImageUrl,
    ParsedSpecifications Specifications,
    IReadOnlyList<ScrapedOffer> Offers);

internal sealed record ScrapedOffer(
    string ExternalKey,
    string RetailerName,
    string? RetailerSku,
    decimal ProductPrice,
    decimal ShippingPrice,
    decimal TotalPrice,
    string Availability,
    int? DeliveryMinBusinessDays,
    int? DeliveryMaxBusinessDays);

internal sealed record BrowserOfferRow(
    string? ExternalKey,
    string? RetailerName,
    string? ProductPrice,
    string? TotalPrice,
    string? AvailabilityText);

internal sealed record ParsedVideoOutput(
    string OutputType,
    string Version,
    int Quantity);

internal sealed record ParsedSpecifications
{
    public string? Socket { get; init; }
    public string? CpuGeneration { get; init; }
    public int? Cores { get; init; }
    public int? Threads { get; init; }
    public decimal? BaseClockGhz { get; init; }
    public decimal? BoostClockGhz { get; init; }
    public int? TdpWatts { get; init; }
    public bool? HasIntegratedGraphics { get; init; }
    public string? Chipset { get; init; }
    public string? FormFactor { get; init; }
    public string? MemoryType { get; init; }
    public int? MemorySlots { get; init; }
    public int? MaximumMemoryGb { get; init; }
    public int? M2SlotCount { get; init; }
    public int? SataPortCount { get; init; }
    public bool? SupportsEcc { get; init; }
    public IReadOnlyList<string> SupportedCpuGenerations { get; init; } = [];
    public IReadOnlyList<ParsedVideoOutput> VideoOutputs { get; init; } = [];
    public string? ModuleFormFactor { get; init; }
    public int? CapacityGb { get; init; }
    public int? ModuleCount { get; init; }
    public int? SpeedMtPerSecond { get; init; }
    public bool? IsEcc { get; init; }
    public bool? IsRegistered { get; init; }
    public decimal? HeightMm { get; init; }
    public string? GpuChipset { get; init; }
    public int? GpuMemoryGb { get; init; }
    public string? GpuMemoryType { get; init; }
    public int? RecommendedPsuWatts { get; init; }
    public decimal? LengthMm { get; init; }
    public decimal? GpuHeightMm { get; init; }
    public decimal? SlotWidth { get; init; }
    public IReadOnlyDictionary<string, int> PowerConnectors { get; init; } = new Dictionary<string, int>();
    public string? StorageType { get; init; }
    public string? InterfaceType { get; init; }
    public string? Protocol { get; init; }
    public string? StorageFormFactor { get; init; }
    public int? M2LengthMm { get; init; }
    public string? CoolerType { get; init; }
    public int? RadiatorSizeMm { get; init; }
    public int? TdpCapacityWatts { get; init; }
    public IReadOnlyList<string> SupportedSockets { get; init; } = [];
    public int? Wattage { get; init; }
    public decimal? PsuLengthMm { get; init; }
    public string? EfficiencyRating { get; init; }
    public string? AtxStandard { get; init; }
    public decimal? MaximumGpuLengthMm { get; init; }
    public decimal? MaximumGpuHeightMm { get; init; }
    public decimal? MaximumGpuSlotWidth { get; init; }
    public decimal? MaximumCpuCoolerHeightMm { get; init; }
    public decimal? MaximumPsuLengthMm { get; init; }
    public IReadOnlyList<string> SupportedMotherboardFormFactors { get; init; } = [];
    public IReadOnlyList<string> SupportedPsuFormFactors { get; init; } = [];
    public IReadOnlyList<(string Location, int SizeMm)> SupportedRadiators { get; init; } = [];
    public decimal? ScreenSizeInches { get; init; }
    public int? ResolutionWidth { get; init; }
    public int? ResolutionHeight { get; init; }
    public int? RefreshRateHz { get; init; }
    public string? PanelType { get; init; }
    public int? MaximumDpi { get; init; }
    public decimal? WeightGrams { get; init; }
    public string? Connectivity { get; init; }
    public string? KeyboardLayout { get; init; }
    public string? KeyboardSize { get; init; }
    public string? SwitchType { get; init; }
}

internal sealed class SourceBlockedException(string message) : Exception(message);
