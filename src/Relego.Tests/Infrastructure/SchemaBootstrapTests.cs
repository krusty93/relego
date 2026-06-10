using Relego.Server.Infrastructure.Database;

namespace Relego.Tests.Infrastructure;

public class SchemaBootstrapTests
{
    [Fact]
    public async Task ApplyAsync_WhenRunTwice_DoesNotThrow()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), $"relego-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDirectory);
        var dbPath = Path.Combine(testDirectory, "relego.db");

        try
        {
            var schemaBootstrap = new SchemaBootstrap();

            await schemaBootstrap.ApplyAsync(dbPath);
            var secondRunException = await Record.ExceptionAsync(() => schemaBootstrap.ApplyAsync(dbPath));

            Assert.Null(secondRunException);
            Assert.True(File.Exists(dbPath));
        }
        finally
        {
            if (Directory.Exists(testDirectory))
            {
                try
                {
                    Directory.Delete(testDirectory, recursive: true);
                }
                catch (IOException)
                {
                    // File may still be locked by SQLite; cleanup will happen on next run
                }
            }
        }
    }
}
