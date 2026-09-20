using EFDB;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Services.Configurator;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ToppreiseCatalog.Import;

internal sealed class PcPresetGenerator(
    AppDbContext db,
    IOptions<PcPresetOptions> options,
    ILogger<PcPresetGenerator> logger)
{
    private readonly PcPresetOptions _options = options.Value;

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        ValidateOptions();
        var products = await LoadAvailableProductsAsync(cancellationToken);
        var generated = Generate(products);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await db.PcPresetProducts.ExecuteDeleteAsync(cancellationToken);
            var existing = await db.PcPresets.ToArrayAsync(cancellationToken);
            foreach (var preset in existing)
            {
                preset.IsEnabled = false;
                preset.UpdatedAt = DateTimeOffset.UtcNow;
            }

            foreach (var definition in generated)
            {
                var preset = existing.FirstOrDefault(x => x.Slug == definition.Slug);
                if (preset is null)
                {
                    preset = new PcPreset { Slug = definition.Slug, CreatedAt = DateTimeOffset.UtcNow };
                    db.PcPresets.Add(preset);
                }
                preset.Name = definition.Name;
                var graphicsDe = definition.Gpu is null ? "integrierter Grafik" : definition.Gpu.Name;
                var graphicsEn = definition.Gpu is null ? "integrated graphics" : definition.Gpu.Name;
                var graphicsIt = definition.Gpu is null ? "grafica integrata" : definition.Gpu.Name;
                preset.DescriptionDe = $"Kompletter PC mit {definition.Cpu.Name}, {graphicsDe}, {definition.Ram.RamSpecification!.CapacityGb} GB RAM und {StorageTb(definition.Storage)} TB SSD.";
                preset.DescriptionEn = $"Complete PC with {definition.Cpu.Name}, {graphicsEn}, {definition.Ram.RamSpecification!.CapacityGb} GB RAM and {StorageTb(definition.Storage)} TB SSD.";
                preset.DescriptionIt = $"PC completo con {definition.Cpu.Name}, {graphicsIt}, {definition.Ram.RamSpecification!.CapacityGb} GB di RAM e SSD da {StorageTb(definition.Storage)} TB.";
                preset.BadgeDe = "KOMPATIBEL";
                preset.BadgeEn = "COMPATIBLE";
                preset.BadgeIt = "COMPATIBILE";
                preset.DisplayOrder = definition.DisplayOrder;
                preset.IsEnabled = true;
                preset.UpdatedAt = DateTimeOffset.UtcNow;
                preset.Products = definition.Products.Select((product, index) => new PcPresetProduct
                {
                    ProductId = product.Id,
                    Quantity = 1,
                    DisplayOrder = (index + 1) * 10
                }).ToArray();
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation("Utworzono {Count} kompatybilnych presetów PC z list w konfiguracji.", generated.Count);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private IReadOnlyList<GeneratedPreset> Generate(IReadOnlyList<Product> products)
    {
        var result = new List<GeneratedPreset>();
        var order = 0;
        foreach (var profile in _options.Profiles)
        foreach (var cpuModel in profile.CpuModels.Distinct(StringComparer.OrdinalIgnoreCase))
        foreach (var gpuModel in GraphicsModels(profile))
        foreach (var ramGb in profile.RamCapacitiesGb.Distinct())
        foreach (var storageGb in profile.StorageCapacitiesGb.Distinct())
        {
            var integrated = IsIntegrated(profile);
            var build = FindBuild(products, cpuModel, gpuModel, ramGb, storageGb, integrated);
            if (build is null)
            {
                logger.LogWarning("Pominięto preset {Profile}/{Cpu}/{Gpu}/{Ram} GB/{Storage} GB: brak dostępnego, kompatybilnego kompletu.", profile.Name, cpuModel, gpuModel ?? "iGPU", ramGb, storageGb);
                continue;
            }

            var graphicsName = gpuModel ?? "iGPU";
            result.Add(new GeneratedPreset(
                Slug($"{profile.Slug}-{cpuModel}-{graphicsName}-{ramGb}gb-{storageGb}gb"),
                $"{profile.Name} / {cpuModel} / {graphicsName} / {ramGb} GB / {StorageTb(build.Storage)} TB",
                order += 10,
                build));
        }
        return result;
    }

    private static Build? FindBuild(IReadOnlyList<Product> products, string cpuModel, string? gpuModel, int ramGb, int storageGb, bool integrated)
    {
        var cpus = ByPrice(products.Where(x => x.CpuSpecification is not null
            && IsBoxedCpu(x.Name) && ModelMatches(x.Name, cpuModel)
            && (!integrated || x.CpuSpecification.HasIntegratedGraphics == true)));
        Product?[] gpus = integrated
            ? [null]
            : ByPrice(products.Where(x => x.GpuSpecification is not null
                && ModelMatches(x.GpuSpecification.Chipset ?? x.Name, gpuModel!)));
        var rams = ByPrice(products.Where(x => x.RamSpecification?.CapacityGb == ramGb));
        var storage = ByPrice(products.Where(x => x.StorageSpecification is { } specification
            && specification.CapacityGb == storageGb && specification.InterfaceType.Equals("PCIE", StringComparison.OrdinalIgnoreCase)));
        var boards = ByPrice(products.Where(x => x.MotherboardSpecification is not null));
        var coolers = ByPrice(products.Where(x => x.CoolerSpecification is not null));
        var psus = ByPrice(products.Where(x => x.PsuSpecification is not null));
        var cases = ByPrice(products.Where(x => x.CaseSpecification is not null));

        foreach (var cpu in cpus)
        foreach (var gpu in gpus)
        foreach (var board in boards.Where(x => BoardFits(x, cpu, integrated)))
        foreach (var ram in rams.Where(x => RamFits(x, board)))
        foreach (var disk in storage.Where(x => StorageFits(x, board)))
        foreach (var cooler in coolers.Where(x => CoolerFits(x, cpu)))
        foreach (var psu in psus.Where(x => PsuFits(x, cpu, gpu)))
        foreach (var pcCase in cases.Where(x => CaseFits(x, board, gpu, cooler, psu)))
            return new Build(cpu, board, ram, gpu, disk, cooler, psu, pcCase);
        return null;
    }

    internal static bool ModelMatches(string value, string configuredModel)
    {
        var pattern = Regex.Escape(configuredModel.Trim()).Replace("\\ ", @"\s+");
        return Regex.IsMatch(value, $@"(?<![\p{{L}}\p{{N}}]){pattern}(?![\p{{L}}\p{{N}}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool IsBoxedCpu(string productName) =>
        productName.Contains("Boxed", StringComparison.OrdinalIgnoreCase)
        && !productName.Contains("Tray", StringComparison.OrdinalIgnoreCase)
        && !productName.Contains("OEM", StringComparison.OrdinalIgnoreCase);

    private async Task<Product[]> LoadAvailableProductsAsync(CancellationToken cancellationToken) => await db.Products
        .AsNoTracking()
        .Where(x => x.IsVisible && x.LifecycleStatus == "active" && x.SpecificationStatus == "valid")
        .Where(x => x.Offers.Any(o => o.IsCurrent && o.Currency == "CHF" && o.Availability != "unavailable" && o.TotalPrice > 0 && o.Retailer.IsEnabled))
        .Include(x => x.Category).Include(x => x.Offers).ThenInclude(x => x.Retailer)
        .Include(x => x.CpuSpecification)
        .Include(x => x.GpuSpecification).ThenInclude(x => x!.PowerConnectors)
        .Include(x => x.MotherboardSpecification).ThenInclude(x => x!.SupportedCpuGenerations)
        .Include(x => x.MotherboardSpecification).ThenInclude(x => x!.VideoOutputs)
        .Include(x => x.RamSpecification).Include(x => x.StorageSpecification)
        .Include(x => x.CoolerSpecification).ThenInclude(x => x!.SupportedSockets)
        .Include(x => x.PsuSpecification).ThenInclude(x => x!.PowerConnectors)
        .Include(x => x.CaseSpecification).ThenInclude(x => x!.SupportedMotherboardFormFactors)
        .Include(x => x.CaseSpecification).ThenInclude(x => x!.SupportedPsuFormFactors)
        .Include(x => x.CaseSpecification).ThenInclude(x => x!.SupportedRadiators)
        .AsSplitQuery().ToArrayAsync(cancellationToken);

    private void ValidateOptions()
    {
        if (_options.Profiles.Length == 0)
            throw new InvalidOperationException("PcPresets musi zawierać co najmniej jeden profil.");
        foreach (var profile in _options.Profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Name) || string.IsNullOrWhiteSpace(profile.Slug)
                || profile.CpuModels.Length == 0 || profile.RamCapacitiesGb.Length == 0 || profile.StorageCapacitiesGb.Length == 0
                || profile.RamCapacitiesGb.Any(x => x <= 0) || profile.StorageCapacitiesGb.Any(x => x <= 0)
                || (!IsIntegrated(profile) && !IsDiscrete(profile))
                || (IsDiscrete(profile) && profile.GpuModels.Length == 0))
                throw new InvalidOperationException($"Profil presetu '{profile.Name}' ma niepełną lub niepoprawną konfigurację.");
        }
    }

    private static IEnumerable<string?> GraphicsModels(PcPresetProfileOptions profile) => IsIntegrated(profile)
        ? new string?[] { null }
        : profile.GpuModels.Distinct(StringComparer.OrdinalIgnoreCase);
    private static bool IsIntegrated(PcPresetProfileOptions profile) => string.Equals(profile.GraphicsMode, "Integrated", StringComparison.OrdinalIgnoreCase);
    private static bool IsDiscrete(PcPresetProfileOptions profile) => string.Equals(profile.GraphicsMode, "Discrete", StringComparison.OrdinalIgnoreCase);

    private static Product[] ByPrice(IEnumerable<Product> products) => products.OrderBy(BestPrice).ThenBy(x => x.CurrentRank > 0 ? x.CurrentRank : int.MaxValue).ToArray();
    private static decimal BestPrice(Product product) => product.Offers.Where(x => x.IsCurrent && x.Currency == "CHF" && x.Availability != "unavailable" && x.TotalPrice > 0 && x.Retailer.IsEnabled).Min(x => x.TotalPrice);
    private static bool Equal(string? left, string? right) => left?.Equals(right, StringComparison.OrdinalIgnoreCase) == true;
    private static bool BoardFits(Product board, Product cpu, bool integrated) => Equal(board.MotherboardSpecification!.Socket, cpu.CpuSpecification!.Socket)
        && (board.MotherboardSpecification.SupportedCpuGenerations.Count == 0 || cpu.CpuSpecification.Generation is null
            || board.MotherboardSpecification.SupportedCpuGenerations.Any(x => Equal(x.Generation, cpu.CpuSpecification.Generation)))
        && (!integrated || board.MotherboardSpecification.VideoOutputs.Count > 0);
    private static bool RamFits(Product ram, Product board) => Equal(ram.RamSpecification!.MemoryType, board.MotherboardSpecification!.MemoryType)
        && (board.MotherboardSpecification.MemorySlots is null || ram.RamSpecification.ModuleCount <= board.MotherboardSpecification.MemorySlots)
        && (board.MotherboardSpecification.MaximumMemoryGb is null || ram.RamSpecification.CapacityGb <= board.MotherboardSpecification.MaximumMemoryGb);
    private static bool StorageFits(Product storage, Product board) => !Equal(storage.StorageSpecification!.InterfaceType, "PCIE") || board.MotherboardSpecification!.M2SlotCount > 0;
    private static bool CoolerFits(Product cooler, Product cpu) => cooler.CoolerSpecification!.SupportedSockets.Any(x => Equal(x.Socket, cpu.CpuSpecification!.Socket))
        && (cooler.CoolerSpecification.TdpCapacityWatts is null || cpu.CpuSpecification!.TdpWatts is null || cooler.CoolerSpecification.TdpCapacityWatts >= cpu.CpuSpecification.TdpWatts);
    private static bool PsuFits(Product psu, Product cpu, Product? gpu)
    {
        int? required = gpu is null
            ? null
            : gpu.GpuSpecification!.RecommendedPsuWatts
                ?? EfProductCatalog.InferRecommendedPsuWatts(gpu.GpuSpecification.Chipset ?? gpu.Name);
        if (cpu.CpuSpecification!.TdpWatts is { } cpuWatts)
        {
            var componentPower = gpu?.GpuSpecification!.TdpWatts is { } gpuWatts
                ? cpuWatts + gpuWatts + 150m
                : cpuWatts + 100m;
            required = Math.Max(required ?? 0, (int)(Math.Ceiling((componentPower * 1.25m) / 50m) * 50m));
        }
        return (required is null || psu.PsuSpecification!.Wattage >= required)
            && (gpu is null || ConnectorsFit(gpu, psu));
    }
    private static bool ConnectorsFit(Product gpu, Product psu) => RequiredConnectors(gpu, "8PIN", "6+2PIN") <= RequiredConnectors(psu, "8PIN", "6+2PIN")
        && RequiredConnectors(gpu, "12VHPWR", "12V-2X6", "16PIN") <= RequiredConnectors(psu, "12VHPWR", "12V-2X6", "16PIN");
    private static int RequiredConnectors(Product product, params string[] types) => product.GpuSpecification is { } gpu
        ? gpu.PowerConnectors.Where(x => types.Contains(x.ConnectorType, StringComparer.OrdinalIgnoreCase)).Sum(x => x.Quantity)
        : product.PsuSpecification!.PowerConnectors.Where(x => types.Contains(x.ConnectorType, StringComparer.OrdinalIgnoreCase)).Sum(x => x.Quantity);
    private static bool CaseFits(Product pcCase, Product board, Product? gpu, Product cooler, Product psu)
    {
        var specification = pcCase.CaseSpecification!;
        return (gpu is null || gpu.GpuSpecification!.LengthMm <= specification.MaximumGpuLengthMm)
            && (gpu?.GpuSpecification!.HeightMm is null || specification.MaximumGpuHeightMm is null || gpu.GpuSpecification.HeightMm <= specification.MaximumGpuHeightMm)
            && (gpu?.GpuSpecification!.SlotWidth is null || specification.MaximumGpuSlotWidth is null || gpu.GpuSpecification.SlotWidth <= specification.MaximumGpuSlotWidth)
            && (cooler.CoolerSpecification!.HeightMm is null || specification.MaximumCpuCoolerHeightMm is null || cooler.CoolerSpecification.HeightMm <= specification.MaximumCpuCoolerHeightMm)
            && (cooler.CoolerSpecification.RadiatorSizeMm is null || specification.SupportedRadiators.Count == 0 || specification.SupportedRadiators.Any(x => x.SizeMm == cooler.CoolerSpecification.RadiatorSizeMm))
            && (psu.PsuSpecification!.LengthMm is null || specification.MaximumPsuLengthMm is null || psu.PsuSpecification.LengthMm <= specification.MaximumPsuLengthMm)
            && (specification.SupportedMotherboardFormFactors.Count == 0 || specification.SupportedMotherboardFormFactors.Any(x => Equal(x.FormFactor, board.MotherboardSpecification!.FormFactor)))
            && (specification.SupportedPsuFormFactors.Count == 0 || specification.SupportedPsuFormFactors.Any(x => Equal(x.FormFactor, psu.PsuSpecification.FormFactor)));
    }
    private static string StorageTb(Product storage) => (storage.StorageSpecification!.CapacityGb / 1000m).ToString("0.##", CultureInfo.InvariantCulture);
    private static string Slug(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        return Regex.Replace(new string(normalized.Where(x => CharUnicodeInfo.GetUnicodeCategory(x) != UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
    }

    private sealed record Build(Product Cpu, Product Motherboard, Product Ram, Product? Gpu, Product Storage, Product Cooling, Product Psu, Product Case)
    {
        public Product[] Products => Gpu is null
            ? [Cpu, Motherboard, Ram, Storage, Cooling, Psu, Case]
            : [Cpu, Motherboard, Ram, Gpu!, Storage, Cooling, Psu, Case];
    }
    private sealed record GeneratedPreset(string Slug, string Name, int DisplayOrder, Build Build)
    {
        public Product Cpu => Build.Cpu;
        public Product? Gpu => Build.Gpu;
        public Product Ram => Build.Ram;
        public Product Storage => Build.Storage;
        public Product[] Products => Build.Products;
    }
}
