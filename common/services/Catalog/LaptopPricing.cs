namespace Services.Catalog;

public static class LaptopPricing
{
    public static decimal CalculateSellingPrice(decimal toppreisePrice)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(toppreisePrice);
        var markup = Math.Max(toppreisePrice * 0.10m, 150m);
        return Math.Round(toppreisePrice + markup, 2, MidpointRounding.AwayFromZero);
    }
}
