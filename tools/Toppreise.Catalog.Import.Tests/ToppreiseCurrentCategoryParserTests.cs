using ToppreiseCatalog.Import;
using Xunit;

namespace Toppreise.Catalog.Import.Tests;

public sealed class ToppreiseCurrentCategoryParserTests
{
    [Fact]
    public void Cpu_MapsCurrentLabels()
    {
        var specs = Parse("CPU", null,
            "INTEL Core i9-14900K \"Raptor Lake-S\", 24x 3.2GHz (6.0GHz), Socket 1700",
            "socket\nIntel Socket 1700\nCPU cores\n24 x\nnumber of threads\n32\nmax. TDP\n125 W\nintegrated GPU\nIntel UHD Graphics 770");

        Assert.Equal("1700", specs.Socket);
        Assert.Equal(24, specs.Cores);
        Assert.Equal(32, specs.Threads);
        Assert.Equal(125, specs.TdpWatts);
        Assert.True(specs.HasIntegratedGraphics);
        Assert.Null(CatalogImportService.ValidateSpecifications("CPU", specs));
    }

    [Fact]
    public void Motherboard_MapsAllVideoOutputsFromGraphicsBlock()
    {
        var specs = Parse("MOTHERBOARD", null,
            "ASUS TUF GAMING Z890-PRO WIFI, Intel Z890",
            "chipset\nIntel Z890\nsocket\nIntel socket 1851\ntype\nDDR5\nformat\nATX\ngraphic\nonboard\ngraphics memory\noutputs\nDisplayPort 1.4\n2 x HDMI 2.1\nexpansion slots\nPCIe x16\n2 x");

        Assert.Collection(specs.VideoOutputs.OrderBy(x => x.OutputType),
            output =>
            {
                Assert.Equal("DisplayPort", output.OutputType);
                Assert.Equal("1.4", output.Version);
                Assert.Equal(1, output.Quantity);
            },
            output =>
            {
                Assert.Equal("HDMI", output.OutputType);
                Assert.Equal("2.1", output.Version);
                Assert.Equal(2, output.Quantity);
            });
    }

    [Fact]
    public void Cpu_DoesNotTreatNotSupportedAsIntegratedGraphics()
    {
        var specs = Parse("CPU", null, "Example CPU, Socket AM5",
            "socket\nSocket AM5\nintegrated GPU\nnot supported");

        Assert.False(specs.HasIntegratedGraphics);
    }

    [Fact]
    public void Ram_MapsCurrentLabels()
    {
        var specs = Parse("RAM", null, "PATRIOT Viper Venom Kit, DDR5-6000, 32 GB",
            "number of modules\n2\nmodule size\n16 GB\ntype\nDDR5-DIMM\ntotal size\n32 GB\nfrequency\nPC5-48000U (6000MHz)\nmodule height\n43 mm");

        Assert.Equal("DDR5", specs.MemoryType);
        Assert.Equal("DIMM", specs.ModuleFormFactor);
        Assert.Equal(32, specs.CapacityGb);
        Assert.Equal(2, specs.ModuleCount);
        Assert.Equal(6000, specs.SpeedMtPerSecond);
        Assert.Equal(43m, specs.HeightMm);
        Assert.Null(CatalogImportService.ValidateSpecifications("RAM", specs));
    }

    [Fact]
    public void Gpu_MapsCurrentLabelsAndPowerConnector()
    {
        var specs = Parse("GPU", null, "GAINWARD GeForce RTX 5080, 16 GB GDDR7",
            "chipset\nNvidia GeForce RTX 5080\nmemory size\n16 GB\nmemory type\nGDDR7\nrequired slots\n3 x\npower connection\n1x 16-Pin\ncard length\n310 mm");

        Assert.Equal("Nvidia GeForce RTX 5080", specs.GpuChipset);
        Assert.Equal(16, specs.GpuMemoryGb);
        Assert.Equal("GDDR7", specs.GpuMemoryType);
        Assert.Equal(3m, specs.SlotWidth);
        Assert.Equal(1, specs.PowerConnectors["16PIN"]);
        Assert.Equal(310m, specs.LengthMm);
        Assert.Null(CatalogImportService.ValidateSpecifications("GPU", specs));
    }

