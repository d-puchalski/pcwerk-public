using Services.Models;
using System.Text.Json;

namespace Services.Configurator;

public sealed class PcBuilderService
{
    public const string OperatingSystemSelectionGroup = "operating-system";

    private static readonly BuilderCategory[] BaseRequiredCategories =
    [
        BuilderCategory.Cpu,
        BuilderCategory.Motherboard,
        BuilderCategory.Ram,
        BuilderCategory.Storage,
        BuilderCategory.Cooling,
        BuilderCategory.Psu,
        BuilderCategory.Case
    ];
    private static readonly BuilderCategory[] DiscreteRequiredCategories =
    [
        BuilderCategory.Cpu,
        BuilderCategory.Gpu,
        BuilderCategory.Motherboard,
        BuilderCategory.Ram,
        BuilderCategory.Storage,
        BuilderCategory.Cooling,
        BuilderCategory.Psu,
        BuilderCategory.Case
    ];
    private static readonly BuilderCategory[] AllCategories = Enum.GetValues<BuilderCategory>();
    private readonly EfProductCatalog _catalog;
    private IReadOnlyList<BuilderProduct> _products = [];

    public IReadOnlyList<BuilderProduct> Products => _products;
    public IReadOnlyList<BuildPreset> Presets { get; private set; } =
    [
        new("scratch", "ConfiguratorPreview.FromScratch", "ConfiguratorPreview.ConfigureEveryComponentYourself", 0, "+", new Dictionary<BuilderCategory, string>())
    ];
    public IReadOnlyList<ServicePackage> ServicePackages { get; } = CreateServicePackages();
    public IReadOnlyList<Service> Services { get; } = CreateServices();
    public IReadOnlyList<Service> OptionalServices => Services.Where(x => x.IsOptional).ToArray();
    public decimal CompatibilityCheckPrice { get; } = 49m;
    public decimal PcWerkBuildStartingPrice { get; } = 149m;

    public PcBuilderService(EfProductCatalog catalog)
    {
        _catalog = catalog;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _products = await _catalog.GetProductsAsync(cancellationToken);
        Presets = CreatePresets();
    }

    public BuilderBuild CreateInitialBuild() => new();

    public BuilderBuild ApplyPreset(string presetId)
    {
        var preset = Presets.First(x => x.Id == presetId);
        return BuildFromIds(preset.ProductIds.Values);
    }

    public BuilderBuild ApplyCatalogPreset(CatalogPcPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);

        var productIds = preset.Components
            .Select(x => x.ProductId)
            .ToHashSet();
        var products = _products
            .Where(x => productIds.Contains(x.DatabaseId))
            .OrderBy(x => x.Category)
            .ToArray();
        if (products.Length != productIds.Count)
        {
            throw new InvalidOperationException("The preset contains products that are not available in the configurator.");
        }

        var build = new BuilderBuild();
        foreach (var product in products)
        {
            Select(build, product);
        }
        if (build.Gpu is null && !UseIntegratedGraphics(build))
        {
            throw new InvalidOperationException("The preset has no graphics card and cannot use integrated graphics.");
        }

        var status = Evaluate(build);
        if (status.SelectedCount != status.RequiredCount || !status.IsCompatible)
        {
            throw new InvalidOperationException("The preset could not be loaded as a complete compatible configuration.");
        }

