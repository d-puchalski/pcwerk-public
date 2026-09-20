using EFDB;
using Microsoft.EntityFrameworkCore;
using Services.Models;
using System.Globalization;

namespace Services.Configurator;

/// <summary>Reads the single, published Toppreise catalogue used by the configurator.</summary>
public sealed class EfProductCatalog(
    IDbContextFactory<AppDbContext> contextFactory,
    TimeProvider timeProvider)
{
    private static readonly string[] BuilderCategoryCodes =
    [
        CatalogCodes.Cpu,
        CatalogCodes.Motherboard,
        CatalogCodes.Ram,
        CatalogCodes.Gpu,
        CatalogCodes.Storage,
        CatalogCodes.Cooling,
        CatalogCodes.Psu,
        CatalogCodes.Case,
        CatalogCodes.Monitor,
        CatalogCodes.Mouse,
        CatalogCodes.Keyboard
    ];
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(15);
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private IReadOnlyList<BuilderProduct>? _products;
    private DateTimeOffset _loadedAt;

    public async Task<IReadOnlyList<BuilderProduct>> GetProductsAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        if (_products is not null && now - _loadedAt < CacheLifetime)
        {
            return _products;
        }

        await _loadLock.WaitAsync(cancellationToken);
        try
        {
            now = timeProvider.GetUtcNow();
            if (_products is not null && now - _loadedAt < CacheLifetime)
            {
                return _products;
            }

            _products = await LoadProductsAsync(null, cancellationToken);
            _loadedAt = now;
            return _products;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    public async Task<IReadOnlyList<BuilderProduct>> GetCurrentProductsByIdsAsync(
        IEnumerable<long> productIds,
        CancellationToken cancellationToken = default)
    {
        var ids = productIds.Distinct().ToArray();
        return ids.Length == 0
            ? []
            : await LoadProductsAsync(ids, cancellationToken);
    }

    private async Task<IReadOnlyList<BuilderProduct>> LoadProductsAsync(
        long[]? productIds,
        CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var products = db.Products
            .AsNoTracking()
            .Where(x => BuilderCategoryCodes.Contains(x.Category.Code))
            .Where(x => x.IsVisible && x.LifecycleStatus == "active" && x.SpecificationStatus == "valid")
            .Where(x => x.Offers.Any(o => o.IsCurrent && o.Currency == "CHF"
                && o.Availability != "unavailable" && o.TotalPrice > 0
                && o.Retailer.IsEnabled));
        if (productIds is not null)
        {
            products = products.Where(x => productIds.Contains(x.Id));
        }

        var entities = await products
            .Include(x => x.Category)
            .Include(x => x.Offers).ThenInclude(x => x.Retailer)
            .Include(x => x.CpuSpecification)
            .Include(x => x.MotherboardSpecification).ThenInclude(x => x!.SupportedCpuGenerations)
            .Include(x => x.MotherboardSpecification).ThenInclude(x => x!.VideoOutputs)
            .Include(x => x.RamSpecification)
            .Include(x => x.GpuSpecification).ThenInclude(x => x!.PowerConnectors)
            .Include(x => x.StorageSpecification)
            .Include(x => x.CoolerSpecification).ThenInclude(x => x!.SupportedSockets)
            .Include(x => x.PsuSpecification).ThenInclude(x => x!.PowerConnectors)
            .Include(x => x.CaseSpecification).ThenInclude(x => x!.SupportedMotherboardFormFactors)
            .Include(x => x.CaseSpecification).ThenInclude(x => x!.SupportedPsuFormFactors)
            .Include(x => x.CaseSpecification).ThenInclude(x => x!.SupportedRadiators)
            .Include(x => x.MonitorSpecification)
            .Include(x => x.MouseSpecification)
            .Include(x => x.KeyboardSpecification)
            .AsSplitQuery()
            .OrderBy(x => x.Category.DisplayOrder)
            .ThenBy(x => x.CurrentRank > 0 ? x.CurrentRank : int.MaxValue)
            .ToArrayAsync(cancellationToken);

        return entities
            .Where(product => product.Category.Code != CatalogCodes.Cpu || IsBoxedCpu(product.Name))
            .Select(Map)
            .ToArray();
    }

    private static BuilderProduct Map(Product product)
    {
        var category = MapCategory(product.Category.Code);
        var offer = product.Offers
            .Where(x => x.IsCurrent && x.Currency == "CHF" && x.Availability != "unavailable"
                && x.TotalPrice > 0 && x.Retailer.IsEnabled)
            .OrderBy(x => x.TotalPrice)
            .ThenBy(x => x.Retailer.Priority)
            .First();
        var delivery = Delivery(offer);
        var (specs, compatibility, filters) = category switch
        {
            BuilderCategory.Cpu => MapCpu(product),
            BuilderCategory.Motherboard => MapMotherboard(product),
            BuilderCategory.Ram => MapRam(product),
            BuilderCategory.Gpu => MapGpu(product),
            BuilderCategory.Storage => MapStorage(product),
            BuilderCategory.Cooling => MapCooler(product),
            BuilderCategory.Psu => MapPsu(product),
            BuilderCategory.Case => MapCase(product),
            BuilderCategory.Monitor => MapMonitor(product),
            BuilderCategory.Mouse => MapMouse(product),
            BuilderCategory.Keyboard => MapKeyboard(product),
            _ => (Array.Empty<string>(), new ProductCompatibility(), Array.Empty<string>())
        };

        var isTopTen = product.CurrentRank is >= 1 and <= 10;
        return new BuilderProduct(
            product.Id,
            $"tp-{product.ToppreiseProductId}",
            product.Name,
            category,
            product.Manufacturer ?? string.Empty,
            product.ImageUrl ?? DefaultImage(category),
            new ProductOffer(
                offer.TotalPrice,
                offer.Currency,
                offer.Retailer.Name,
                delivery.Min,
                delivery.Max,
                offer.ObservedAt),
            specs,
            compatibility,
            isTopTen ? ["ConfiguratorPreview.TopProduct"] : [],
            isTopTen,
            filters,
            product.ManufacturerPartNumber,
            product.CurrentRank,
            CreateFilterValues(product, category),
            CreateSpecificationDetails(product, category),
            product.SourceUrl);
    }

    private static (IReadOnlyList<string> Specs, ProductCompatibility Compatibility, IReadOnlyList<string> Filters) MapCpu(Product product)
    {
        var x = product.CpuSpecification!;
        return (Compact(x.Cores is { } cores ? $"{cores} Cores" : null, x.Socket,
                x.BoostClockGhz is { } clock ? $"{Number(clock)} GHz" : null),
            new(Socket: x.Socket, CpuGeneration: x.Generation, PowerDrawWatts: x.TdpWatts,
                HasIntegratedGraphics: x.HasIntegratedGraphics),
            Compact(x.Generation, x.Cores >= 8 ? "productivity" : "gaming"));
    }

    private static (IReadOnlyList<string>, ProductCompatibility, IReadOnlyList<string>) MapMotherboard(Product product)
    {
        var x = product.MotherboardSpecification!;
        var storage = Compact(x.M2SlotCount > 0 ? "PCIe" : null, x.SataPortCount > 0 ? "SATA" : null);
        var videoOutputs = x.VideoOutputs
            .OrderBy(y => y.OutputType)
            .ThenBy(y => y.Version)
            .Select(y => new VideoOutput(y.OutputType, y.Version, y.Quantity))
            .ToArray();
        return (Compact(x.Socket, x.Chipset, x.FormFactor, x.MemoryType,
                videoOutputs.Length > 0 ? string.Join(" + ", videoOutputs.Select(y => y.DisplayName)) : null),
            new(Socket: x.Socket,
                SupportedCpuGenerations: x.SupportedCpuGenerations.Select(y => y.Generation).ToArray(),
                RamGeneration: x.MemoryType,
                RamSlotCount: x.MemorySlots,
                FormFactor: x.FormFactor,
                SupportedStorageInterfaces: storage,
                VideoOutputs: videoOutputs),
            Compact(x.Socket, x.Chipset, x.FormFactor, x.MemoryType));
    }

    private static (IReadOnlyList<string>, ProductCompatibility, IReadOnlyList<string>) MapRam(Product product)
    {
        var x = product.RamSpecification!;
        return (Compact($"{x.CapacityGb} GB", x.MemoryType,
                x.SpeedMtPerSecond is { } speed ? $"{speed} MT/s" : null,
                $"{x.ModuleCount} module(s)"),
            new(RamGeneration: x.MemoryType, RamModuleCount: x.ModuleCount),
            Compact(x.MemoryType, x.ModuleFormFactor, x.CapacityGb >= 32 ? "32gb+" : "16gb"));
    }

    private static (IReadOnlyList<string>, ProductCompatibility, IReadOnlyList<string>) MapGpu(Product product)
    {
        var x = product.GpuSpecification!;
        var recommendedPsuWatts = x.RecommendedPsuWatts ?? InferRecommendedPsuWatts(x.Chipset ?? product.Name);
        return (Compact(x.Chipset, x.MemoryGb is { } memory ? $"{memory} GB {x.MemoryType}" : null,
                $"{Number(x.LengthMm)} mm", $"PSU ≥ {recommendedPsuWatts} W"),
            new(PowerDrawWatts: x.TdpWatts,
                GpuLengthMm: Round(x.LengthMm), GpuHeightMm: Round(x.HeightMm), GpuSlotWidth: x.SlotWidth,
                RecommendedPsuWatts: recommendedPsuWatts,
                Pcie8Pin: ConnectorCount(x.PowerConnectors, "8PIN", "6+2PIN"),
                Pcie12Vhpwr: ConnectorCount(x.PowerConnectors, "12VHPWR", "16PIN"),
                Pcie12V2X6: ConnectorCount(x.PowerConnectors, "12V-2X6")),
            Compact(x.Chipset, x.MemoryGb >= 16 ? "16gb+" : "8gb+"));
    }

    private static (IReadOnlyList<string>, ProductCompatibility, IReadOnlyList<string>) MapStorage(Product product)
    {
        var x = product.StorageSpecification!;
        return (Compact($"{x.CapacityGb} GB", x.StorageType, x.Protocol, x.InterfaceType),
            new(StorageInterface: x.InterfaceType),
            Compact(x.StorageType, x.Protocol, x.CapacityGb >= 2000 ? "2tb+" : "1tb"));
    }

    private static (IReadOnlyList<string>, ProductCompatibility, IReadOnlyList<string>) MapCooler(Product product)
    {
        var x = product.CoolerSpecification!;
        return (Compact(x.CoolerType == "aio" ? $"AIO {x.RadiatorSizeMm} mm" : "Air cooler",
                x.HeightMm is { } height ? $"{Number(height)} mm" : null),
            new(CoolerHeightMm: Round(x.HeightMm), RadiatorSizeMm: x.RadiatorSizeMm,
                SupportedSockets: x.SupportedSockets.Select(y => y.Socket).ToArray()),
            Compact(x.CoolerType));
    }

    private static (IReadOnlyList<string>, ProductCompatibility, IReadOnlyList<string>) MapPsu(Product product)
    {
        var x = product.PsuSpecification!;
        return (Compact($"{x.Wattage} W", x.FormFactor, x.EfficiencyRating, x.AtxStandard),
            new(PsuWattage: x.Wattage, PsuFormFactor: x.FormFactor, PsuLengthMm: Round(x.LengthMm),
                Pcie8Pin: ConnectorCount(x.PowerConnectors, "8PIN", "6+2PIN"),
                Pcie12Vhpwr: ConnectorCount(x.PowerConnectors, "12VHPWR", "12V-2X6", "16PIN")),
            Compact(x.FormFactor, x.EfficiencyRating, x.Wattage >= 850 ? "850w+" : "650w+"));
    }

    private static (IReadOnlyList<string>, ProductCompatibility, IReadOnlyList<string>) MapCase(Product product)
    {
        var x = product.CaseSpecification!;
        var boardFormats = x.SupportedMotherboardFormFactors.Select(y => y.FormFactor).ToArray();
        var psuFormats = x.SupportedPsuFormFactors.Select(y => y.FormFactor).ToArray();
        var radiators = x.SupportedRadiators.Select(y => y.SizeMm).Distinct().ToArray();
        IReadOnlyList<int>? radiatorSupport = radiators.Length == 0 ? null : radiators;
        return (Compact(string.Join(", ", boardFormats), $"GPU {Number(x.MaximumGpuLengthMm)} mm",
                x.MaximumCpuCoolerHeightMm is { } height ? $"Cooler {Number(height)} mm" : null),
            new(MaxGpuLengthMm: Round(x.MaximumGpuLengthMm), MaxGpuHeightMm: Round(x.MaximumGpuHeightMm),
                MaxGpuSlotWidth: x.MaximumGpuSlotWidth, MaxPsuLengthMm: Round(x.MaximumPsuLengthMm),
                MaxCoolerHeightMm: Round(x.MaximumCpuCoolerHeightMm), RadiatorSupportMm: radiatorSupport,
                SupportedFormFactors: boardFormats, SupportedPsuFormFactors: psuFormats),
            boardFormats);
    }

    private static (IReadOnlyList<string>, ProductCompatibility, IReadOnlyList<string>) MapMonitor(Product product)
    {
        var x = product.MonitorSpecification!;
        return (Compact(x.ScreenSizeInches is { } size ? $"{Number(size)} inch" : null,
                x.ResolutionWidth is { } width && x.ResolutionHeight is { } height ? $"{width} × {height}" : null,
                x.RefreshRateHz is { } hz ? $"{hz} Hz" : null, x.PanelType), new(), Compact(x.PanelType));
    }

    private static (IReadOnlyList<string>, ProductCompatibility, IReadOnlyList<string>) MapMouse(Product product)
    {
        var x = product.MouseSpecification;
        return (Compact(x?.MaximumDpi is { } dpi ? $"{dpi} DPI" : null,
                x?.WeightGrams is { } weight ? $"{Number(weight)} g" : null, x?.Connectivity),
            new(), Compact(x?.Connectivity));
    }

    private static (IReadOnlyList<string>, ProductCompatibility, IReadOnlyList<string>) MapKeyboard(Product product)
    {
        var x = product.KeyboardSpecification;
        return (Compact(x?.Layout, x?.Size, x?.SwitchType, x?.Connectivity),
            new(), Compact(x?.Layout, x?.Size, x?.Connectivity));
    }

    private static IReadOnlyList<ProductFilterValue> CreateFilterValues(Product product, BuilderCategory category)
    {
        var values = new List<ProductFilterValue>();
        AddFilter(values, "manufacturer", "ProductSpecifications.Manufacturer", product.Manufacturer);

        switch (category)
        {
            case BuilderCategory.Cpu:
                var cpu = product.CpuSpecification!;
                AddFilter(values, "socket", "ProductSpecifications.Socket", cpu.Socket);
                AddFilter(values, "generation", "ProductSpecifications.Generation", cpu.Generation);
                AddFilter(values, "cores", "ProductSpecifications.CpuCores", cpu.Cores is { } cpuCores ? $"{cpuCores}" : null);
                AddFilter(values, "integrated-graphics", "ProductSpecifications.IntegratedGraphics", YesNo(cpu.HasIntegratedGraphics));
                break;
            case BuilderCategory.Gpu:
                var gpu = product.GpuSpecification!;
                AddFilter(values, "chipset", "ProductSpecifications.GraphicsChipset", gpu.Chipset);
                AddFilter(values, "memory", "ProductSpecifications.GraphicsMemory", gpu.MemoryGb is { } memory ? $"{memory} GB" : null);
                AddFilter(values, "memory-type", "ProductSpecifications.MemoryType", gpu.MemoryType);
                AddFilter(values, "recommended-psu", "ProductSpecifications.RecommendedPsu", $"{gpu.RecommendedPsuWatts ?? InferRecommendedPsuWatts(gpu.Chipset ?? product.Name)} W");
                break;
            case BuilderCategory.Motherboard:
                var board = product.MotherboardSpecification!;
                AddFilter(values, "socket", "ProductSpecifications.Socket", board.Socket);
                AddFilter(values, "chipset", "ProductSpecifications.Chipset", board.Chipset);
                AddFilter(values, "form-factor", "ProductSpecifications.FormFactor", board.FormFactor);
                AddFilter(values, "memory-type", "ProductSpecifications.MemoryType", board.MemoryType);
                foreach (var output in board.VideoOutputs.Select(x => x.OutputType).Distinct(StringComparer.OrdinalIgnoreCase))
                    AddFilter(values, "video-output", "ProductSpecifications.VideoOutputs", output);
                break;
            case BuilderCategory.Ram:
                var ram = product.RamSpecification!;
                AddFilter(values, "memory-type", "ProductSpecifications.MemoryType", ram.MemoryType);
                AddFilter(values, "capacity", "ProductSpecifications.Capacity", $"{ram.CapacityGb} GB");
                AddFilter(values, "modules", "ProductSpecifications.ModuleCount", $"{ram.ModuleCount}");
                AddFilter(values, "speed", "ProductSpecifications.MemorySpeed", ram.SpeedMtPerSecond is { } speed ? $"{speed} MT/s" : null);
                break;
            case BuilderCategory.Storage:
                var storage = product.StorageSpecification!;
                AddFilter(values, "storage-type", "ProductSpecifications.DriveType", storage.StorageType);
                AddFilter(values, "capacity", "ProductSpecifications.Capacity", Capacity(storage.CapacityGb));
                AddFilter(values, "interface", "ProductSpecifications.Interface", storage.InterfaceType);
                AddFilter(values, "protocol", "ProductSpecifications.Protocol", storage.Protocol);
                AddFilter(values, "form-factor", "ProductSpecifications.FormFactor", storage.FormFactor);
                break;
            case BuilderCategory.Cooling:
                var cooler = product.CoolerSpecification!;
                AddFilter(values, "cooler-type", "ProductSpecifications.CoolerType", cooler.CoolerType);
                AddFilter(values, "radiator", "ProductSpecifications.RadiatorSize", cooler.RadiatorSizeMm is { } radiator ? $"{radiator} mm" : null);
                foreach (var socket in cooler.SupportedSockets.Select(x => x.Socket))
                    AddFilter(values, "socket", "ProductSpecifications.SupportedSocket", socket);
                break;
            case BuilderCategory.Psu:
                var psu = product.PsuSpecification!;
                AddFilter(values, "wattage", "ProductSpecifications.Wattage", $"{psu.Wattage} W");
                AddFilter(values, "form-factor", "ProductSpecifications.FormFactor", psu.FormFactor);
                AddFilter(values, "efficiency", "ProductSpecifications.Efficiency", psu.EfficiencyRating);
                AddFilter(values, "atx-standard", "ProductSpecifications.AtxStandard", psu.AtxStandard);
                break;
            case BuilderCategory.Case:
                var pcCase = product.CaseSpecification!;
                foreach (var formFactor in pcCase.SupportedMotherboardFormFactors.Select(x => x.FormFactor))
                    AddFilter(values, "board-form-factor", "ProductSpecifications.MotherboardSupport", formFactor);
                foreach (var psuFormFactor in pcCase.SupportedPsuFormFactors.Select(x => x.FormFactor))
                    AddFilter(values, "psu-form-factor", "ProductSpecifications.PsuSupport", psuFormFactor);
                foreach (var supportedRadiator in pcCase.SupportedRadiators.Select(x => x.SizeMm).Distinct())
                    AddFilter(values, "radiator", "ProductSpecifications.RadiatorSupport", $"{supportedRadiator} mm");
                AddFilter(values, "gpu-clearance", "ProductSpecifications.GpuClearance", ClearanceBucket(pcCase.MaximumGpuLengthMm));
                break;
            case BuilderCategory.Monitor:
                var monitor = product.MonitorSpecification!;
                AddFilter(values, "screen-size", "ProductSpecifications.ScreenSize", monitor.ScreenSizeInches is { } size ? $"{Number(size)} inch" : null);
                AddFilter(values, "resolution", "ProductSpecifications.Resolution", Resolution(monitor.ResolutionWidth, monitor.ResolutionHeight));
                AddFilter(values, "refresh-rate", "ProductSpecifications.RefreshRate", monitor.RefreshRateHz is { } hz ? $"{hz} Hz" : null);
                AddFilter(values, "panel", "ProductSpecifications.PanelType", monitor.PanelType);
                break;
            case BuilderCategory.Mouse:
                var mouse = product.MouseSpecification;
                AddFilter(values, "connectivity", "ProductSpecifications.Connectivity", mouse?.Connectivity);
                AddFilter(values, "dpi", "ProductSpecifications.MaximumDpi", mouse?.MaximumDpi is { } dpi ? $"{dpi} DPI" : null);
                break;
            case BuilderCategory.Keyboard:
                var keyboard = product.KeyboardSpecification;
                AddFilter(values, "layout", "ProductSpecifications.Layout", keyboard?.Layout);
                AddFilter(values, "size", "ProductSpecifications.KeyboardSize", keyboard?.Size);
                AddFilter(values, "switch", "ProductSpecifications.SwitchType", keyboard?.SwitchType);
                AddFilter(values, "connectivity", "ProductSpecifications.Connectivity", keyboard?.Connectivity);
                break;
        }

        return values.Distinct().ToArray();
    }

    private static IReadOnlyList<ProductSpecificationDetail> CreateSpecificationDetails(Product product, BuilderCategory category)
    {
        var details = new List<ProductSpecificationDetail>();
        AddDetail(details, "ProductSpecifications.Manufacturer", product.Manufacturer);
        AddDetail(details, "ProductSpecifications.ManufacturerPartNumber", product.ManufacturerPartNumber);
        AddDetail(details, "ProductSpecifications.PopularityRank", product.CurrentRank > 0 ? $"#{product.CurrentRank}" : null);

        switch (category)
        {
            case BuilderCategory.Cpu:
                var cpu = product.CpuSpecification!;
                AddDetail(details, "ProductSpecifications.Socket", cpu.Socket);
                AddDetail(details, "ProductSpecifications.Generation", cpu.Generation);
                AddDetail(details, "ProductSpecifications.CoresThreads", cpu.Cores is { } cores ? $"{cores} / {cpu.Threads?.ToString() ?? "—"}" : null);
                AddDetail(details, "ProductSpecifications.BaseClock", Unit(cpu.BaseClockGhz, "GHz"));
                AddDetail(details, "ProductSpecifications.BoostClock", Unit(cpu.BoostClockGhz, "GHz"));
                AddDetail(details, "TDP", Unit(cpu.TdpWatts, "W"));
                AddDetail(details, "ProductSpecifications.IntegratedGraphics", YesNo(cpu.HasIntegratedGraphics));
                break;
            case BuilderCategory.Motherboard:
                var board = product.MotherboardSpecification!;
                AddDetail(details, "ProductSpecifications.Socket", board.Socket);
                AddDetail(details, "ProductSpecifications.Chipset", board.Chipset);
                AddDetail(details, "ProductSpecifications.FormFactor", board.FormFactor);
                AddDetail(details, "ProductSpecifications.MemoryType", board.MemoryType);
                AddDetail(details, "ProductSpecifications.MemorySlots", board.MemorySlots?.ToString(CultureInfo.InvariantCulture));
                AddDetail(details, "ProductSpecifications.MaximumMemory", Unit(board.MaximumMemoryGb, "GB"));
                AddDetail(details, "ProductSpecifications.M2Slots", board.M2SlotCount?.ToString(CultureInfo.InvariantCulture));
                AddDetail(details, "ProductSpecifications.SataPorts", board.SataPortCount?.ToString(CultureInfo.InvariantCulture));
                AddDetail(details, "ProductSpecifications.CpuGenerations", Join(board.SupportedCpuGenerations.Select(x => x.Generation)));
                AddDetail(details, "ProductSpecifications.EccSupport", YesNo(board.SupportsEcc));
                AddDetail(details, "ProductSpecifications.VideoOutputs", Join(board.VideoOutputs
                    .OrderBy(x => x.OutputType)
                    .ThenBy(x => x.Version)
                    .Select(x => new VideoOutput(x.OutputType, x.Version, x.Quantity).DisplayName)));
                break;
            case BuilderCategory.Ram:
                var ram = product.RamSpecification!;
                AddDetail(details, "ProductSpecifications.Capacity", $"{ram.CapacityGb} GB");
                AddDetail(details, "ProductSpecifications.MemoryType", ram.MemoryType);
                AddDetail(details, "ProductSpecifications.ModuleFormat", ram.ModuleFormFactor);
                AddDetail(details, "ProductSpecifications.Modules", ram.ModuleCount.ToString(CultureInfo.InvariantCulture));
                AddDetail(details, "ProductSpecifications.Speed", Unit(ram.SpeedMtPerSecond, "MT/s"));
                AddDetail(details, "ECC", YesNo(ram.IsEcc));
                AddDetail(details, "ProductSpecifications.Registered", YesNo(ram.IsRegistered));
                AddDetail(details, "ProductSpecifications.Height", Unit(ram.HeightMm, "mm"));
                break;
            case BuilderCategory.Gpu:
                var gpu = product.GpuSpecification!;
                AddDetail(details, "ProductSpecifications.GraphicsChipset", gpu.Chipset);
                AddDetail(details, "ProductSpecifications.GraphicsMemory", Unit(gpu.MemoryGb, "GB"));
                AddDetail(details, "ProductSpecifications.MemoryType", gpu.MemoryType);
                AddDetail(details, "TDP", Unit(gpu.TdpWatts, "W"));
                AddDetail(details, "ProductSpecifications.RecommendedPsu", $"{gpu.RecommendedPsuWatts ?? InferRecommendedPsuWatts(gpu.Chipset ?? product.Name)} W");
                AddDetail(details, "ProductSpecifications.Dimensions", Dimensions(gpu.LengthMm, gpu.HeightMm));
                AddDetail(details, "ProductSpecifications.SlotWidth", Unit(gpu.SlotWidth, "slots"));
                AddDetail(details, "ProductSpecifications.PowerConnectors", Connectors(gpu.PowerConnectors.Select(x => (x.ConnectorType, x.Quantity))));
                break;
            case BuilderCategory.Storage:
                var storage = product.StorageSpecification!;
                AddDetail(details, "ProductSpecifications.Capacity", Capacity(storage.CapacityGb));
                AddDetail(details, "ProductSpecifications.DriveType", storage.StorageType);
                AddDetail(details, "ProductSpecifications.Interface", storage.InterfaceType);
                AddDetail(details, "ProductSpecifications.Protocol", storage.Protocol);
                AddDetail(details, "ProductSpecifications.FormFactor", storage.FormFactor);
                AddDetail(details, "ProductSpecifications.M2Length", Unit(storage.M2LengthMm, "mm"));
                break;
            case BuilderCategory.Cooling:
                var cooler = product.CoolerSpecification!;
                AddDetail(details, "ProductSpecifications.CoolerType", cooler.CoolerType);
                AddDetail(details, "ProductSpecifications.Height", Unit(cooler.HeightMm, "mm"));
                AddDetail(details, "ProductSpecifications.RadiatorSize", Unit(cooler.RadiatorSizeMm, "mm"));
                AddDetail(details, "ProductSpecifications.CoolingCapacity", Unit(cooler.TdpCapacityWatts, "W"));
                AddDetail(details, "ProductSpecifications.SupportedSockets", Join(cooler.SupportedSockets.Select(x => x.Socket)));
                break;
            case BuilderCategory.Psu:
                var psu = product.PsuSpecification!;
                AddDetail(details, "ProductSpecifications.Wattage", $"{psu.Wattage} W");
                AddDetail(details, "ProductSpecifications.FormFactor", psu.FormFactor);
                AddDetail(details, "ProductSpecifications.Length", Unit(psu.LengthMm, "mm"));
                AddDetail(details, "ProductSpecifications.Efficiency", psu.EfficiencyRating);
                AddDetail(details, "ProductSpecifications.AtxStandard", psu.AtxStandard);
                AddDetail(details, "ProductSpecifications.PowerConnectors", Connectors(psu.PowerConnectors.Select(x => (x.ConnectorType, x.Quantity))));
                break;
            case BuilderCategory.Case:
                var pcCase = product.CaseSpecification!;
                AddDetail(details, "ProductSpecifications.MotherboardSupport", Join(pcCase.SupportedMotherboardFormFactors.Select(x => x.FormFactor)));
                AddDetail(details, "ProductSpecifications.PsuSupport", Join(pcCase.SupportedPsuFormFactors.Select(x => x.FormFactor)));
                AddDetail(details, "ProductSpecifications.MaximumGpuLength", Unit<decimal>(pcCase.MaximumGpuLengthMm, "mm"));
                AddDetail(details, "ProductSpecifications.MaximumGpuHeight", Unit(pcCase.MaximumGpuHeightMm, "mm"));
                AddDetail(details, "ProductSpecifications.MaximumGpuSlots", Unit(pcCase.MaximumGpuSlotWidth, "slots"));
                AddDetail(details, "ProductSpecifications.MaximumCpuCoolerHeight", Unit(pcCase.MaximumCpuCoolerHeightMm, "mm"));
                AddDetail(details, "ProductSpecifications.MaximumPsuLength", Unit(pcCase.MaximumPsuLengthMm, "mm"));
                AddDetail(details, "ProductSpecifications.RadiatorSupport", Join(pcCase.SupportedRadiators.Select(x => $"{x.SizeMm} mm ({x.Location})")));
                break;
            case BuilderCategory.Monitor:
                var monitor = product.MonitorSpecification!;
                AddDetail(details, "ProductSpecifications.ScreenSize", Unit(monitor.ScreenSizeInches, "inch"));
                AddDetail(details, "ProductSpecifications.Resolution", Resolution(monitor.ResolutionWidth, monitor.ResolutionHeight));
                AddDetail(details, "ProductSpecifications.RefreshRate", Unit(monitor.RefreshRateHz, "Hz"));
                AddDetail(details, "ProductSpecifications.PanelType", monitor.PanelType);
                break;
            case BuilderCategory.Mouse:
                var mouse = product.MouseSpecification;
                AddDetail(details, "ProductSpecifications.MaximumDpi", Unit(mouse?.MaximumDpi, "DPI"));
                AddDetail(details, "ProductSpecifications.Weight", Unit(mouse?.WeightGrams, "g"));
                AddDetail(details, "ProductSpecifications.Connectivity", mouse?.Connectivity);
                break;
            case BuilderCategory.Keyboard:
                var keyboard = product.KeyboardSpecification;
                AddDetail(details, "ProductSpecifications.Layout", keyboard?.Layout);
                AddDetail(details, "ProductSpecifications.KeyboardSize", keyboard?.Size);
                AddDetail(details, "ProductSpecifications.SwitchType", keyboard?.SwitchType);
                AddDetail(details, "ProductSpecifications.Connectivity", keyboard?.Connectivity);
                break;
        }

        return details;
    }

    private static void AddFilter(ICollection<ProductFilterValue> values, string key, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) values.Add(new(key, label, value));
    }

    private static void AddDetail(ICollection<ProductSpecificationDetail> details, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) details.Add(new(label, value));
    }

    private static string? YesNo(bool? value) => value switch
    {
        true => "ProductSpecifications.Yes",
        false => "ProductSpecifications.No",
        _ => null,
    };
    private static string? Unit<T>(T? value, string unit) where T : struct, IFormattable =>
        value is { } item ? $"{item.ToString(null, CultureInfo.InvariantCulture)} {unit}" : null;
    private static string Capacity(int capacityGb) => capacityGb >= 1000 && capacityGb % 1000 == 0 ? $"{capacityGb / 1000} TB" : $"{capacityGb} GB";
    private static string ClearanceBucket(decimal lengthMm) => $"≥ {(int)(Math.Floor(lengthMm / 50m) * 50m)} mm";
    private static string? Resolution(int? width, int? height) => width is { } w && height is { } h ? $"{w} × {h}" : null;
    private static string? Dimensions(decimal length, decimal? height) => height is { } h ? $"{Number(length)} × {Number(h)} mm" : $"{Number(length)} mm long";
    private static string? Join(IEnumerable<string> values)
    {
        var items = values.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return items.Length == 0 ? null : string.Join(", ", items);
    }
    private static string? Connectors(IEnumerable<(string Type, int Quantity)> connectors)
    {
        var items = connectors.Where(x => x.Quantity > 0).Select(x => $"{x.Quantity}× {x.Type}").ToArray();
        return items.Length == 0 ? null : string.Join(", ", items);
    }

    private static BuilderCategory MapCategory(string code) => code switch
    {
        CatalogCodes.Cpu => BuilderCategory.Cpu,
        CatalogCodes.Gpu => BuilderCategory.Gpu,
        CatalogCodes.Motherboard => BuilderCategory.Motherboard,
        CatalogCodes.Ram => BuilderCategory.Ram,
        CatalogCodes.Storage => BuilderCategory.Storage,
        CatalogCodes.Cooling => BuilderCategory.Cooling,
        CatalogCodes.Psu => BuilderCategory.Psu,
        CatalogCodes.Case => BuilderCategory.Case,
        CatalogCodes.Monitor => BuilderCategory.Monitor,
        CatalogCodes.Mouse => BuilderCategory.Mouse,
        CatalogCodes.Keyboard => BuilderCategory.Keyboard,
        _ => throw new InvalidOperationException($"Nieznany kod kategorii: {code}")
    };

    private static (int Min, int Max) Delivery(Offer offer) =>
        (offer.DeliveryMinBusinessDays, offer.DeliveryMaxBusinessDays) switch
        {
            ({ } min, { } max) when max >= min => (min, max),
            _ when offer.Availability == "in_stock" => (1, 3),
            _ when offer.Availability == "one_week" => (1, 5),
            _ when offer.Availability == "two_weeks" => (6, 10),
            _ when offer.Availability == "four_weeks" => (16, 20),
            _ => (1, 10)
        };

    private static int? ConnectorCount<T>(IEnumerable<T> connectors, params string[] types)
    {
        var values = connectors.Select(x => x switch
        {
            GpuPowerConnectorRequirement gpu => (gpu.ConnectorType, gpu.Quantity),
            PsuPowerConnector psu => (psu.ConnectorType, psu.Quantity),
            _ => (string.Empty, 0)
        });
        var count = values.Where(x => types.Contains(x.Item1, StringComparer.OrdinalIgnoreCase)).Sum(x => x.Item2);
        return count == 0 ? null : count;
    }

    private static IReadOnlyList<string> Compact(params string?[] values) =>
        values.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    private static string Number(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    private static int? Round(decimal? value) => value is null ? null : (int)Math.Ceiling(value.Value);
    public static int InferRecommendedPsuWatts(string gpuName)
    {
        var value = gpuName.ToUpperInvariant();
        if (value.Contains("RTX 5090")) return 1000;
        if (value.Contains("RTX 5080") || value.Contains("RTX 4090")) return 850;
        if (value.Contains("RTX 5070 TI") || value.Contains("RTX 4080")
            || value.Contains("RX 9070 XT") || value.Contains("RX 7900 XTX")
            || value.Contains("RX 7900 XT")) return 750;
        if (value.Contains("RTX 5070") || value.Contains("RTX 4070 TI")
            || value.Contains("RX 7800 XT") || value.Contains("RX 7700 XT")) return 700;
        if (value.Contains("RTX 5060 TI") || value.Contains("RTX 4070")
            || value.Contains("RX 9070") || value.Contains("RX 7900 GRE")) return 650;
        if (value.Contains("RTX 5060") || value.Contains("RTX 4060")
            || value.Contains("RX 7600") || value.Contains("INTEL ARC")) return 550;
        return 750;
    }

    internal static bool IsBoxedCpu(string productName) =>
        productName.Contains("Boxed", StringComparison.OrdinalIgnoreCase)
        && !productName.Contains("Tray", StringComparison.OrdinalIgnoreCase)
        && !productName.Contains("OEM", StringComparison.OrdinalIgnoreCase);

    private static string DefaultImage(BuilderCategory category) => $"/images/components/{category.ToString().ToLowerInvariant()}.svg";
}
