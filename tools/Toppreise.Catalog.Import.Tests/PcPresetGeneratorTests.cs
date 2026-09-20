using ToppreiseCatalog.Import;
using Xunit;

namespace Toppreise.Catalog.Import.Tests;

public sealed class PcPresetGeneratorTests
{
    [Theory]
    [InlineData("AMD Ryzen 9 9950X3D BOX", "Ryzen 9 9950X3D")]
    [InlineData("NVIDIA GeForce RTX 5070 Ti", "RTX 5070 Ti")]
    [InlineData("GeForce RTX 5090", "RTX 5090")]
    public void ModelMatches_MatchesConfiguredModel(string product, string configured)
        => Assert.True(PcPresetGenerator.ModelMatches(product, configured));

    [Theory]
    [InlineData("AMD Ryzen 9 9950X3D BOX", "Ryzen 9 9950X")]
    [InlineData("NVIDIA GeForce RTX 5070 Ti", "RTX 5070")]
    public void ModelMatches_DoesNotMatchLongerModel(string product, string configured)
        => Assert.False(PcPresetGenerator.ModelMatches(product, configured));
}
