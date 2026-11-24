namespace PmTool.Work.Tests.Projects;

/// <summary>給 AI 助理參考的模板：和專案服務同一份清單，另外能取得整份藍圖。</summary>
[TestFixture]
public sealed class TemplateLibraryTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public void List_IsTheSameListAsTheProjectServiceShows()
    {
        // Act
        var library = _host.Templates.List();

        // Assert
        Assert.That(library.Select(t => t.Key), Is.EqualTo(_host.Projects.ListTemplates().Select(t => t.Key)));
    }

    [Test]
    public void FindBlueprint_KnownKey_ReturnsTheWholeBlueprint()
    {
        // Act
        var blueprint = _host.Templates.FindBlueprint("simple_tasks");

        // Assert
        Assert.That(blueprint, Is.Not.Null);
        Assert.That(blueprint!.ItemTypes.Select(t => t.Key), Does.Contain("task"));
    }

    [Test]
    public void FindBlueprint_UnknownKey_ReturnsNull() =>
        Assert.That(_host.Templates.FindBlueprint("no_such_template"), Is.Null);
}
