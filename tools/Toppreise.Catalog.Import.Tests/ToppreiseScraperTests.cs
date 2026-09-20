using Microsoft.Playwright;
using ToppreiseCatalog.Import;
using Xunit;

namespace Toppreise.Catalog.Import.Tests;

public sealed class ToppreiseScraperTests
{
    [Fact]
    public void IsNavigationInterrupted_RecognizesThirdPartyNavigationRace()
    {
        var exception = new PlaywrightException(
            "Navigation to Toppreise is interrupted by another navigation to googleads.g.doubleclick.net");

        Assert.True(ToppreiseScraper.IsNavigationInterrupted(exception));
    }

    [Fact]
    public void IsNavigationInterrupted_DoesNotClassifyOtherFailuresAsRace()
    {
        var exception = new PlaywrightException("Timeout 45000ms exceeded");

        Assert.False(ToppreiseScraper.IsNavigationInterrupted(exception));
    }
}
