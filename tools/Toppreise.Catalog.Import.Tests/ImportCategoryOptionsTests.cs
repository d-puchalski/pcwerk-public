using ToppreiseCatalog.Import;
using Xunit;

namespace Toppreise.Catalog.Import.Tests;

public sealed class ImportCategoryOptionsTests
{
    [Fact]
    public void IsEnabled_UsesIndividualCategorySwitches()
    {
        var options = new ImportCategoryOptions
        {
            Cpu = false,
            Laptop = false
        };

        Assert.False(options.IsEnabled("CPU"));
        Assert.False(options.IsEnabled("laptop"));
        Assert.True(options.IsEnabled("GPU"));
    }

    [Fact]
    public void IsEnabled_DefaultsUnknownFutureCategoriesToEnabled()
    {
        var options = new ImportCategoryOptions();

        Assert.True(options.IsEnabled("FUTURE_CATEGORY"));
    }
}
