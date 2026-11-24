using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using PmTool.Abstractions;
using static PmTool.Work.Tests.SavedViews.SavedViewCrudTests;

namespace PmTool.Work.Tests.SavedViews;

/// <summary>檢視在邊界情況下的行為：被另一個分頁刪掉、資料列壞掉、名稱裡看不見的字元。</summary>
[TestFixture]
public sealed class SavedViewRobustnessTests
{
    private readonly CapturingLogs _logs = new();
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() =>
        _host = await WorkTestHost.StartAsync(services => services.AddLogging(b => b.AddProvider(_logs)));

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task ReplaceAsync_ViewDeletedAfterItWasRead_IsNotFound()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var view = (await Create(scenario.Admin, "會被刪掉")).Value;
        await using var other = await RowLockHolder.BeginAsync(_host.ConnectionString);
        await other.LockSavedViewAsync(view.Id);
        await using var scope = _host.Root.CreateAsyncScope();

        // Act：換掉的請求讀到那一列之後卡在更新上，另一個分頁這時把它刪掉並提交。
        var replacing = scope.ServiceProvider.GetRequiredService<ISavedViewService>().ReplaceAsync(
            scenario.Admin, "WEB", view.Id, new SavedViewDraft("新名字", SavedViewLayout.Board, Empty), CancellationToken.None);
        await RowLockHolder.WaitForLockWaitersAsync(_host.ConnectionString, 1);
        await other.DeleteSavedViewAsync(view.Id);
        await other.CommitAsync();
        var result = await replacing;

        // Assert
        AssertFailure(result, FailureKind.NotFound, WorkErrorCodes.SavedViewNotFound);
    }

    [Test]
    public async Task ListAsync_ViewWithUnreadableQuery_ComesBackWithEmptyQueryAndWarns()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var nullQuery = (await Create(scenario.Admin, "null")).Value;
        var wrongType = (await Create(scenario.Admin, "型別不對")).Value;
        var fine = (await Create(scenario.Admin, "正常", query: Empty with { Search = "abc" })).Value;
        await SetRawQueryAsync(nullQuery.Id, "null");
        await SetRawQueryAsync(wrongType.Id, """{"state": 5}""");
        _logs.Clear();

        // Act
        var result = await _host.SavedViews.ListAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
        var byId = result.Value.ToDictionary(v => v.Id);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(byId[nullQuery.Id].Query, Is.EqualTo(Empty));
            Assert.That(byId[wrongType.Id].Query, Is.EqualTo(Empty));
            Assert.That(byId[fine.Id].Query.Search, Is.EqualTo("abc"));
            Assert.That(_logs.Warnings, Has.Count.EqualTo(2));
            Assert.That(_logs.Warnings, Has.All.Contain(nullQuery.Id.ToString()).Or.Contain(wrongType.Id.ToString()));
            Assert.That(_logs.Warnings, Has.None.Contain("state"), "記錄不帶條件的內容");
        }
    }

    [TestCase("​", TestName = "ZeroWidthSpace")]
    [TestCase("‮", TestName = "RightToLeftOverride")]
    [TestCase("﻿", TestName = "ByteOrderMark")]
    public async Task CreateAsync_NameWithInvisibleFormatCharacter_IsInvalid(string invisible)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var inside = await Create(scenario.Admin, $"本週{invisible}到期");
        var onlyInvisible = await Create(scenario.Admin, $"a{invisible}");

        // Assert
        foreach (var result in new[] { inside, onlyInvisible })
        {
            AssertFailure(result, FailureKind.Invalid, WorkErrorCodes.SavedViewInvalid);
            Assert.That(result.Failure!.Details.Select(d => d.Path), Does.Contain("name"));
        }
    }

    [Test]
    public async Task CreateAsync_SearchWithFormatCharacter_StaysAllowed()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var result = await Create(scenario.Admin, "搜尋", query: Empty with { Search = "a‍b" });

        // Assert：只改檢視名稱；搜尋文字要不要擋是另一個決定。
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
    }

    private Task<Result<SavedView>> Create(Actor actor, string name, SavedViewQuery? query = null) =>
        _host.SavedViews.CreateAsync(actor, "WEB", new SavedViewDraft(name, SavedViewLayout.Table, query ?? Empty), CancellationToken.None);

    private async Task SetRawQueryAsync(Guid viewId, string json)
    {
        await using var connection = new NpgsqlConnection(_host.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("UPDATE work.saved_views SET query = @query WHERE id = @id", connection);
        command.Parameters.Add(new NpgsqlParameter("query", NpgsqlDbType.Jsonb) { Value = json });
        command.Parameters.AddWithValue("id", viewId);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class CapturingLogs : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _warnings = new();

        public IReadOnlyList<string> Warnings => [.. _warnings];

        public void Clear() => _warnings.Clear();

        public ILogger CreateLogger(string categoryName) => new Capture(_warnings);

        public void Dispose()
        {
        }

        private sealed class Capture(ConcurrentQueue<string> warnings) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (logLevel == LogLevel.Warning)
                {
                    warnings.Enqueue(formatter(state, exception));
                }
            }
        }
    }
}
