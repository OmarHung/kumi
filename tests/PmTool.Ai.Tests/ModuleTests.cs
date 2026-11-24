namespace PmTool.Ai.Tests;

[TestFixture]
public sealed class ModuleTests
{
    [Test]
    public async Task Migrations_create_the_ai_schema_and_leave_nothing_pending()
    {
        // Arrange
        await using var host = await AiTestHost.StartAsync();
        await using var db = await host.CreateDbContextAsync();

        // Act
        var pending = await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.GetPendingMigrationsAsync(db.Database);

        // Assert
        Assert.That(pending, Is.Empty);
    }
}
