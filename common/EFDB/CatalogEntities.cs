namespace EFDB;

public static class CatalogCodes
{
    public const string Cpu = "CPU";
    public const string Gpu = "GPU";
    public const string Motherboard = "MOTHERBOARD";
    public const string Ram = "RAM";
    public const string Storage = "STORAGE";
    public const string Cooling = "COOLING";
    public const string Psu = "PSU";
    public const string Case = "CASE";
    public const string Monitor = "MONITOR";
    public const string Mouse = "MOUSE";
    public const string Keyboard = "KEYBOARD";
    public const string Laptop = "LAPTOP";
}

public sealed class CatalogCategory
{
    public short Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public bool IsRequired { get; set; }
    public bool IsEnabled { get; set; } = true;

    public ICollection<SourceCategory> SourceCategories { get; set; } = [];
    public ICollection<Product> Products { get; set; } = [];
}

public sealed class SourceCategory
{
    public int Id { get; set; }
    public short CategoryId { get; set; }
    public string ExternalCategoryId { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? ProductSubtype { get; set; }
    public int TopLimit { get; set; } = 100;
    public bool IsEnabled { get; set; } = true;

    public CatalogCategory Category { get; set; } = null!;
    public ICollection<Product> Products { get; set; } = [];
    public ICollection<RankingSnapshot> RankingSnapshots { get; set; } = [];
}

public sealed class Product
{
    public long Id { get; set; }
    public short CategoryId { get; set; }
    public int SourceCategoryId { get; set; }
    public long ToppreiseProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Manufacturer { get; set; }
    public string? ManufacturerPartNumber { get; set; }
    public string? Ean { get; set; }
    public string SourceUrl { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public decimal? SellingPrice { get; set; }
    public string LifecycleStatus { get; set; } = "active";
    public string SpecificationStatus { get; set; } = "pending";
    public bool IsVisible { get; set; }
    public int CurrentRank { get; set; }
    public DateTimeOffset? RankedAt { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset? SpecificationsObservedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long? LastSeenImportRunId { get; set; }

    public CatalogCategory Category { get; set; } = null!;
    public SourceCategory SourceCategory { get; set; } = null!;
    public ImportRun? LastSeenImportRun { get; set; }
    public ICollection<RankingItem> RankingItems { get; set; } = [];
    public ICollection<Offer> Offers { get; set; } = [];
    public ICollection<PcPresetProduct> PresetProducts { get; set; } = [];

    public CpuSpecification? CpuSpecification { get; set; }
    public MotherboardSpecification? MotherboardSpecification { get; set; }
    public RamSpecification? RamSpecification { get; set; }
    public GpuSpecification? GpuSpecification { get; set; }
    public StorageSpecification? StorageSpecification { get; set; }
    public CoolerSpecification? CoolerSpecification { get; set; }
    public PsuSpecification? PsuSpecification { get; set; }
    public CaseSpecification? CaseSpecification { get; set; }
    public MonitorSpecification? MonitorSpecification { get; set; }
    public MouseSpecification? MouseSpecification { get; set; }
    public KeyboardSpecification? KeyboardSpecification { get; set; }
}

public sealed class PcPreset
{
    public long Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DescriptionDe { get; set; } = string.Empty;
    public string DescriptionEn { get; set; } = string.Empty;
    public string DescriptionIt { get; set; } = string.Empty;
    public string? BadgeDe { get; set; }
    public string? BadgeEn { get; set; }
    public string? BadgeIt { get; set; }
    public string? ImageUrl { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<PcPresetProduct> Products { get; set; } = [];
    public ICollection<OrderItem> OrderItems { get; set; } = [];
}

public sealed class PcPresetProduct
{
    public long PcPresetId { get; set; }
    public long ProductId { get; set; }
    public short Quantity { get; set; } = 1;
    public int DisplayOrder { get; set; }

    public PcPreset PcPreset { get; set; } = null!;
    public Product Product { get; set; } = null!;
}

public sealed class RankingSnapshot
{
    public long Id { get; set; }
    public int SourceCategoryId { get; set; }
    public long ImportRunId { get; set; }
    public DateTimeOffset ObservedAt { get; set; }
    public int ItemCount { get; set; }

    public SourceCategory SourceCategory { get; set; } = null!;
    public ImportRun ImportRun { get; set; } = null!;
    public ICollection<RankingItem> Items { get; set; } = [];
}

public sealed class RankingItem
{
    public long RankingSnapshotId { get; set; }
    public int Rank { get; set; }
    public long ProductId { get; set; }
    public decimal? ListedProductPrice { get; set; }
    public decimal? ListedTotalPrice { get; set; }
    public string Currency { get; set; } = "CHF";

    public RankingSnapshot RankingSnapshot { get; set; } = null!;
    public Product Product { get; set; } = null!;
}

public sealed class ImportRun
{
    public long Id { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string Status { get; set; } = "running";
    public string ParserVersion { get; set; } = string.Empty;
    public int CategoryCount { get; set; }
    public int ProductCount { get; set; }
    public int OfferCount { get; set; }
    public int ErrorCount { get; set; }
    public string? ErrorMessage { get; set; }

    public ICollection<Product> LastSeenProducts { get; set; } = [];
    public ICollection<RankingSnapshot> RankingSnapshots { get; set; } = [];
    public ICollection<ImportError> Errors { get; set; } = [];
}

public sealed class ImportError
{
    public long Id { get; set; }
    public long ImportRunId { get; set; }
    public string Stage { get; set; } = string.Empty;
    public string? SourceUrl { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }

    public ImportRun ImportRun { get; set; } = null!;
}