    [Theory]
    [InlineData("air", "THERMALRIGHT Peerless Assassin 120", null, 164)]
    [InlineData("aio", "ASUS ROG Ryuo IV 360 ARGB", 360, null)]
    public void Cooling_MapsCurrentSocketListAndCriticalDimension(
        string subtype,
        string name,
        int? expectedRadiator,
        int? expectedHeight)
    {
        var specs = Parse("COOLING", subtype, name,
            "CPU socket\n115X, 1200, 1700, 1851, AM4, AM5\nheight\n164 mm");

        Assert.Contains("1150", specs.SupportedSockets);
        Assert.Contains("1151", specs.SupportedSockets);
        Assert.Contains("1200", specs.SupportedSockets);
        Assert.Contains("1700", specs.SupportedSockets);
        Assert.Contains("1851", specs.SupportedSockets);
        Assert.Contains("AM5", specs.SupportedSockets);
        Assert.Equal(expectedRadiator, specs.RadiatorSizeMm);
        Assert.Equal(expectedHeight, specs.HeightMm);
        Assert.Null(CatalogImportService.ValidateSpecifications("COOLING", specs));
    }

    [Fact]
    public void Psu_MapsCurrentLabelsAndConnectors()
    {
        var specs = Parse("PSU", null, "ASROCK Taichi TC-1300T, 1300 Watts",
            "power\n1300 W\nform factor\nATX\nspecification\nATX 3.1\ndepth\n18 cm\nPCI-Express\n2x 16 pin 8x 6+2 pin\nmotherboard\n20+4 pin 2x 4+4 pin\nefficiency\n80 PLUS Titanium");

        Assert.Equal(1300, specs.Wattage);
        Assert.Equal("ATX", specs.FormFactor);
        Assert.Equal(180m, specs.PsuLengthMm);
        Assert.Equal("80 PLUS Titanium", specs.EfficiencyRating);
        Assert.Equal("ATX 3.1", specs.AtxStandard);
        Assert.Equal(2, specs.PowerConnectors["16PIN"]);
        Assert.Equal(8, specs.PowerConnectors["6+2PIN"]);
        Assert.Equal(1, specs.PowerConnectors["20+4PIN"]);
        Assert.Equal(2, specs.PowerConnectors["4+4PIN"]);
        Assert.Null(CatalogImportService.ValidateSpecifications("PSU", specs));
    }

    [Fact]
    public void Case_MapsCurrentUnitlessCentimetreDimensionsWithoutInventingAtx()
    {
        var specs = Parse("CASE", null, "LIAN LI B4-mATX",
            "form factor\nmicro-ATXmini-ITX\nmax. graphic card length\n36\nmax. CPU cooler height\n17\nPSU length support\n14\nexpansion slots\n4\nwater cooling\n-");

        Assert.Equal(360m, specs.MaximumGpuLengthMm);
        Assert.Equal(170m, specs.MaximumCpuCoolerHeightMm);
        Assert.Equal(140m, specs.MaximumPsuLengthMm);
        Assert.Equal(4m, specs.MaximumGpuSlotWidth);
        Assert.Equal(["Micro-ATX", "Mini-ITX"], specs.SupportedMotherboardFormFactors);
        Assert.Empty(specs.SupportedPsuFormFactors);
        Assert.Null(CatalogImportService.ValidateSpecifications("CASE", specs));
    }

    [Fact]
    public void Case_MapsAllCurrentMultilineMotherboardFormFactors()
    {
        var specs = Parse("CASE", null, "LIAN LI Lancool 217 Window, White",
            "form factor\nSSI EEB\nE-ATX\nATX\nmax. form factor\nE-ATX\nmax. graphic card length\n38");

        Assert.Equal(["EEB", "E-ATX", "ATX"], specs.SupportedMotherboardFormFactors);
        Assert.Null(CatalogImportService.ValidateSpecifications("CASE", specs));
    }

