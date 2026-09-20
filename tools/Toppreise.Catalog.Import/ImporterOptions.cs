namespace ToppreiseCatalog.Import;

internal sealed class ImporterOptions
{
    public const string SectionName = "ToppreiseImport";

    public bool Headless { get; init; }
    public ImportCategoryOptions Categories { get; init; } = new();
    public int RequestDelayMilliseconds { get; init; } = 1500;
    public int NavigationTimeoutMilliseconds { get; init; } = 45000;
    public int ManualVerificationTimeoutSeconds { get; init; } = 180;
    public int SpecificationRefreshDays { get; init; } = 30;
    public string ParserVersion { get; init; } = "1.4.0";
}

internal sealed class ImportCategoryOptions
{
    public bool Cpu { get; init; } = true;
    public bool Motherboard { get; init; } = true;
    public bool Ram { get; init; } = true;
    public bool Gpu { get; init; } = true;
    public bool Storage { get; init; } = true;
    public bool Cooling { get; init; } = true;
    public bool Psu { get; init; } = true;
    public bool Case { get; init; } = true;
    public bool Monitor { get; init; } = true;
    public bool Mouse { get; init; } = true;
    public bool Keyboard { get; init; } = true;
    public bool Laptop { get; init; } = true;

    public bool IsEnabled(string categoryCode) => categoryCode.ToUpperInvariant() switch
    {
        "CPU" => Cpu,
        "MOTHERBOARD" => Motherboard,
        "RAM" => Ram,
        "GPU" => Gpu,
        "STORAGE" => Storage,
        "COOLING" => Cooling,
        "PSU" => Psu,
        "CASE" => Case,
        "MONITOR" => Monitor,
        "MOUSE" => Mouse,
        "KEYBOARD" => Keyboard,
        "LAPTOP" => Laptop,
        _ => true
    };
}

internal sealed class PcPresetOptions
{
    public const string SectionName = "PcPresets";

    public PcPresetProfileOptions[] Profiles { get; init; } = [];
}

internal sealed class PcPresetProfileOptions
{
    public string Name { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string GraphicsMode { get; init; } = "Integrated";
    public string[] CpuModels { get; init; } = [];
    public string[] GpuModels { get; init; } = [];
    public int[] RamCapacitiesGb { get; init; } = [];
    public int[] StorageCapacitiesGb { get; init; } = [];
}
