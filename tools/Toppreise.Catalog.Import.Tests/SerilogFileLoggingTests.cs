using Serilog;
using ToppreiseCatalog.Import;
using Xunit;

namespace Toppreise.Catalog.Import.Tests;

public sealed class SerilogFileLoggingTests
{
    [Fact]
    public void FileSink_PersistsWarningsErrorsAndExceptionDetails()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pcwerk-toppreise-log-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var logger = Program.CreateLogger(
                Path.Combine(directory, "warnings-errors-.log"),
                includeConsole: false);
            try
            {
                logger.Information("information-marker");
                logger.Warning("warning-marker");
                logger.Error(new InvalidOperationException("exception-marker"), "error-marker");
            }
            finally
            {
                (logger as IDisposable)?.Dispose();
            }

            var file = Assert.Single(Directory.GetFiles(directory, "*.log"));
            var contents = File.ReadAllText(file);
            Assert.Contains("warning-marker", contents);
            Assert.Contains("error-marker", contents);
            Assert.Contains("exception-marker", contents);
            Assert.DoesNotContain("information-marker", contents);
        }
        finally
        {
            foreach (var file in Directory.GetFiles(directory))
            {
                File.Delete(file);
            }
            Directory.Delete(directory);
        }
    }
}