    [Fact]
    public void Monitor_MapsCurrentLabels()
    {
        var specs = Parse("MONITOR", null, "LG ELECTRONICS StanbyME 2",
            "diagonal\n27 \"\nresolution (native)\n2560x1440\nmax. refresh rate\n60 Hz\npanel type\nIPS");

        Assert.Equal(27m, specs.ScreenSizeInches);
        Assert.Equal(2560, specs.ResolutionWidth);
        Assert.Equal(1440, specs.ResolutionHeight);
        Assert.Equal(60, specs.RefreshRateHz);
        Assert.Equal("IPS", specs.PanelType);
        Assert.Null(CatalogImportService.ValidateSpecifications("MONITOR", specs));
    }

    [Fact]
    public void Mouse_MapsCurrentLabelsAndConvertsKilogramsToGrams()
    {
        var specs = Parse("MOUSE", null, "LOGITECH MX Master 4",
            "cable\nwireless\nconnectors\nBluetooth LE, USB-C\nresolution\n8000\nweight\n0.15 kg");

        Assert.Equal(8000, specs.MaximumDpi);
        Assert.Equal(150m, specs.WeightGrams);
        Assert.Equal("wireless; Bluetooth LE, USB-C", specs.Connectivity);
        Assert.Null(CatalogImportService.ValidateSpecifications("MOUSE", specs));
    }

    [Fact]
    public void Keyboard_MapsCurrentLabels()
    {
        var specs = Parse("KEYBOARD", null, "LOGITECH MX Keys S Plus",
            "cable\nwireless\nconnectors\nBluetooth, USB-C\nkeyboard layout\nSwiss layout\nswitch type\nscissor");

        Assert.Equal("Swiss layout", specs.KeyboardLayout);
        Assert.Equal("scissor", specs.SwitchType);
        Assert.Equal("wireless; Bluetooth, USB-C", specs.Connectivity);
        Assert.Null(CatalogImportService.ValidateSpecifications("KEYBOARD", specs));
    }

    [Fact]
    public void MonitorValidator_RejectsEmptySpecifications()
    {
        Assert.NotNull(CatalogImportService.ValidateSpecifications("MONITOR", new ParsedSpecifications()));
    }

    [Theory]
    [InlineData("MOUSE")]
    [InlineData("KEYBOARD")]
    public void AccessoryValidator_AllowsMissingNonCompatibilitySpecifications(string categoryCode)
    {
        Assert.Null(CatalogImportService.ValidateSpecifications(categoryCode, new ParsedSpecifications()));
    }

    [Fact]
    public void MouseCategory_RejectsMousepadWithPreciseReason()
    {
        var error = CatalogImportService.ValidateProductCategory(
            "MOUSE",
            "MSI Agility GD21 Gaming Mousepad, Black");

        Assert.Equal("Produkt jest podkładką pod mysz, a nie myszą.", error);
    }

    [Fact]
    public void Keyboard_RecoversLayoutFromProductNameWhenDataSheetIsEmpty()
    {
        var specs = Parse(
            "KEYBOARD",
            null,
            "CHERRY XTRFY MX 3.1, Cherry MX2A RGB Red Switches, German Layout, White",
            string.Empty);

        Assert.Equal("German Layout", specs.KeyboardLayout);
        Assert.Null(CatalogImportService.ValidateSpecifications("KEYBOARD", specs));
    }

    [Fact]
    public void SpecificationRefresh_UsesParserVersionToInvalidateCurrentData()
    {
        var now = new DateTimeOffset(2026, 8, 23, 22, 0, 0, TimeSpan.Zero);

        Assert.True(CatalogImportService.ShouldRefreshSpecifications(now, "1.0.0", "1.1.0", now, 30));
        Assert.False(CatalogImportService.ShouldRefreshSpecifications(now, "1.1.0", "1.1.0", now, 30));
    }

    private static ParsedSpecifications Parse(string categoryCode, string? subtype, string name, string body) =>
        ToppreisePageParser.ParseProduct(categoryCode, subtype, name, $"<h1>{name}</h1>", body, []).Specifications;
}
