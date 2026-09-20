namespace EFDB;

public sealed class CpuSpecification
{
    public long ProductId { get; set; }
    public string Socket { get; set; } = string.Empty;
    public string? Generation { get; set; }
    public int? Cores { get; set; }
    public int? Threads { get; set; }
    public decimal? BaseClockGhz { get; set; }
    public decimal? BoostClockGhz { get; set; }
    public int? TdpWatts { get; set; }
    public bool? HasIntegratedGraphics { get; set; }
    public Product Product { get; set; } = null!;
}

public sealed class MotherboardSpecification
{
    public long ProductId { get; set; }
    public string Socket { get; set; } = string.Empty;
    public string? Chipset { get; set; }
    public string FormFactor { get; set; } = string.Empty;
    public string MemoryType { get; set; } = string.Empty;
    public int? MemorySlots { get; set; }
    public int? MaximumMemoryGb { get; set; }
    public int? M2SlotCount { get; set; }
    public int? SataPortCount { get; set; }
    public bool? SupportsEcc { get; set; }
    public Product Product { get; set; } = null!;
    public ICollection<MotherboardSupportedCpuGeneration> SupportedCpuGenerations { get; set; } = [];
    public ICollection<MotherboardVideoOutput> VideoOutputs { get; set; } = [];
}

public sealed class MotherboardSupportedCpuGeneration
{
    public long ProductId { get; set; }
    public string Generation { get; set; } = string.Empty;
    public MotherboardSpecification MotherboardSpecification { get; set; } = null!;
}

public sealed class MotherboardVideoOutput
{
    public long ProductId { get; set; }
    public string OutputType { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
    public MotherboardSpecification MotherboardSpecification { get; set; } = null!;
}

public sealed class RamSpecification
{
    public long ProductId { get; set; }
    public string MemoryType { get; set; } = string.Empty;
    public string? ModuleFormFactor { get; set; }
    public int CapacityGb { get; set; }
    public int ModuleCount { get; set; }
    public int? SpeedMtPerSecond { get; set; }
    public bool? IsEcc { get; set; }
    public bool? IsRegistered { get; set; }
    public decimal? HeightMm { get; set; }
    public Product Product { get; set; } = null!;
}

public sealed class GpuSpecification
{
    public long ProductId { get; set; }
    public string? Chipset { get; set; }
    public int? MemoryGb { get; set; }
    public string? MemoryType { get; set; }
    public int? TdpWatts { get; set; }
    public int? RecommendedPsuWatts { get; set; }
    public decimal LengthMm { get; set; }
    public decimal? HeightMm { get; set; }
    public decimal? SlotWidth { get; set; }
    public Product Product { get; set; } = null!;
    public ICollection<GpuPowerConnectorRequirement> PowerConnectors { get; set; } = [];
}

public sealed class GpuPowerConnectorRequirement
{
    public long ProductId { get; set; }
    public string ConnectorType { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public GpuSpecification GpuSpecification { get; set; } = null!;
}

public sealed class StorageSpecification
{
    public long ProductId { get; set; }
    public string StorageType { get; set; } = string.Empty;
    public string InterfaceType { get; set; } = string.Empty;
    public string? Protocol { get; set; }
    public string? FormFactor { get; set; }
    public int CapacityGb { get; set; }
    public int? M2LengthMm { get; set; }
    public Product Product { get; set; } = null!;
}

public sealed class CoolerSpecification
{
    public long ProductId { get; set; }
    public string CoolerType { get; set; } = string.Empty;
    public decimal? HeightMm { get; set; }
    public int? RadiatorSizeMm { get; set; }
    public int? TdpCapacityWatts { get; set; }
    public Product Product { get; set; } = null!;
    public ICollection<CoolerSupportedSocket> SupportedSockets { get; set; } = [];
}

public sealed class CoolerSupportedSocket
{
    public long ProductId { get; set; }
    public string Socket { get; set; } = string.Empty;
    public CoolerSpecification CoolerSpecification { get; set; } = null!;
}

public sealed class PsuSpecification
{
    public long ProductId { get; set; }
    public int Wattage { get; set; }
    public string FormFactor { get; set; } = string.Empty;
    public decimal? LengthMm { get; set; }
    public string? EfficiencyRating { get; set; }
    public string? AtxStandard { get; set; }
    public Product Product { get; set; } = null!;
    public ICollection<PsuPowerConnector> PowerConnectors { get; set; } = [];
}

public sealed class PsuPowerConnector
{
    public long ProductId { get; set; }
    public string ConnectorType { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public PsuSpecification PsuSpecification { get; set; } = null!;
}

public sealed class CaseSpecification
{
    public long ProductId { get; set; }
    public decimal MaximumGpuLengthMm { get; set; }
    public decimal? MaximumGpuHeightMm { get; set; }
    public decimal? MaximumGpuSlotWidth { get; set; }
    public decimal? MaximumCpuCoolerHeightMm { get; set; }
    public decimal? MaximumPsuLengthMm { get; set; }
    public Product Product { get; set; } = null!;
    public ICollection<CaseSupportedMotherboardFormFactor> SupportedMotherboardFormFactors { get; set; } = [];
    public ICollection<CaseSupportedPsuFormFactor> SupportedPsuFormFactors { get; set; } = [];
    public ICollection<CaseSupportedRadiator> SupportedRadiators { get; set; } = [];
}

public sealed class CaseSupportedMotherboardFormFactor
{
    public long ProductId { get; set; }
    public string FormFactor { get; set; } = string.Empty;
    public CaseSpecification CaseSpecification { get; set; } = null!;
}

public sealed class CaseSupportedPsuFormFactor
{
    public long ProductId { get; set; }
    public string FormFactor { get; set; } = string.Empty;
    public CaseSpecification CaseSpecification { get; set; } = null!;
}

public sealed class CaseSupportedRadiator
{
    public long ProductId { get; set; }
    public string Location { get; set; } = string.Empty;
    public int SizeMm { get; set; }
    public CaseSpecification CaseSpecification { get; set; } = null!;
}

public sealed class MonitorSpecification
{
    public long ProductId { get; set; }
    public decimal? ScreenSizeInches { get; set; }
    public int? ResolutionWidth { get; set; }
    public int? ResolutionHeight { get; set; }
    public int? RefreshRateHz { get; set; }
    public string? PanelType { get; set; }
    public Product Product { get; set; } = null!;
}

public sealed class MouseSpecification
{
    public long ProductId { get; set; }
    public int? MaximumDpi { get; set; }
    public decimal? WeightGrams { get; set; }
    public string? Connectivity { get; set; }
    public Product Product { get; set; } = null!;
}

public sealed class KeyboardSpecification
{
    public long ProductId { get; set; }
    public string? Layout { get; set; }
    public string? Size { get; set; }
    public string? SwitchType { get; set; }
    public string? Connectivity { get; set; }
    public Product Product { get; set; } = null!;
}
