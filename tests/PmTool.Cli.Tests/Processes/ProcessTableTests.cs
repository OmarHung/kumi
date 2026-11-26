using System.Diagnostics;
using PmTool.Cli.Agent;
using PmTool.Cli.Processes;

namespace PmTool.Cli.Tests.Processes;

[TestFixture]
public sealed class ProcessTableTests
{
    [Test]
    public void Parse_ReadsPidParentAndTheStartTimeAsOneNormalizedValue()
    {
        // Arrange
        const string output = "  101     1 Thu Nov 26 11:21:05 2026\n  102   101 Thu  Nov  6 01:02:03 2026\nnot a line\n  abc 1 Thu Nov 26 11:21:05 2026\n";

        // Act
        var entries = ProcessTable.Parse(output);

        // Assert
        Assert.That(entries, Is.EqualTo(new[]
        {
            new ProcessEntry(101, 1, "Thu Nov 26 11:21:05 2026"),
            new ProcessEntry(102, 101, "Thu Nov 6 01:02:03 2026"),
        }));
    }

    [Test]
    public void ApplyStableEnvironment_PinsLocaleAndTimeZoneSoRecordingAndComparingAgree()
    {
        // Arrange
        var info = new ProcessStartInfo("ps");

        // Act
        ProcessTable.ApplyStableEnvironment(info);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(info.Environment["LC_ALL"], Is.EqualTo("C"));
            Assert.That(info.Environment["TZ"], Is.EqualTo("UTC"));
        }
    }

    [Test]
    [Platform(Include = "Linux,MacOsX")]
    public void IsSameProcess_IsTrueForTheRecordedStartAndFalseWhenThePidIsReused()
    {
        // Arrange
        var self = Environment.ProcessId;
        var start = ProcessTable.StartOf(self);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(start, Is.Not.Null.And.Not.Empty);
            Assert.That(ProcessTable.IsSameProcess(self, start), Is.True);
            Assert.That(ProcessTable.IsSameProcess(self, "Mon Jan 1 00:00:00 2001"), Is.False, "編號相同但啟動時間不同：是別的程序");
            Assert.That(ProcessTable.IsSameProcess(self, null), Is.False, "當時查不到就不動手");
            Assert.That(ProcessTable.IsSameProcess(int.MaxValue, start), Is.False, "沒有這個程序");
        }
    }

    [Test]
    [Platform(Include = "Linux,MacOsX")]
    public async Task KillSurvivors_DoesNotKillAProcessWhoseStartTimeNoLongerMatches()
    {
        // Arrange
        using var bystander = Process.Start(new ProcessStartInfo("/bin/sleep", "60") { UseShellExecute = false })!;
        try
        {
            var tracker = new DescendantTracker();
            tracker.Track(bystander.Id, "Mon Jan 1 00:00:00 2001");

            // Act
            await tracker.KillSurvivorsAsync();

            // Assert
            Assert.That(bystander.HasExited, Is.False, "編號被別的程序重用時不能誤殺");
        }
        finally
        {
            bystander.Kill();
        }
    }

    [Test]
    [Platform(Include = "Linux,MacOsX")]
    public async Task KillSurvivors_KillsADescendantItRecordedWithTheMatchingStart()
    {
        // Arrange
        using var parent = Process.Start(new ProcessStartInfo("/bin/bash", ["-c", "sleep 60 & wait"]) { UseShellExecute = false })!;
        var tracker = new DescendantTracker();
        for (var attempt = 0; attempt < 50 && ProcessTable.Read().All(entry => entry.ParentId != parent.Id); attempt++)
        {
            await Task.Delay(100);
        }

        tracker.Snapshot(parent.Id);
        var child = ProcessTable.Read().First(entry => entry.ParentId == parent.Id).ProcessId;
        parent.Kill();
        await parent.WaitForExitAsync();

        // Act
        await tracker.KillSurvivorsAsync();

        // Assert
        for (var attempt = 0; attempt < 30 && ProcessTable.StartOf(child) is not null; attempt++)
        {
            await Task.Delay(100);
        }

        Assert.That(ProcessTable.StartOf(child), Is.Null, "還活著而且啟動時間一樣的被清掉了");
    }
}
