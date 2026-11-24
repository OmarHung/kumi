using System.Net;

namespace PmTool.Cli.Tests;

/// <summary>kumi item list --field：依自訂欄位篩選，照 HTTP 合約送成 field.&lt;欄位&gt; 參數。</summary>
[TestFixture]
public sealed class ItemFieldFilterTests
{
    private const string ItemsPath = "/v1/projects/MAINT/items";

    private static readonly object EmptyPage = new { items = Array.Empty<object>(), nextCursor = (string?)null };

    private static CliHarness LoggedIn()
    {
        var cli = new CliHarness().LoggedInByEnvironment();
        cli.Server.On("GET", ItemsPath, EmptyPage);
        cli.Server.On("GET", "/v1/projects/MAINT", new
        {
            project = new { key = "MAINT" },
            blueprint = new { fields = Array.Empty<object>(), workflows = Array.Empty<object>(), itemTypes = Array.Empty<object>() },
            myPermissions = new[] { "item.read" },
        });
        return cli;
    }

    [Test]
    public async Task ItemList_RepeatedFieldOptions_AreSentAsFieldParameters()
    {
        // Arrange
        using var cli = LoggedIn();

        // Act
        var result = await cli.RunAsync("item", "list", "-p", "MAINT", "--field", "client=client_a", "--field", "billable=true");

        // Assert
        var request = cli.Server.To("GET", ItemsPath).Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(request.QueryAll("field.client"), Is.EqualTo(new[] { "client_a" }));
            Assert.That(request.QueryAll("field.billable"), Is.EqualTo(new[] { "true" }));
        }
    }

    [Test]
    public async Task ItemList_CommaSeparatedValues_AreSentAsRepeatedParameters()
    {
        // Arrange
        using var cli = LoggedIn();

        // Act
        var result = await cli.RunAsync("item", "list", "-p", "MAINT", "--field", "owner=me,_none,0b1f6f3e-0000-4000-8000-000000000001");

        // Assert
        var request = cli.Server.To("GET", ItemsPath).Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(request.QueryAll("field.owner"), Is.EqualTo(new[] { "me", "_none", "0b1f6f3e-0000-4000-8000-000000000001" }));
        }
    }

    [Test]
    public async Task ItemList_FieldWithAll_KeepsConditionOnEveryPage()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", ItemsPath, request => request.Query("cursor") is null
            ? FakeServer.Envelope(HttpStatusCode.OK, new { items = Array.Empty<object>(), nextCursor = "c2" })
            : FakeServer.Envelope(HttpStatusCode.OK, EmptyPage));

        // Act
        var result = await cli.RunAsync("item", "list", "-p", "MAINT", "--field", "client=a,b", "--all", "--json");

        // Assert
        var requests = cli.Server.To("GET", ItemsPath);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(requests, Has.Count.EqualTo(2));
            Assert.That(requests.Select(request => string.Join("|", request.QueryAll("field.client"))), Is.All.EqualTo("a|b"));
        }
    }

    [TestCase("client", TestName = "ItemList_FieldWithoutEquals_IsUsageError")]
    [TestCase("Client=a", TestName = "ItemList_FieldNameWithUppercase_IsUsageError")]
    [TestCase("c=a", TestName = "ItemList_FieldNameTooShort_IsUsageError")]
    [TestCase("1client=a", TestName = "ItemList_FieldNameStartsWithDigit_IsUsageError")]
    [TestCase("client=", TestName = "ItemList_EmptyValue_IsUsageError")]
    [TestCase("client=a,", TestName = "ItemList_EmptyValueInList_IsUsageError")]
    [TestCase("=a", TestName = "ItemList_EmptyFieldName_IsUsageError")]
    [TestCase("client\n=a", TestName = "ItemList_FieldNameEndingWithANewline_IsUsageError")]
    public async Task ItemList_InvalidFieldOption_IsUsageErrorBeforeAnyRequest(string option)
    {
        // Arrange
        using var cli = LoggedIn();

        // Act
        var result = await cli.RunAsync("item", "list", "-p", "MAINT", "--field", option);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
            Assert.That(result.Error, Does.Contain("--field"));
            Assert.That(cli.Server.To("GET", ItemsPath), Is.Empty);
        }
    }

    [Test]
    public async Task ItemList_SameFieldGivenTwice_IsUsageError()
    {
        // Arrange
        using var cli = LoggedIn();

        // Act
        var result = await cli.RunAsync("item", "list", "-p", "MAINT", "--field", "client=a", "--field", "client=b");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
            Assert.That(result.Error, Does.Contain("client"));
            Assert.That(cli.Server.To("GET", ItemsPath), Is.Empty);
        }
    }

    [Test]
    public async Task ItemList_ServerRejectsFilter_PrintsServerMessage()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.OnError("GET", ItemsPath, HttpStatusCode.UnprocessableEntity, "items.invalid_filter", "欄位 client 不能用來篩選。");

        // Act
        var result = await cli.RunAsync("item", "list", "-p", "MAINT", "--field", "client=a");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Not.EqualTo(ExitCodes.Success));
            Assert.That(result.Error, Does.Contain("欄位 client 不能用來篩選。"));
        }
    }
}
