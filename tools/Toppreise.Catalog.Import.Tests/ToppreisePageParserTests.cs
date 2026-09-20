using ToppreiseCatalog.Import;
using Xunit;

namespace Toppreise.Catalog.Import.Tests;

public sealed class ToppreisePageParserTests
{
    [Fact]
    public void ParseRanking_AcceptsLocalizedProductRoutesAndPreservesOrder()
    {
        const string html = """
            <a class="Plugin_Product" href="/price-comparison/AMD-Ryzen-p123">AMD Ryzen 7</a>
            <a href="/price-comparison/Sandisk-SSD-p999">product history</a>
            <a class="medium-box Plugin_Product shadow" href="/preisvergleich/Intel-Core-p456">Intel Core Ultra</a>
            <a class="Plugin_Product" href="/price-comparison/AMD-Ryzen-p123">duplicate</a>
            """;

        var result = ToppreisePageParser.ParseRanking(html, 100);

        Assert.Equal(2, result.Count);
        Assert.Equal((123L, 1), (result[0].ToppreiseProductId, result[0].Rank));
        Assert.Equal((456L, 2), (result[1].ToppreiseProductId, result[1].Rank));
    }

    [Fact]
    public void ParseRanking_IgnoresProductHistoryAndUsesCleanBrowserNames()
    {
        BrowserRankingRow[] rows =
        [
            new("https://www.toppreise.ch/price-comparison/AMD-Ryzen-p123", "AMD Ryzen 7 9800X3D"),
            new("https://www.toppreise.ch/not-a-ranking-product/Sandisk-p999", "SANDISK SSD CHF 149.00")
        ];

        var result = ToppreisePageParser.ParseRanking(rows, 100);

        var product = Assert.Single(result);
        Assert.Equal(123L, product.ToppreiseProductId);
        Assert.Equal("AMD Ryzen 7 9800X3D", product.Name);
    }

    [Fact]
    public void ParseProduct_MapsCriticalCpuFieldsFromTypedLabels()
    {
        const string body = """
            Brand
            AMD
            Manufacturer article number
            100-100001084WOF
            CPU socket
            Socket AM5
            Processor cores
            8
            TDP
            120 W
            EAN
            invalid value that must not reach varchar(32)
            """;

        var result = ToppreisePageParser.ParseProduct(
            "CPU",
            null,
            "AMD Ryzen 7 9800X3D 8x 4.7GHz (5.2GHz), Socket AM5",
            "<h1>AMD Ryzen 7 9800X3D</h1>",
            body,
            []);

        Assert.Equal("AM5", result.Specifications.Socket);
        Assert.Equal(8, result.Specifications.Cores);
        Assert.Equal(120, result.Specifications.TdpWatts);
        Assert.Null(result.Ean);
        Assert.Null(CatalogImportService.ValidateSpecifications("CPU", result.Specifications));
    }

    [Fact]
    public void Validator_RejectsGpuWithoutLength()
    {
        var error = CatalogImportService.ValidateSpecifications("GPU", new ParsedSpecifications());

        Assert.NotNull(error);
    }

    [Fact]
    public void ParseProduct_MapsCurrentToppreiseMotherboardLabels()
    {
        const string body = """
            Manufacturer
            ASROCK
            MPN:
            90-MXBR20-A0UAYZ
            chipset
            Intel B860
            socket
            Intel socket 1851
            memory
            type
            DDR5
            number of slots
            4
            dimensions
            format
            ATX
            """;

        var result = ToppreisePageParser.ParseProduct(
            "MOTHERBOARD",
            null,
            "ASROCK Phantom Gaming B860 Lightning WiFi, Intel B860",
            "<h1>ASROCK Phantom Gaming B860 Lightning WiFi, Intel B860</h1>",
            body,
            []);

        Assert.Equal("ASROCK", result.Manufacturer);
        Assert.Equal("90-MXBR20-A0UAYZ", result.ManufacturerPartNumber);
        Assert.Equal("1851", result.Specifications.Socket);
        Assert.Equal("ATX", result.Specifications.FormFactor);
        Assert.Equal("DDR5", result.Specifications.MemoryType);
        Assert.Equal(4, result.Specifications.MemorySlots);
        Assert.Null(CatalogImportService.ValidateSpecifications("MOTHERBOARD", result.Specifications));
    }

