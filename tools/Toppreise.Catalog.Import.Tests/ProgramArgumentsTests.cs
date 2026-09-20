using ToppreiseCatalog.Import;
using Xunit;

namespace Toppreise.Catalog.Import.Tests;

public sealed class ProgramArgumentsTests
{
    [Theory]
    [InlineData("--presets-only")]
    [InlineData("--PRESETS-ONLY")]
    public void HasPresetsOnlyArgument_RecognizesSwitch(string argument)
        => Assert.True(Program.HasPresetsOnlyArgument([argument]));

    [Fact]
    public void HasPresetsOnlyArgument_ReturnsFalseForRegularConfigurationArguments()
        => Assert.False(Program.HasPresetsOnlyArgument(["--ToppreiseImport:Headless=false"]));
}
