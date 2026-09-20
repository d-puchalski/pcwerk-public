using Services.Configurator;
using Services.Models;
using Xunit;

namespace Services.Tests;

public sealed class ConfiguratorCompatibilityTests
{
    [Theory]
    [InlineData("Nvidia GeForce RTX 5090", 1000)]
    [InlineData("Nvidia GeForce RTX 5080", 850)]
    [InlineData("Nvidia GeForce RTX 5070 Ti", 750)]
    [InlineData("AMD Radeon RX 9070 XT", 750)]
    [InlineData("Intel Arc B580", 550)]
    [InlineData("Unknown discrete GPU", 750)]
    public void InferRecommendedPsuWatts_UsesConservativeGpuModelFallback(string gpuName, int expected)
    {
        Assert.Equal(expected, EfProductCatalog.InferRecommendedPsuWatts(gpuName));
    }

    [Theory]
    [InlineData("AMD Ryzen 7 9800X3D, Boxed without Heatsink", true)]
    [InlineData("AMD Ryzen 7 5800XT, Boxed with Wraith Prism Cooler", true)]
    [InlineData("AMD Ryzen 7 7800X3D, Tray", false)]
    [InlineData("Intel Core i9 OEM", false)]
    public void IsBoxedCpu_AllowsOnlyRetailBoxProducts(string productName, bool expected)
    {
        Assert.Equal(expected, EfProductCatalog.IsBoxedCpu(productName));
    }

    [Fact]
    public void GpuSelection_DoesNotRequirePsuBeforePsuStep()
    {
        var service = new PcBuilderService(null!);
        var gpu = Product(
            BuilderCategory.Gpu,
            new(RecommendedPsuWatts: 850, Pcie8Pin: 3));

        Assert.Null(service.GetCompatibilityIssue(gpu, new BuilderBuild()));
    }

    [Fact]
    public void IntegratedGraphics_CompletesBuildWithoutDedicatedGpu()
    {
        var service = new PcBuilderService(null!);
        var build = CompleteBaseBuild(
            new(HasIntegratedGraphics: true),
            new(VideoOutputs: [new("HDMI", "2.1", 1)]));

        Assert.True(service.UseIntegratedGraphics(build));

        var status = service.Evaluate(build);
        Assert.Equal(7, status.RequiredCount);
        Assert.Equal(7, status.SelectedCount);
        Assert.Empty(status.MissingComponents);
        Assert.True(status.IsCompatible);
    }

    [Fact]
    public void IntegratedGraphics_RequiresCpuGraphicsAndMotherboardOutput()
    {
        var service = new PcBuilderService(null!);
        var cpuWithoutGraphics = CompleteBaseBuild(new(HasIntegratedGraphics: false), new(VideoOutputs: [new("HDMI", "2.1", 1)]));
        var boardWithoutOutput = CompleteBaseBuild(new(HasIntegratedGraphics: true), new(VideoOutputs: []));

        Assert.False(service.UseIntegratedGraphics(cpuWithoutGraphics));
        Assert.False(service.UseIntegratedGraphics(boardWithoutOutput));
    }

    [Fact]
    public void DiscreteGraphicsMode_StillRequiresGraphicsCard()
    {
        var service = new PcBuilderService(null!);
        var build = CompleteBaseBuild(new(HasIntegratedGraphics: true), new(VideoOutputs: [new("HDMI", "2.1", 1)]));

        var status = service.Evaluate(build);

        Assert.Equal(8, status.RequiredCount);
        Assert.Contains(BuilderCategory.Gpu, status.MissingComponents);
    }

    [Fact]
    public void PsuSelection_RejectsInsufficientPower()
    {
        var service = new PcBuilderService(null!);
        var build = new BuilderBuild
        {
            Gpu = Product(BuilderCategory.Gpu, new(RecommendedPsuWatts: 850, Pcie8Pin: 3))
        };
        var psu = Product(BuilderCategory.Psu, new(PsuWattage: 750, Pcie8Pin: 3));

        Assert.Contains("850 W", service.GetCompatibilityIssue(psu, build));
    }