        return build;
    }

    public BuilderProduct? GetSelected(BuilderBuild build, BuilderCategory category) => category switch
    {
        BuilderCategory.Cpu => build.Cpu,
        BuilderCategory.Gpu => build.Gpu,
        BuilderCategory.Motherboard => build.Motherboard,
        BuilderCategory.Ram => build.Ram,
        BuilderCategory.Storage => build.Storage,
        BuilderCategory.Cooling => build.Cooling,
        BuilderCategory.Psu => build.Psu,
        BuilderCategory.Case => build.Case,
        BuilderCategory.Monitor => build.Monitor,
        BuilderCategory.Mouse => build.Mouse,
        BuilderCategory.Keyboard => build.Keyboard,
        _ => null
    };

    public void Select(BuilderBuild build, BuilderProduct product)
    {
        if (GetCompatibilityIssue(product, build) is not null)
            return;

        if (product.Category == BuilderCategory.Cpu
            && build.Motherboard?.Compatibility.Socket is { } boardSocket
            && product.Compatibility.Socket is { } cpuSocket
            && !boardSocket.Equals(cpuSocket, StringComparison.OrdinalIgnoreCase))
        {
            build.Motherboard = null;
            build.Ram = null;
            build.Cooling = null;
        }

        switch (product.Category)
        {
            case BuilderCategory.Cpu: build.Cpu = product; break;
            case BuilderCategory.Gpu:
                build.Gpu = product;
                build.GraphicsMode = BuildGraphicsMode.Discrete;
                break;
            case BuilderCategory.Motherboard: build.Motherboard = product; break;
            case BuilderCategory.Ram: build.Ram = product; break;
            case BuilderCategory.Storage: build.Storage = product; break;
            case BuilderCategory.Cooling: build.Cooling = product; break;
            case BuilderCategory.Psu: build.Psu = product; break;
            case BuilderCategory.Case: build.Case = product; break;
            case BuilderCategory.Monitor: build.Monitor = product; break;
            case BuilderCategory.Mouse: build.Mouse = product; break;
            case BuilderCategory.Keyboard: build.Keyboard = product; break;
        }
    }

    public bool CanUseIntegratedGraphics(BuilderBuild build) =>
        build.Cpu?.Compatibility.HasIntegratedGraphics == true
        && (build.Motherboard is null || build.Motherboard.Compatibility.VideoOutputs is { Count: > 0 });

    public bool UseIntegratedGraphics(BuilderBuild build)
    {
        if (!CanUseIntegratedGraphics(build)) return false;
        build.Gpu = null;
        build.GraphicsMode = BuildGraphicsMode.Integrated;
        return true;
    }

    public void UseDiscreteGraphics(BuilderBuild build) => build.GraphicsMode = BuildGraphicsMode.Discrete;

    public void RemoveOptionalProduct(BuilderBuild build, BuilderCategory category)
    {
        switch (category)
        {
            case BuilderCategory.Monitor: build.Monitor = null; break;
            case BuilderCategory.Mouse: build.Mouse = null; break;
            case BuilderCategory.Keyboard: build.Keyboard = null; break;
        }
    }

    public IReadOnlyList<BuilderProduct> GetCompatibleProducts(
        BuilderCategory category,
        BuilderBuild build,
        IReadOnlyDictionary<string, string>? filters = null,
        ProductSort sort = ProductSort.PriceAscending)
    {
        var products = _products
            .Where(x => x.Category == category)
            .Where(x => GetCompatibilityIssue(x, build) is null);

        if (filters is not null)
        {
            foreach (var filter in filters.Where(x => !string.IsNullOrWhiteSpace(x.Value)))
            {
                products = products.Where(product => product.FilterValues?.Any(value =>
                    value.Key.Equals(filter.Key, StringComparison.OrdinalIgnoreCase)
                    && value.Value.Equals(filter.Value, StringComparison.OrdinalIgnoreCase)) == true);
            }
        }

        return sort switch
        {
            ProductSort.PriceAscending => products.OrderBy(x => x.Price).ThenBy(x => x.Name).ToArray(),
            ProductSort.PriceDescending => products.OrderByDescending(x => x.Price).ThenBy(x => x.Name).ToArray(),
            ProductSort.TopRank => products.OrderBy(RankOrder).ThenBy(x => x.Price).ToArray(),
            ProductSort.Name => products.OrderBy(x => x.Name).ThenBy(x => x.Price).ToArray(),
            _ => products.OrderByDescending(x => x.Recommended).ThenBy(RankOrder).ThenBy(x => x.Price).ToArray()
        };
    }

    private static int RankOrder(BuilderProduct product) =>
        product.TopRank > 0 ? product.TopRank : int.MaxValue;

    public IReadOnlyList<ProductFilterDefinition> GetFilterDefinitions(BuilderCategory category, BuilderBuild build) =>
        _products
            .Where(x => x.Category == category)
            .Where(x => GetCompatibilityIssue(x, build) is null)
            .SelectMany(x => x.FilterValues ?? [])
            .GroupBy(x => new { x.Key, x.Label })
            .Select(group => new ProductFilterDefinition(
                group.Key.Key,
                group.Key.Label,
                group.Select(x => x.Value)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(NaturalFilterOrder)
                    .ThenBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToArray()))
            .Where(x => x.Values.Count > 1)
            .OrderBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static decimal NaturalFilterOrder(string value)
    {
        var number = new string(value.TakeWhile(x => char.IsDigit(x) || x is '.' or ',').ToArray())
            .Replace(',', '.');
        return decimal.TryParse(number, System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out var result)
            ? result
            : decimal.MaxValue;
    }

    public BuildStatus Evaluate(BuilderBuild build)
    {
        var required = RequiredCategories(build);
        var missing = required.Where(x => GetSelected(build, x) is null).ToArray();
        var selected = required.Select(x => GetSelected(build, x)).Where(x => x is not null).Cast<BuilderProduct>().ToArray();
        var warnings = selected.Select(x => GetCompatibilityIssue(x, build)).Where(x => x is not null).Cast<string>().Distinct().ToArray();
        return new(selected.Length, required.Length, warnings.Length == 0, missing, warnings, CalculatePricing(build).Total);
    }

    private static BuilderCategory[] RequiredCategories(BuilderBuild build) => build.GraphicsMode == BuildGraphicsMode.Integrated
        ? BaseRequiredCategories
        : DiscreteRequiredCategories;

    public BuildPricing CalculatePricing(BuilderBuild build, decimal deliveryPrice = 0)
    {
        var hardwareSubtotal = AllCategories
            .Select(x => GetSelected(build, x))
            .Where(x => x is not null)
            .Sum(x => x!.Price);
        var packagePrice = build.OrderType switch
        {
            BuildOrderType.ComponentsOnly => CompatibilityCheckPrice,
            BuildOrderType.PcWerkBuild => build.ServicePackage?.Price ?? 0,
            _ => 0
        };
        var addonsTotal = build.OrderType == BuildOrderType.PcWerkBuild
            ? build.ServiceAddons.Where(x => !IsIncludedInSelectedPackage(build, x.Id)).Sum(x => GetAddonPrice(build, x))
            : 0;
        return new(hardwareSubtotal, packagePrice, addonsTotal, deliveryPrice, hardwareSubtotal + packagePrice + addonsTotal + deliveryPrice);
    }

    public BuildDeliveryEstimate? CalculateDelivery(BuilderBuild build)
    {
        var selected = AllCategories
            .Select(x => GetSelected(build, x))
            .Where(x => x is not null)
            .Cast<BuilderProduct>()
            .ToArray();

        return selected.Length == 0
            ? null
            : new(
                selected.Max(x => x.Offer.DeliveryMinBusinessDays),
                selected.Max(x => x.Offer.DeliveryMaxBusinessDays));
    }

    public OrderSummary CreateOrderSummary(BuilderBuild build, decimal deliveryPrice = 0)
    {
        var pricing = CalculatePricing(build, deliveryPrice);
        return new(pricing.HardwareSubtotal, pricing.ServicePackagePrice + pricing.AddonsTotal, pricing.DeliveryPrice, pricing.Total);
    }

    public void SetOrderType(BuilderBuild build, BuildOrderType orderType)
    {
        build.OrderType = orderType;
        if (orderType == BuildOrderType.ComponentsOnly)
        {
            build.ServicePackage = null;
            build.ServiceAddons.Clear();
        }
    }

    public void SelectServicePackage(BuilderBuild build, string packageId)
    {
        build.OrderType = BuildOrderType.PcWerkBuild;
        build.ServicePackage = ServicePackages.First(x => x.Id == packageId);
        build.ServiceAddons.RemoveAll(x => IsIncludedInSelectedPackage(build, x.Id));
    }

    public bool IsIncludedInSelectedPackage(BuilderBuild build, string serviceId) =>
        build.ServicePackage?.IncludedServices.Contains(serviceId, StringComparer.OrdinalIgnoreCase) ?? false;

    public bool IsAddonSelected(BuilderBuild build, string serviceId) =>
        build.ServiceAddons.Any(x => x.Id.Equals(serviceId, StringComparison.OrdinalIgnoreCase));

    public void ClearServiceSelectionGroup(BuilderBuild build, string selectionGroup) =>
        build.ServiceAddons.RemoveAll(service =>
            service.SelectionGroup?.Equals(selectionGroup, StringComparison.OrdinalIgnoreCase) == true);

    public decimal GetAddonPrice(BuilderBuild build, Service service)
    {
        var discount = service.DiscountedByIncludedServiceId is { } includedServiceId
            && IsIncludedInSelectedPackage(build, includedServiceId)
                ? service.IncludedServiceDiscount
                : 0;
        return Math.Max(0, service.Price - discount);
    }

    public void ToggleServiceAddon(BuilderBuild build, string serviceId)
    {
        if (build.OrderType != BuildOrderType.PcWerkBuild || build.ServicePackage is null || IsIncludedInSelectedPackage(build, serviceId))
            return;

        var service = OptionalServices.First(x => x.Id == serviceId);
        var existing = build.ServiceAddons.FirstOrDefault(x => x.Id.Equals(serviceId, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            build.ServiceAddons.Remove(existing);
            return;
        }

        if (service.SelectionGroup is { } selectionGroup)
        {
            build.ServiceAddons.RemoveAll(x => x.SelectionGroup?.Equals(selectionGroup, StringComparison.OrdinalIgnoreCase) == true);
        }
        build.ServiceAddons.Add(service);
    }

    public string? GetCompatibilityIssue(BuilderProduct candidate, BuilderBuild build)
    {
        var cpu = candidate.Category == BuilderCategory.Cpu ? candidate : build.Cpu;
        var changesCpuPlatform = candidate.Category == BuilderCategory.Cpu
            && candidate.Compatibility.Socket is { } candidateSocket
            && build.Motherboard?.Compatibility.Socket is { } selectedBoardSocket
            && !candidateSocket.Equals(selectedBoardSocket, StringComparison.OrdinalIgnoreCase);
        var board = candidate.Category == BuilderCategory.Motherboard
            ? candidate
            : changesCpuPlatform ? null : build.Motherboard;
        var ram = candidate.Category == BuilderCategory.Ram ? candidate : build.Ram;
        var storage = candidate.Category == BuilderCategory.Storage ? candidate : build.Storage;
        var gpu = candidate.Category == BuilderCategory.Gpu ? candidate : build.Gpu;
        var psu = candidate.Category == BuilderCategory.Psu ? candidate : build.Psu;
        var pcCase = candidate.Category == BuilderCategory.Case ? candidate : build.Case;
        var cooler = candidate.Category == BuilderCategory.Cooling
            ? candidate
            : changesCpuPlatform ? null : build.Cooling;

        if (build.GraphicsMode == BuildGraphicsMode.Integrated
            && cpu is not null && cpu.Compatibility.HasIntegratedGraphics != true)
            return "Prozessor hat keine integrierte Grafik";
        if (build.GraphicsMode == BuildGraphicsMode.Integrated
            && board is not null && board.Compatibility.VideoOutputs is not { Count: > 0 })
            return "Mainboard hat keinen unterstützten Grafikausgang";

        if (cpu?.Compatibility.Socket is { } cpuSocket && board?.Compatibility.Socket is { } boardSocket
            && !cpuSocket.Equals(boardSocket, StringComparison.OrdinalIgnoreCase))
            return $"Benötigt {cpuSocket} Mainboard";
        if (cpu?.Compatibility.CpuGeneration is { } cpuGeneration
            && board?.Compatibility.SupportedCpuGenerations is { Count: > 0 } generations
            && !generations.Contains(cpuGeneration, StringComparer.OrdinalIgnoreCase))
            return $"Mainboard unterstützt {cpuGeneration} nicht";
        if (ram?.Compatibility.RamGeneration is { } ramType && board?.Compatibility.RamGeneration is { } boardRam && ramType != boardRam)
            return $"Mainboard unterstützt {boardRam}";
        if (ram?.Compatibility.RamModuleCount is { } modules && board?.Compatibility.RamSlotCount is { } slots && modules > slots)
            return $"RAM benötigt {modules} Steckplätze, Mainboard hat {slots}";
        if (gpu?.Compatibility.GpuLengthMm is { } gpuLength && pcCase?.Compatibility.MaxGpuLengthMm is { } clearance && gpuLength > clearance)
            return $"Grafikkarte ist {gpuLength - clearance} mm zu lang";
        if (gpu?.Compatibility.GpuHeightMm is { } gpuHeight && pcCase?.Compatibility.MaxGpuHeightMm is { } maxGpuHeight && gpuHeight > maxGpuHeight)
            return $"Grafikkarte ist {gpuHeight - maxGpuHeight} mm zu hoch";
        if (gpu?.Compatibility.GpuSlotWidth is { } gpuSlots && pcCase?.Compatibility.MaxGpuSlotWidth is { } maxGpuSlots && gpuSlots > maxGpuSlots)
            return $"Grafikkarte benötigt {gpuSlots:0.#} Slots, Gehäuse erlaubt {maxGpuSlots:0.#}";
        var recommended = RequiredPsuWattage(cpu, gpu);
        if (recommended is not null && psu?.Compatibility.PsuWattage is { } wattage && wattage < recommended)
            return $"Mindestens {recommended} W Netzteil erforderlich";
        if (board?.Compatibility.FormFactor is { } formFactor && pcCase?.Compatibility.SupportedFormFactors is { Count: > 0 } supported && !supported.Contains(formFactor, StringComparer.OrdinalIgnoreCase))
            return $"Gehäuse unterstützt kein {formFactor}";
        if (cooler?.Compatibility.RadiatorSizeMm is { } radiator
            && pcCase?.Compatibility.RadiatorSupportMm is { Count: > 0 } radiators
            && !radiators.Contains(radiator))
            return $"Gehäuse unterstützt keinen {radiator}-mm-Radiator";
        if (cooler?.Compatibility.SupportedSockets is { Count: > 0 } sockets && cpu?.Compatibility.Socket is { } socket && !sockets.Contains(socket, StringComparer.OrdinalIgnoreCase))
            return $"Kühler unterstützt {socket} nicht";
        if (cooler?.Compatibility.CoolerHeightMm is { } coolerHeight && pcCase?.Compatibility.MaxCoolerHeightMm is { } maxCoolerHeight && coolerHeight > maxCoolerHeight)
            return $"CPU-Kühler ist {coolerHeight - maxCoolerHeight} mm zu hoch";
        if (psu?.Compatibility.PsuLengthMm is { } psuLength && pcCase?.Compatibility.MaxPsuLengthMm is { } maxPsuLength && psuLength > maxPsuLength)
            return $"Netzteil ist {psuLength - maxPsuLength} mm zu lang";
        if (psu?.Compatibility.PsuFormFactor is { } psuFormFactor && pcCase?.Compatibility.SupportedPsuFormFactors is { Count: > 0 } psuFormats && !psuFormats.Contains(psuFormFactor, StringComparer.OrdinalIgnoreCase))
            return $"Gehäuse unterstützt kein {psuFormFactor}-Netzteil";
        if (storage?.Compatibility.StorageInterface is { } storageInterface && board?.Compatibility.SupportedStorageInterfaces is { Count: > 0 } storageInterfaces && !storageInterfaces.Contains(storageInterface, StringComparer.OrdinalIgnoreCase))
            return $"Mainboard unterstützt keinen {storageInterface}-Speicher";
        if (psu is not null && gpu?.Compatibility.Pcie8Pin is { } required8Pin
            && required8Pin > (psu?.Compatibility.Pcie8Pin ?? 0))
            return $"Netzteil hat zu wenige PCIe-8-Pin-Anschlüsse";
        var required12V = (gpu?.Compatibility.Pcie12Vhpwr ?? 0) + (gpu?.Compatibility.Pcie12V2X6 ?? 0);
        if (psu is not null && required12V > 0
            && required12V > (psu.Compatibility.Pcie12Vhpwr ?? 0))
            return $"Netzteil hat keinen passenden 12VHPWR/12V-2x6-Anschluss";
        return null;
    }

    private static int? RequiredPsuWattage(BuilderProduct? cpu, BuilderProduct? gpu)
    {
        var catalogueRecommendation = gpu?.Compatibility.RecommendedPsuWatts;
        int? calculatedRecommendation = null;
        if (cpu?.Compatibility.PowerDrawWatts is { } cpuWatts
            && gpu?.Compatibility.PowerDrawWatts is { } gpuWatts)
        {
            // 150 W covers the board, memory, storage and fans; 25% headroom
            // avoids running the PSU at its limit.
            var required = (cpuWatts + gpuWatts + 150m) * 1.25m;
            calculatedRecommendation = (int)(Math.Ceiling(required / 50m) * 50m);
        }

        return (catalogueRecommendation, calculatedRecommendation) switch
        {
            ({ } catalogue, { } calculated) => Math.Max(catalogue, calculated),
            ({ } catalogue, null) => catalogue,
            (null, { } calculated) => calculated,
            _ => null
        };
    }

    public string Serialize(BuilderBuild build)
    {
        var ids = AllCategories.ToDictionary(x => x.ToString(), x => GetSelected(build, x)?.Id);
        return JsonSerializer.Serialize(new SerializedBuild(
            ids,
            build.GraphicsMode,
            build.OrderType,
            build.ServicePackage?.Id,
            build.ServiceAddons.Select(x => x.Id).ToArray()));
    }

    public BuilderBuild Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return CreateInitialBuild();
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty(nameof(SerializedBuild.Components), out _))
            {
                var state = JsonSerializer.Deserialize<SerializedBuild>(json)!;
                var build = BuildFromIds(state.Components.Values.Where(x => x is not null).Cast<string>());
                if (state.GraphicsMode == BuildGraphicsMode.Integrated) UseIntegratedGraphics(build);
                if (state.OrderType is { } orderType) SetOrderType(build, orderType);
                if (state.ServicePackageId is not null && ServicePackages.Any(x => x.Id == state.ServicePackageId))
                    SelectServicePackage(build, state.ServicePackageId);
                foreach (var addonId in state.ServiceAddonIds ?? [])
                    if (OptionalServices.Any(x => x.Id == addonId)) ToggleServiceAddon(build, addonId);
                return build;
            }

            var legacyIds = JsonSerializer.Deserialize<Dictionary<string, string?>>(json)?.Values.Where(x => x is not null).Cast<string>() ?? [];
            return BuildFromIds(legacyIds);
        }
        catch (JsonException)
        {
            return CreateInitialBuild();
        }
    }

    private BuilderBuild BuildFromIds(IEnumerable<string> ids)
    {
        var build = new BuilderBuild();
        foreach (var id in ids)
        {
            var product = _products.FirstOrDefault(x => x.Id == id);
            if (product is not null) Select(build, product);
        }
        return build;
    }

    private IReadOnlyList<BuildPreset> CreatePresets()
        =>
        [
            new("scratch", "ConfiguratorPreview.FromScratch", "ConfiguratorPreview.ConfigureEveryComponentYourself", 0, "+", new Dictionary<BuilderCategory, string>())
        ];

    private sealed record SerializedBuild(
        IReadOnlyDictionary<string, string?> Components,
        BuildGraphicsMode? GraphicsMode,
        BuildOrderType? OrderType,
        string? ServicePackageId,
        IReadOnlyList<string> ServiceAddonIds);

    private static IReadOnlyList<ServicePackage> CreateServicePackages() =>
    [
        new("assembly", "ConfiguratorPreview.PcWerkAssembly", "ConfiguratorPreview.ProfessionalAssemblyAndASafeFirstBoot", 149m, false,
            ["professional-assembly", "cable-management", "connector-inspection", "first-boot"],
            "ConfiguratorPreview.ProfessionallyAssembledCleanlyWired"),
        new("complete", "ConfiguratorPreview.PcWerkComplete", "ConfiguratorPreview.TheCompletePcWerkBuildAssembledConfiguredAndThoroughlyTested", 249m, true,
            ["professional-assembly", "cable-management", "manual-verification", "bios-update", "memory-profile", "cpu-gpu-config", "temperature-check", "cpu-stress", "gpu-stress", "stability-check", "final-system-check"],
            "ConfiguratorPreview.FullyAssembledFullyConfiguredFullyTested"),
        new("complete-plus", "ConfiguratorPreview.PcWerkCompletePlus", "ConfiguratorPreview.EverythingInCompletePlusOsSetupFineTuningAndPrioritySupport", 329m, false,
            ["professional-assembly", "cable-management", "manual-verification", "bios-update", "memory-profile", "cpu-gpu-config", "temperature-check", "cpu-stress", "gpu-stress", "stability-check", "final-system-check", "windows-installation", "advanced-config", "software-setup", "configuration-docs", "priority-support"],
            "ConfiguratorPreview.CompletelySetUpIndividuallyOptimizedPersonallySupported")
    ];

    private static IReadOnlyList<Service> CreateServices() =>
    [
        ServiceItem("professional-assembly", "ConfiguratorPreview.ProfessionalAssembly", "ConfiguratorPreview.CarefulAssemblyOfAllComponents", "assembly", "complete", "complete-plus"),
        ServiceItem("cable-management", "ConfiguratorPreview.CableManagement", "ConfiguratorPreview.CleanAirflowOptimizedCableRouting", "assembly", "complete", "complete-plus"),
        ServiceItem("connector-inspection", "ConfiguratorPreview.ConnectorInspection", "ConfiguratorPreview.InspectionOfAllPowerAndDataConnections", "assembly"),
        ServiceItem("first-boot", "ConfiguratorPreview.FirstBootTest", "ConfiguratorPreview.ControlledFirstSystemBoot", "assembly"),
        ServiceItem("memory-profile", "ConfiguratorPreview.RamXmpExpoSetup", "ConfiguratorPreview.ConfigurationOfTheCorrectMemoryProfile", "complete", "complete-plus"),
        ServiceItem("cpu-gpu-config", "ConfiguratorPreview.CpuGpuConfiguration", "ConfiguratorPreview.SafeBaseConfigurationOfTheCoreComponents", "complete", "complete-plus"),
        ServiceItem("temperature-check", "ConfiguratorPreview.TemperatureCheck", "ConfiguratorPreview.TemperatureChecksUnderLoad", "complete", "complete-plus"),
        ServiceItem("cpu-stress", "ConfiguratorPreview.CpuStressTest", "ConfiguratorPreview.ProcessorLoadTest", "complete", "complete-plus"),
        ServiceItem("gpu-stress", "ConfiguratorPreview.GpuStressTest", "ConfiguratorPreview.GraphicsCardLoadTest", "complete", "complete-plus"),
        ServiceItem("stability-check", "ConfiguratorPreview.StabilityCheck", "ConfiguratorPreview.SystemStabilityVerification", "complete", "complete-plus"),
        ServiceItem("final-system-check", "ConfiguratorPreview.FinalSystemCheck", "ConfiguratorPreview.FinalFunctionalInspection", "complete", "complete-plus"),
        ServiceItem("advanced-config", "ConfiguratorPreview.AdvancedSystemConfiguration", "ConfiguratorPreview.AdvancedSystemSetup", "complete-plus"),
        ServiceItem("software-setup", "ConfiguratorPreview.BasicSoftwareSetup", "ConfiguratorPreview.SetupOfSelectedEssentialSoftware", "complete-plus"),
        ServiceItem("configuration-docs", "ConfiguratorPreview.FinalConfigurationDocumentation", "ConfiguratorPreview.DocumentationOfTheDeliveredSettings", "complete-plus"),
        ServiceItem("priority-support", "ConfiguratorPreview.PrioritySupportAfterDelivery", "ConfiguratorPreview.PriorityAssistanceAfterDelivery", "complete-plus"),

        ServiceItem("windows-installation", "ConfiguratorPreview.WindowsInstallation", "ConfiguratorPreview.InstallationAndBasicConfigurationTheWindowsLicenseIsNotIncluded", "complete-plus"),
        new("bios-update", "ConfiguratorPreview.BiosUpdate", "ConfiguratorPreview.CheckAndUpdateToASuitableStableBiosVersion", 29m, "ServiceCategory.Firmware", ["complete", "complete-plus"], true),
        new("extended-stress", "ConfiguratorPreview.ExtendedStressTest", "ConfiguratorPreview.ExtendedLoadAndStabilityTesting", 49m, "ServiceCategory.Testing", [], true),
        new("os-ubuntu", "ConfiguratorPreview.UbuntuLinux", "ConfiguratorPreview.UbuntuLinuxInstallationAndBasicConfigurationNoLicenseFee", 39m, "ServiceCategory.Setup", [], IsOptional: true, SelectionGroup: OperatingSystemSelectionGroup, DiscountedByIncludedServiceId: "windows-installation", IncludedServiceDiscount: 39m),
        new("os-windows-home", "ConfiguratorPreview.Windows11Home", "ConfiguratorPreview.Windows11InstallationConfigurationWithoutActivation", 39m, "ServiceCategory.Setup", [], IsOptional: true, SelectionGroup: OperatingSystemSelectionGroup, DiscountedByIncludedServiceId: "windows-installation", IncludedServiceDiscount: 39m),
        new("os-windows-home-activated", "ConfiguratorPreview.Windows11HomeActivated", "ConfiguratorPreview.Windows11HomeInstallationConfigurationAndOfficialLicense", 139m, "ServiceCategory.Setup", [], IsOptional: true, SelectionGroup: OperatingSystemSelectionGroup, DiscountedByIncludedServiceId: "windows-installation", IncludedServiceDiscount: 39m),
        new("os-windows-pro", "ConfiguratorPreview.Windows11Pro", "ConfiguratorPreview.Windows11InstallationConfigurationWithoutActivation", 39m, "ServiceCategory.Setup", [], IsOptional: true, SelectionGroup: OperatingSystemSelectionGroup, DiscountedByIncludedServiceId: "windows-installation", IncludedServiceDiscount: 39m),
        new("os-windows-pro-activated", "ConfiguratorPreview.Windows11ProActivated", "ConfiguratorPreview.Windows11ProInstallationConfigurationAndOfficialLicense", 159m, "ServiceCategory.Setup", [], IsOptional: true, SelectionGroup: OperatingSystemSelectionGroup, DiscountedByIncludedServiceId: "windows-installation", IncludedServiceDiscount: 39m),
        new("data-transfer", "ConfiguratorPreview.DataTransfer", "ConfiguratorPreview.TransferOfYourDataFromYourExistingPc", 79m, "ServiceCategory.Data", [], true, true),
        new("home-setup-zurich", "ConfiguratorPreview.HomeSetupZurich", "ConfiguratorPreview.DeliveryConnectionAndSetupAtYourHome", 99m, "ServiceCategory.OnSite", [], true, true, "ConfiguratorPreview.AvailableOnlyForSupportedPostalCodes"),
        ServiceItem("manual-verification", "ConfiguratorPreview.ManualPcWerkFinalVerification", "ConfiguratorPreview.ManualVerificationOfTheFinalHardwareSelectionBeforeAssembly", "complete", "complete-plus")
    ];

    private static Service ServiceItem(string id, string nameTranslationKey, string descriptionTranslationKey, params string[] includedInPackages) =>
        new(id, nameTranslationKey, descriptionTranslationKey, 0, "ServiceCategory.Package", includedInPackages);
}