    [Theory]
    [InlineData("ASUS ROG STRIX Z890-I GAMING WIFI", "Intel socket 1851", "DDR5", "mini ITX", "1851", "Mini-ITX")]
    [InlineData("ASUS PRIME H610I-PLUS-CSM", "Intel socket 1700", "DDR5", "mini ITX", "1700", "Mini-ITX")]
    [InlineData("GIGABYTE Z890 AORUS MASTER AI TOP", "Intel socket 1851", "DDR5", "eATX", "1851", "E-ATX")]
    [InlineData("GIGABYTE Z790 AORUS XTREME X ICE", "Intel socket 1700", "DDR5", "Extended ATX", "1700", "E-ATX")]
    [InlineData("ASROCK WRX90 WS EVO", "AMD socket sTR5", "DDR5", "EEB", "STR5", "EEB")]
    [InlineData("ASUS Pro WS TRX50-SAGE WIFI", "AMD socket sTR5", "DDR5", "CEB", "STR5", "CEB")]
    public void ParseProduct_AcceptsMotherboardsPreviouslyHiddenByOldParser(
        string name,
        string socket,
        string memoryType,
        string formFactor,
        string expectedSocket,
        string expectedFormFactor)
    {
        var body = $"socket\n{socket}\nmemory\ntype\n{memoryType}\ndimensions\nformat\n{formFactor}";

        var result = ToppreisePageParser.ParseProduct(
            "MOTHERBOARD",
            null,
            name,
            $"<h1>{name}</h1>",
            body,
            []);

        Assert.Equal(expectedSocket, result.Specifications.Socket);
        Assert.Equal(memoryType, result.Specifications.MemoryType);
        Assert.Equal(expectedFormFactor, result.Specifications.FormFactor);
        Assert.Null(CatalogImportService.ValidateSpecifications("MOTHERBOARD", result.Specifications));
    }

    [Fact]
    public void Validator_ReportsMissingMotherboardFormFactorPrecisely()
    {
        var specs = new ParsedSpecifications { Socket = "AM5", MemoryType = "DDR5" };

        var error = CatalogImportService.ValidateSpecifications("MOTHERBOARD", specs);

        Assert.Contains("formatu", error);
    }

    [Fact]
    public void CoolerParser_DoesNotMistakeFanHeightForMissingHeatsinkHeight()
    {
        const string body = """
            CPU socket
            AM4, AM5, 1700, 1851
            heatsink
            height
            weight
            fan
            height
            25 mm
            """;

        var result = ToppreisePageParser.ParseProduct(
            "COOLING",
            "air",
            "DEEPCOOL AG620 BK ARGB V2",
            "<h1>DEEPCOOL AG620 BK ARGB V2</h1>",
            body,
            []);

        Assert.Null(result.Specifications.HeightMm);
        Assert.Contains("wysokości", CatalogImportService.ValidateSpecifications("COOLING", result.Specifications));
    }

    [Theory]
    [InlineData("DEEPCOOL LE240 V2", "recommended fan\n2x 12 cm", 240)]
    [InlineData("LIAN LI HydroShift II OLED Curved 360P28", "recommended fan\n3x 12 cm", 360)]
    [InlineData("SHARKOON S60 ARGB", "recommended fan\n3x 12 cm", 360)]
    [InlineData("SHARKOON S40", "recommended fan\n3x 12 cm", 360)]
    public void CoolerParser_MapsAioSizeFromEmbeddedModelOrRecommendedFans(
        string name,
        string body,
        int expectedSize)
    {
        var result = ToppreisePageParser.ParseProduct(
            "COOLING",
            "aio",
            name,
            $"<h1>{name}</h1>",
            $"CPU socket\nAM5\n{body}",
            []);

        Assert.Equal(expectedSize, result.Specifications.RadiatorSizeMm);
        Assert.Null(CatalogImportService.ValidateSpecifications("COOLING", result.Specifications));
    }

