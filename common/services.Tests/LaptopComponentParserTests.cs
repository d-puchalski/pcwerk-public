using Services.Catalog;
using Xunit;

namespace Services.Tests;

public sealed class LaptopComponentParserTests
{
    [Fact]
    public void Parse_ExtractsCoreComponentsFromLaptopName()
    {
        const string name = "HP OmniBook 7 17-dc0747nz (BG3Y1EA#UUZ), Core Ultra 7 258V (8x 2.2/4.8 GHz), 32 GB, 2.0 TB SSD, Swiss keyboard layout";

        var components = LaptopComponentParser.Parse(name);

        Assert.Collection(
            components,
            component => Assert.Equal(("CPU", "Core Ultra 7 258V (8x 2.2/4.8 GHz)"), (component.CategoryCode, component.Name)),
            component => Assert.Equal(("RAM", "32 GB"), (component.CategoryCode, component.Name)),
            component => Assert.Equal(("STORAGE", "2.0 TB SSD"), (component.CategoryCode, component.Name)));
    }

    [Fact]
    public void Parse_HandlesDisplayAndMemoryInTheSameSegment()
    {
        const string name = "APPLE MacBook Pro 16 CTO, Apple M5 Pro (18C/20C), Standard Display 64GB RAM, 1.0TB SSD, Space Black";

        var components = LaptopComponentParser.Parse(name);

        Assert.Contains(components, component => component.CategoryCode == "CPU" && component.Name == "Apple M5 Pro (18C/20C)");
        Assert.Contains(components, component => component.CategoryCode == "DISPLAY" && component.Name == "Standard Display");
        Assert.Contains(components, component => component.CategoryCode == "RAM" && component.Name == "64GB RAM");
        Assert.Contains(components, component => component.CategoryCode == "STORAGE" && component.Name == "1.0TB SSD");
    }
}
