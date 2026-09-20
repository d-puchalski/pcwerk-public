using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace EFDB;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<CatalogCategory> CatalogCategories => Set<CatalogCategory>();
    public DbSet<SourceCategory> SourceCategories => Set<SourceCategory>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<PcPreset> PcPresets => Set<PcPreset>();
    public DbSet<PcPresetProduct> PcPresetProducts => Set<PcPresetProduct>();
    public DbSet<RankingSnapshot> RankingSnapshots => Set<RankingSnapshot>();
    public DbSet<RankingItem> RankingItems => Set<RankingItem>();
    public DbSet<Retailer> Retailers => Set<Retailer>();
    public DbSet<Offer> Offers => Set<Offer>();
    public DbSet<OfferObservation> OfferObservations => Set<OfferObservation>();
    public DbSet<ImportRun> ImportRuns => Set<ImportRun>();
    public DbSet<ImportError> ImportErrors => Set<ImportError>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderItemDetail> OrderItemDetails => Set<OrderItemDetail>();
    public DbSet<OrderItemComponent> OrderItemComponents => Set<OrderItemComponent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("main");
        ConfigureCatalog(modelBuilder);
        ConfigureSpecifications(modelBuilder);
        ConfigureCommerce(modelBuilder);
        ConfigureOrders(modelBuilder);
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }
        }
    }

    private static void ConfigureCatalog(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CatalogCategory>(entity =>
        {
            entity.ToTable("categories");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).UseIdentityAlwaysColumn();
            entity.Property(x => x.Code).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.HasIndex(x => x.Code).IsUnique();
        });

        modelBuilder.Entity<SourceCategory>(entity =>
        {
            entity.ToTable("source_categories");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ExternalCategoryId).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Url).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.ProductSubtype).HasMaxLength(32);
            entity.HasIndex(x => x.ExternalCategoryId).IsUnique();
            entity.HasOne(x => x.Category).WithMany(x => x.SourceCategories).HasForeignKey(x => x.CategoryId);
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("products");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(500).IsRequired();
            entity.Property(x => x.Manufacturer).HasMaxLength(120);
            entity.Property(x => x.ManufacturerPartNumber).HasMaxLength(160);
            entity.Property(x => x.Ean).HasMaxLength(32);
            entity.Property(x => x.SourceUrl).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.ImageUrl).HasMaxLength(1000);
            entity.Property(x => x.SellingPrice).HasPrecision(12, 2);
            entity.Property(x => x.LifecycleStatus).HasMaxLength(24).IsRequired();
            entity.Property(x => x.SpecificationStatus).HasMaxLength(24).IsRequired();
            entity.HasIndex(x => x.ToppreiseProductId).IsUnique();
            entity.HasIndex(x => new { x.CategoryId, x.IsVisible, x.CurrentRank });
            entity.HasIndex(x => x.ManufacturerPartNumber);
            entity.HasOne(x => x.Category).WithMany(x => x.Products).HasForeignKey(x => x.CategoryId);
            entity.HasOne(x => x.SourceCategory).WithMany(x => x.Products).HasForeignKey(x => x.SourceCategoryId);
            entity.HasOne(x => x.LastSeenImportRun).WithMany(x => x.LastSeenProducts)
                .HasForeignKey(x => x.LastSeenImportRunId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<PcPreset>(entity =>
        {
            entity.ToTable("pc_presets");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).UseIdentityAlwaysColumn();
            entity.Property(x => x.Slug).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(160).IsRequired();
            entity.Property(x => x.DescriptionDe).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.DescriptionEn).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.DescriptionIt).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.BadgeDe).HasMaxLength(80);
            entity.Property(x => x.BadgeEn).HasMaxLength(80);
            entity.Property(x => x.BadgeIt).HasMaxLength(80);
            entity.Property(x => x.ImageUrl).HasMaxLength(1000);
            entity.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
            entity.HasIndex(x => x.Slug).IsUnique();
            entity.HasIndex(x => new { x.IsEnabled, x.DisplayOrder });
        });

        modelBuilder.Entity<PcPresetProduct>(entity =>
        {
            entity.ToTable("pc_preset_products");
            entity.HasKey(x => new { x.PcPresetId, x.ProductId });
            entity.HasIndex(x => x.ProductId);
            entity.HasOne(x => x.PcPreset).WithMany(x => x.Products)
                .HasForeignKey(x => x.PcPresetId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Product).WithMany(x => x.PresetProducts)
                .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RankingSnapshot>(entity =>
        {
            entity.ToTable("ranking_snapshots");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.SourceCategoryId, x.ObservedAt });
            entity.HasOne(x => x.SourceCategory).WithMany(x => x.RankingSnapshots).HasForeignKey(x => x.SourceCategoryId);
            entity.HasOne(x => x.ImportRun).WithMany(x => x.RankingSnapshots).HasForeignKey(x => x.ImportRunId);
        });

        modelBuilder.Entity<RankingItem>(entity =>
        {
            entity.ToTable("ranking_items");
            entity.HasKey(x => new { x.RankingSnapshotId, x.Rank });
            entity.HasIndex(x => new { x.RankingSnapshotId, x.ProductId }).IsUnique();
            entity.Property(x => x.ListedProductPrice).HasPrecision(12, 2);
            entity.Property(x => x.ListedTotalPrice).HasPrecision(12, 2);
            entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            entity.HasOne(x => x.RankingSnapshot).WithMany(x => x.Items).HasForeignKey(x => x.RankingSnapshotId);
            entity.HasOne(x => x.Product).WithMany(x => x.RankingItems).HasForeignKey(x => x.ProductId);
        });

        modelBuilder.Entity<ImportRun>(entity =>
        {
            entity.ToTable("import_runs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Status).HasMaxLength(24).IsRequired();
            entity.Property(x => x.ParserVersion).HasMaxLength(40).IsRequired();
        });

        modelBuilder.Entity<ImportError>(entity =>
        {
            entity.ToTable("import_errors");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Stage).HasMaxLength(40).IsRequired();
            entity.Property(x => x.SourceUrl).HasMaxLength(1000);
            entity.Property(x => x.Message).HasMaxLength(4000).IsRequired();
            entity.HasOne(x => x.ImportRun).WithMany(x => x.Errors).HasForeignKey(x => x.ImportRunId);
        });
    }

    private static void ConfigureSpecifications(ModelBuilder modelBuilder)
    {
        ConfigureOneToOne<CpuSpecification>(modelBuilder, "cpu_specs");
        ConfigureOneToOne<MotherboardSpecification>(modelBuilder, "motherboard_specs");
        ConfigureOneToOne<RamSpecification>(modelBuilder, "ram_specs");
        ConfigureOneToOne<GpuSpecification>(modelBuilder, "gpu_specs");
        ConfigureOneToOne<StorageSpecification>(modelBuilder, "storage_specs");
        ConfigureOneToOne<CoolerSpecification>(modelBuilder, "cooler_specs");
        ConfigureOneToOne<PsuSpecification>(modelBuilder, "psu_specs");
        ConfigureOneToOne<CaseSpecification>(modelBuilder, "case_specs");
        ConfigureOneToOne<MonitorSpecification>(modelBuilder, "monitor_specs");
        ConfigureOneToOne<MouseSpecification>(modelBuilder, "mouse_specs");
        ConfigureOneToOne<KeyboardSpecification>(modelBuilder, "keyboard_specs");

        ConfigureStringCollection<MotherboardSupportedCpuGeneration, MotherboardSpecification>(modelBuilder,
            "motherboard_cpu_generations", "Generation", "SupportedCpuGenerations", 80);
        modelBuilder.Entity<MotherboardVideoOutput>(entity =>
        {
            entity.ToTable("motherboard_video_outputs");
            entity.HasKey(x => new { x.ProductId, x.OutputType, x.Version });
            entity.Property(x => x.OutputType).HasMaxLength(24);
            entity.Property(x => x.Version).HasMaxLength(16);
            entity.HasOne(x => x.MotherboardSpecification).WithMany(x => x.VideoOutputs)
                .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        });
        ConfigureStringCollection<GpuPowerConnectorRequirement, GpuSpecification>(modelBuilder,
            "gpu_power_connectors", "ConnectorType", "PowerConnectors", 32);
        ConfigureStringCollection<CoolerSupportedSocket, CoolerSpecification>(modelBuilder,
            "cooler_sockets", "Socket", "SupportedSockets", 40);
        ConfigureStringCollection<PsuPowerConnector, PsuSpecification>(modelBuilder,
            "psu_power_connectors", "ConnectorType", "PowerConnectors", 32);
        ConfigureStringCollection<CaseSupportedMotherboardFormFactor, CaseSpecification>(modelBuilder,
            "case_motherboard_form_factors", "FormFactor", "SupportedMotherboardFormFactors", 40);
        ConfigureStringCollection<CaseSupportedPsuFormFactor, CaseSpecification>(modelBuilder,
            "case_psu_form_factors", "FormFactor", "SupportedPsuFormFactors", 40);

        modelBuilder.Entity<CaseSupportedRadiator>(entity =>
        {
            entity.ToTable("case_radiator_support");
            entity.HasKey(x => new { x.ProductId, x.Location, x.SizeMm });
            entity.Property(x => x.Location).HasMaxLength(40);
            entity.HasOne(x => x.CaseSpecification).WithMany(x => x.SupportedRadiators)
                .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CpuSpecification>().Property(x => x.Socket).HasMaxLength(40);
        modelBuilder.Entity<CpuSpecification>().Property(x => x.Generation).HasMaxLength(80);
        modelBuilder.Entity<CpuSpecification>().Property(x => x.BaseClockGhz).HasPrecision(6, 2);
        modelBuilder.Entity<CpuSpecification>().Property(x => x.BoostClockGhz).HasPrecision(6, 2);
        modelBuilder.Entity<MotherboardSpecification>().Property(x => x.Socket).HasMaxLength(40);
        modelBuilder.Entity<MotherboardSpecification>().Property(x => x.Chipset).HasMaxLength(80);
        modelBuilder.Entity<MotherboardSpecification>().Property(x => x.FormFactor).HasMaxLength(40);
        modelBuilder.Entity<MotherboardSpecification>().Property(x => x.MemoryType).HasMaxLength(20);
        modelBuilder.Entity<RamSpecification>().Property(x => x.MemoryType).HasMaxLength(20);
        modelBuilder.Entity<RamSpecification>().Property(x => x.ModuleFormFactor).HasMaxLength(20);
        modelBuilder.Entity<RamSpecification>().Property(x => x.HeightMm).HasPrecision(8, 2);
        modelBuilder.Entity<GpuSpecification>().Property(x => x.Chipset).HasMaxLength(120);
        modelBuilder.Entity<GpuSpecification>().Property(x => x.MemoryType).HasMaxLength(20);
        modelBuilder.Entity<GpuSpecification>().Property(x => x.LengthMm).HasPrecision(8, 2);
        modelBuilder.Entity<GpuSpecification>().Property(x => x.HeightMm).HasPrecision(8, 2);
        modelBuilder.Entity<GpuSpecification>().Property(x => x.SlotWidth).HasPrecision(4, 2);
        modelBuilder.Entity<StorageSpecification>().Property(x => x.StorageType).HasMaxLength(24);
        modelBuilder.Entity<StorageSpecification>().Property(x => x.InterfaceType).HasMaxLength(40);
        modelBuilder.Entity<StorageSpecification>().Property(x => x.Protocol).HasMaxLength(24);
        modelBuilder.Entity<StorageSpecification>().Property(x => x.FormFactor).HasMaxLength(24);
        modelBuilder.Entity<CoolerSpecification>().Property(x => x.CoolerType).HasMaxLength(20);
        modelBuilder.Entity<CoolerSpecification>().Property(x => x.HeightMm).HasPrecision(8, 2);
        modelBuilder.Entity<PsuSpecification>().Property(x => x.FormFactor).HasMaxLength(24);
        modelBuilder.Entity<PsuSpecification>().Property(x => x.LengthMm).HasPrecision(8, 2);
        modelBuilder.Entity<PsuSpecification>().Property(x => x.EfficiencyRating).HasMaxLength(40);
        modelBuilder.Entity<PsuSpecification>().Property(x => x.AtxStandard).HasMaxLength(24);
        modelBuilder.Entity<CaseSpecification>().Property(x => x.MaximumGpuLengthMm).HasPrecision(8, 2);
        modelBuilder.Entity<CaseSpecification>().Property(x => x.MaximumGpuHeightMm).HasPrecision(8, 2);
        modelBuilder.Entity<CaseSpecification>().Property(x => x.MaximumGpuSlotWidth).HasPrecision(4, 2);
        modelBuilder.Entity<CaseSpecification>().Property(x => x.MaximumCpuCoolerHeightMm).HasPrecision(8, 2);
        modelBuilder.Entity<CaseSpecification>().Property(x => x.MaximumPsuLengthMm).HasPrecision(8, 2);
        modelBuilder.Entity<MonitorSpecification>().Property(x => x.ScreenSizeInches).HasPrecision(6, 2);
        modelBuilder.Entity<MonitorSpecification>().Property(x => x.PanelType).HasMaxLength(40);
        modelBuilder.Entity<MouseSpecification>().Property(x => x.WeightGrams).HasPrecision(8, 2);
        modelBuilder.Entity<MouseSpecification>().Property(x => x.Connectivity).HasMaxLength(80);
        modelBuilder.Entity<KeyboardSpecification>().Property(x => x.Layout).HasMaxLength(40);
        modelBuilder.Entity<KeyboardSpecification>().Property(x => x.Size).HasMaxLength(40);
        modelBuilder.Entity<KeyboardSpecification>().Property(x => x.SwitchType).HasMaxLength(120);
        modelBuilder.Entity<KeyboardSpecification>().Property(x => x.Connectivity).HasMaxLength(80);
    }

    private static void ConfigureCommerce(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Retailer>(entity =>
        {
            entity.ToTable("retailers");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ExternalKey).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.HasIndex(x => x.ExternalKey).IsUnique();
        });
        modelBuilder.Entity<Offer>(entity =>
        {
            entity.ToTable("offers");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ExternalOfferKey).HasMaxLength(240).IsRequired();
            entity.Property(x => x.RetailerSku).HasMaxLength(160);
            entity.Property(x => x.SourceUrl).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.ProductPrice).HasPrecision(12, 2);
            entity.Property(x => x.ShippingPrice).HasPrecision(12, 2);
            entity.Property(x => x.TotalPrice).HasPrecision(12, 2);
            entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            entity.Property(x => x.Availability).HasMaxLength(24).IsRequired();
            entity.HasIndex(x => new { x.ProductId, x.RetailerId, x.ExternalOfferKey }).IsUnique();
            entity.HasIndex(x => new { x.ProductId, x.IsCurrent, x.TotalPrice });
            entity.HasOne(x => x.Product).WithMany(x => x.Offers).HasForeignKey(x => x.ProductId);
            entity.HasOne(x => x.Retailer).WithMany(x => x.Offers).HasForeignKey(x => x.RetailerId);
        });
        modelBuilder.Entity<OfferObservation>(entity =>
        {
            entity.ToTable("offer_observations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ProductPrice).HasPrecision(12, 2);
            entity.Property(x => x.ShippingPrice).HasPrecision(12, 2);
            entity.Property(x => x.TotalPrice).HasPrecision(12, 2);
            entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            entity.Property(x => x.Availability).HasMaxLength(24).IsRequired();
            entity.HasIndex(x => new { x.OfferId, x.ObservedAt });
            entity.HasOne(x => x.Offer).WithMany(x => x.Observations).HasForeignKey(x => x.OfferId);
            entity.HasOne(x => x.ImportRun).WithMany().HasForeignKey(x => x.ImportRunId);
        });
    }

    private static void ConfigureOrders(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("orders");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Number).HasMaxLength(40).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(24).IsRequired();
            entity.Property(x => x.EmailNotificationStatus).HasMaxLength(24).IsRequired();
            entity.Property(x => x.EmailNotificationError).HasMaxLength(2000);
            entity.Property(x => x.CustomerFirstName).HasMaxLength(120).IsRequired();
            entity.Property(x => x.CustomerLastName).HasMaxLength(120).IsRequired();
            entity.Property(x => x.CustomerEmail).HasMaxLength(320).IsRequired();
            entity.Property(x => x.CustomerPhone).HasMaxLength(60);
            entity.Property(x => x.Company).HasMaxLength(200);
            entity.Property(x => x.Street).HasMaxLength(240);
            entity.Property(x => x.PostalCode).HasMaxLength(24);
            entity.Property(x => x.City).HasMaxLength(120);
            entity.Property(x => x.Language).HasMaxLength(8).IsRequired();
            entity.Property(x => x.Subtotal).HasPrecision(12, 2);
            entity.Property(x => x.Shipping).HasPrecision(12, 2);
            entity.Property(x => x.Total).HasPrecision(12, 2);
            entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            entity.HasIndex(x => x.Number).IsUnique();
        });
        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.ToTable("order_items");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ItemType).HasMaxLength(40).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(300).IsRequired();
            entity.Property(x => x.ImagePath).HasMaxLength(1000);
            entity.Property(x => x.UnitPrice).HasPrecision(12, 2);
            entity.Property(x => x.ConfigurationOrderType).HasMaxLength(40);
            entity.Property(x => x.ServicePackageCode).HasMaxLength(40);
            entity.HasIndex(x => new { x.OrderId, x.Position }).IsUnique();
            entity.HasOne(x => x.Order).WithMany(x => x.Items).HasForeignKey(x => x.OrderId);
            entity.HasOne(x => x.PcPreset).WithMany(x => x.OrderItems)
                .HasForeignKey(x => x.PcPresetId).OnDelete(DeleteBehavior.SetNull);
        });
        modelBuilder.Entity<OrderItemDetail>(entity =>
        {
            entity.ToTable("order_item_details");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Label).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Value).HasMaxLength(1000).IsRequired();
            entity.HasOne(x => x.OrderItem).WithMany(x => x.Details).HasForeignKey(x => x.OrderItemId);
        });
        modelBuilder.Entity<OrderItemComponent>(entity =>
        {
            entity.ToTable("order_item_components");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.CategoryCode).HasMaxLength(32).IsRequired();
            entity.Property(x => x.ProductName).HasMaxLength(500).IsRequired();
            entity.Property(x => x.Manufacturer).HasMaxLength(120);
            entity.Property(x => x.ManufacturerPartNumber).HasMaxLength(160);
            entity.Property(x => x.RetailerName).HasMaxLength(200);
            entity.Property(x => x.UnitPrice).HasPrecision(12, 2);
            entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            entity.HasIndex(x => new { x.OrderItemId, x.Position }).IsUnique();
            entity.HasOne(x => x.OrderItem).WithMany(x => x.Components).HasForeignKey(x => x.OrderItemId);
            entity.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureOneToOne<TEntity>(ModelBuilder modelBuilder, string tableName)
        where TEntity : class
    {
        modelBuilder.Entity<TEntity>(entity =>
        {
            entity.ToTable(tableName);
            entity.HasKey("ProductId");
            entity.HasOne(typeof(Product), "Product").WithOne(typeof(TEntity).Name)
                .HasForeignKey(typeof(TEntity), "ProductId").OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureStringCollection<TChild, TParent>(ModelBuilder modelBuilder, string tableName,
        string valueProperty, string parentNavigation, int maxLength)
        where TChild : class
        where TParent : class
    {
        modelBuilder.Entity<TChild>(entity =>
        {
            entity.ToTable(tableName);
            entity.HasKey("ProductId", valueProperty);
            entity.Property(valueProperty).HasMaxLength(maxLength);
            entity.HasOne(typeof(TParent), typeof(TParent).Name).WithMany(parentNavigation)
                .HasForeignKey("ProductId").OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static string ToSnakeCase(string value) =>
        Regex.Replace(value, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant();
}