    [Fact]
    public void PsuSelection_RejectsMissingGpuConnectors()
    {
        var service = new PcBuilderService(null!);
        var build = new BuilderBuild
        {
            Gpu = Product(BuilderCategory.Gpu, new(RecommendedPsuWatts: 850, Pcie8Pin: 3))
        };
        var psu = Product(BuilderCategory.Psu, new(PsuWattage: 850, Pcie8Pin: 2));

        Assert.Contains("PCIe-8-Pin", service.GetCompatibilityIssue(psu, build));
    }

    [Fact]
    public void PsuSelection_AcceptsSufficientPowerAndConnectors()
    {
        var service = new PcBuilderService(null!);
        var build = new BuilderBuild
        {
            Gpu = Product(BuilderCategory.Gpu, new(RecommendedPsuWatts: 850, Pcie8Pin: 3))
        };
        var psu = Product(BuilderCategory.Psu, new(PsuWattage: 850, Pcie8Pin: 3));

        Assert.Null(service.GetCompatibilityIssue(psu, build));
    }

    [Fact]
    public void CaseSelection_DoesNotRejectRadiatorWhenCaseSupportDataIsUnknown()
    {
        var service = new PcBuilderService(null!);
        var build = new BuilderBuild
        {
            Cooling = Product(BuilderCategory.Cooling, new(RadiatorSizeMm: 240))
        };
        var pcCase = Product(BuilderCategory.Case, new(RadiatorSupportMm: null));

        Assert.Null(service.GetCompatibilityIssue(pcCase, build));
    }

    [Fact]
    public void CaseSelection_RejectsRadiatorWhenKnownSupportDoesNotContainItsSize()
    {
        var service = new PcBuilderService(null!);
        var build = new BuilderBuild
        {
            Cooling = Product(BuilderCategory.Cooling, new(RadiatorSizeMm: 360))
        };
        var pcCase = Product(BuilderCategory.Case, new(RadiatorSupportMm: [120, 240, 280]));

        Assert.Contains("360-mm-Radiator", service.GetCompatibilityIssue(pcCase, build));
    }

    [Fact]
    public void CpuSelection_CanSwitchFromAm4ToAm5AndClearsPlatformDependentParts()
    {
        var service = new PcBuilderService(null!);
        var build = new BuilderBuild
        {
            Cpu = Product(BuilderCategory.Cpu, new(Socket: "AM4")),
            Motherboard = Product(BuilderCategory.Motherboard, new(Socket: "AM4", RamGeneration: "DDR4")),
            Ram = Product(BuilderCategory.Ram, new(RamGeneration: "DDR4")),
            Cooling = Product(BuilderCategory.Cooling, new(SupportedSockets: ["AM4"]))
        };
        var am5Cpu = Product(BuilderCategory.Cpu, new(Socket: "AM5"));

        service.Select(build, am5Cpu);

        Assert.Same(am5Cpu, build.Cpu);
        Assert.Null(build.Motherboard);
        Assert.Null(build.Ram);
        Assert.Null(build.Cooling);
    }

    [Fact]
    public void CpuSelection_OnSamePlatformKeepsCompatibleParts()
    {
        var service = new PcBuilderService(null!);
        var board = Product(BuilderCategory.Motherboard, new(Socket: "AM4", RamGeneration: "DDR4"));
        var ram = Product(BuilderCategory.Ram, new(RamGeneration: "DDR4"));
        var cooler = Product(BuilderCategory.Cooling, new(SupportedSockets: ["AM4"]));
        var build = new BuilderBuild { Motherboard = board, Ram = ram, Cooling = cooler };

        service.Select(build, Product(BuilderCategory.Cpu, new(Socket: "AM4")));

        Assert.Same(board, build.Motherboard);
        Assert.Same(ram, build.Ram);
        Assert.Same(cooler, build.Cooling);
    }

