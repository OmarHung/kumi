using PmTool.Cli.Agent;

namespace PmTool.Cli.Tests.Agent;

[TestFixture]
[Platform(Include = "Linux,MacOsX")]
public sealed class ProcessRunnerStopTests
{
    [Test]
    public async Task Cancel_WhenTheKillCommandIsMissing_KillsAtOnceInsteadOfWaitingOutTheInterruptGrace()
    {
        // Arrange
        var options = new AgentOptions { InterruptGrace = TimeSpan.FromSeconds(30), DrainTimeout = TimeSpan.FromMilliseconds(200) };
        var runner = new ProcessRunner(options, _ => null);
        var request = new ProcessRequest("/bin/sleep", ["60"], Path.GetTempPath(), string.Empty, new Dictionary<string, string>());
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var clock = System.Diagnostics.Stopwatch.StartNew();

        // Act
        var result = await runner.RunAsync(request, _ => LineVerdict.Continue, TimeSpan.FromSeconds(60), cancel.Token);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Cancelled, Is.True);
            Assert.That(clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)));
        }
    }
}