    [Fact]
    public void ParseProduct_MapsCurrentToppreiseStorageLabels()
    {
        const string body = """
            size
            M.2 (2280)
            total capacity
            4 TB
            protocol
            NVMe 2.0
            """;

        var result = ToppreisePageParser.ParseProduct(
            "STORAGE",
            null,
            "SAMSUNG 990 EVO Plus SSD M.2, 4.0TB (MZ-V9S4T0BW)",
            "<h1>SAMSUNG 990 EVO Plus SSD M.2, 4.0TB (MZ-V9S4T0BW)</h1>",
            body,
            []);

        Assert.Equal(4000, result.Specifications.CapacityGb);
        Assert.Equal("PCIe", result.Specifications.InterfaceType);
        Assert.Equal("NVMe", result.Specifications.Protocol);
        Assert.Equal("M.2 (2280)", result.Specifications.StorageFormFactor);
        Assert.Equal(80, result.Specifications.M2LengthMm);
        Assert.Null(CatalogImportService.ValidateSpecifications("STORAGE", result.Specifications));
    }

    [Theory]
    [InlineData("WESTERN DIGITAL Red SA500 NAS SSD, 4.0TB", "Serial ATA\nSATA 6Gb/s", "SATA")]
    [InlineData("SAMSUNG PM1653 SAS Enterprise SSD, 3.84 TB", "SCSI\nSAS 12Gb/s", "SAS")]
    public void ParseProduct_MapsStorageInterfaceFromConnectorSection(
        string name,
        string connectorLines,
        string expectedInterface)
    {
        var body = $"total capacity\n4 TB\n{connectorLines}";

        var result = ToppreisePageParser.ParseProduct(
            "STORAGE",
            null,
            name,
            $"<h1>{name}</h1>",
            body,
            []);

        Assert.Equal(expectedInterface, result.Specifications.InterfaceType);
        Assert.Null(CatalogImportService.ValidateSpecifications("STORAGE", result.Specifications));
    }

    [Fact]
    public void ParseOffers_MapsCurrentToppreiseDealerRow()
    {
        var result = ToppreisePageParser.ParseOffers([
            new BrowserOfferRow(
                "589543501",
                "Foletti Computer",
                "403.45",
                "403.45",
                "available from external/foreign warehouse")
        ]);

        var offer = Assert.Single(result);
        Assert.Equal("589543501", offer.ExternalKey);
        Assert.Equal("Foletti Computer", offer.RetailerName);
        Assert.Equal(403.45m, offer.ProductPrice);
        Assert.Equal(0m, offer.ShippingPrice);
        Assert.Equal(403.45m, offer.TotalPrice);
        Assert.Equal("unknown", offer.Availability);
    }

    [Theory]
    [InlineData("AMD Epyc 7203, Socket SP3", "SP3")]
    [InlineData("AMD Threadripper PRO 3945WX, Socket sWRX8", "SWRX8")]
    [InlineData("AMD Threadripper, Socket sTRX4", "STRX4")]
    [InlineData("AMD Threadripper, Socket TR4", "TR4")]
    [InlineData("AMD Threadripper, Socket TR5", "TR5")]
    [InlineData("AMD Ryzen 7 7800X3D, Socket A5", "AM5")]
    public void ParseProduct_NormalizesSupportedAmdSockets(string name, string expectedSocket)
    {
        var result = ToppreisePageParser.ParseProduct(
            "CPU",
            null,
            name,
            $"<h1>{name}</h1>",
            string.Empty,
            []);

        Assert.Equal(expectedSocket, result.Specifications.Socket);
        Assert.Null(CatalogImportService.ValidateSpecifications("CPU", result.Specifications));
    }
}