    [Fact]
    public void ComponentsOnlyPricing_IncludesManualCompatibilityCheck()
    {
        var service = new PcBuilderService(null!);
        var build = new BuilderBuild { OrderType = BuildOrderType.ComponentsOnly };

        var pricing = service.CalculatePricing(build);

        Assert.Equal(49m, pricing.ServicePackagePrice);
        Assert.Equal(49m, pricing.Total);
    }

    [Theory]
    [InlineData("os-ubuntu", 39)]
    [InlineData("os-windows-home", 39)]
    [InlineData("os-windows-home-activated", 139)]
    [InlineData("os-windows-pro", 39)]
    [InlineData("os-windows-pro-activated", 159)]
    public void OperatingSystemPricing_OutsideCompletePlusIncludesInstallation(string serviceId, decimal expected)
    {
        var service = new PcBuilderService(null!);
        var build = new BuilderBuild();
        service.SetOrderType(build, BuildOrderType.PcWerkBuild);
        service.SelectServicePackage(build, "complete");

        service.ToggleServiceAddon(build, serviceId);

        Assert.Equal(expected, service.CalculatePricing(build).AddonsTotal);
    }

    [Theory]
    [InlineData("os-ubuntu", 0)]
    [InlineData("os-windows-home", 0)]
    [InlineData("os-windows-home-activated", 100)]
    [InlineData("os-windows-pro", 0)]
    [InlineData("os-windows-pro-activated", 120)]
    public void OperatingSystemPricing_InCompletePlusChargesOnlyLicense(string serviceId, decimal expected)
    {
        var service = new PcBuilderService(null!);
        var build = new BuilderBuild();
        service.SetOrderType(build, BuildOrderType.PcWerkBuild);
        service.SelectServicePackage(build, "complete-plus");

        service.ToggleServiceAddon(build, serviceId);

        Assert.Equal(expected, service.CalculatePricing(build).AddonsTotal);
    }

    [Fact]
    public void OperatingSystemSelection_AllowsOnlyOneChoice()
    {
        var service = new PcBuilderService(null!);
        var build = new BuilderBuild();
        service.SetOrderType(build, BuildOrderType.PcWerkBuild);
        service.SelectServicePackage(build, "complete");

        service.ToggleServiceAddon(build, "os-ubuntu");
        service.ToggleServiceAddon(build, "os-windows-pro-activated");

        var selection = Assert.Single(build.ServiceAddons);
        Assert.Equal("os-windows-pro-activated", selection.Id);
    }

    [Fact]
    public void OperatingSystemSelection_CanBeCleared()
    {
        var service = new PcBuilderService(null!);
        var build = new BuilderBuild();
        service.SetOrderType(build, BuildOrderType.PcWerkBuild);
        service.SelectServicePackage(build, "complete");
        service.ToggleServiceAddon(build, "os-windows-home-activated");

        service.ClearServiceSelectionGroup(build, PcBuilderService.OperatingSystemSelectionGroup);

        Assert.Empty(build.ServiceAddons);
        Assert.Equal(0m, service.CalculatePricing(build).AddonsTotal);
    }

    private static BuilderProduct Product(BuilderCategory category, ProductCompatibility compatibility) => new(
        1,
        $"test-{category}",
        category.ToString(),
        category,
        "Test",
        "/test.webp",
        new(1, "CHF", "Test retailer", 1, 3, DateTimeOffset.UtcNow),
        [],
        compatibility,
        []);

    private static BuilderBuild CompleteBaseBuild(ProductCompatibility cpu, ProductCompatibility motherboard) => new()
    {
        Cpu = Product(BuilderCategory.Cpu, cpu),
        Motherboard = Product(BuilderCategory.Motherboard, motherboard),
        Ram = Product(BuilderCategory.Ram, new()),
        Storage = Product(BuilderCategory.Storage, new()),
        Cooling = Product(BuilderCategory.Cooling, new()),
        Psu = Product(BuilderCategory.Psu, new()),
        Case = Product(BuilderCategory.Case, new())
    };
}
