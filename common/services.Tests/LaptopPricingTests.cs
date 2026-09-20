using Services.Catalog;
using Xunit;

namespace Services.Tests;

public sealed class LaptopPricingTests
{
    [Fact]
    public void CalculateSellingPrice_UsesTenPercentMarkupWithMinimumOf150Chf()
    {
        Assert.Equal(650m, LaptopPricing.CalculateSellingPrice(500m));
        Assert.Equal(1649.95m, LaptopPricing.CalculateSellingPrice(1499.95m));
        Assert.Equal(2200m, LaptopPricing.CalculateSellingPrice(2000m));
    }
}
