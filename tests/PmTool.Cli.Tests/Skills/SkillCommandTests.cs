using System.Text.Json.Nodes;
using PmTool.Cli.Commands;
using PmTool.Cli.Skills;

namespace PmTool.Cli.Tests.Skills;

/// <summary>kumi skill：安裝位置、標記與 --force、舊名稱、update／status／uninstall。全部在測試自己的假家目錄裡。</summary>
[TestFixture]
public sealed class SkillCommandTests
{
    private CliHarness _cli = null!;

    private string Home => _cli.Environment["HOME"]!;

    private string ClaudeSkills => Path.Combine(Home, ".claude", "skills");

    private string CodexSkills => Path.Combine(Home, ".codex", "skills");

    [SetUp]
    public void SetUp()
    {
        _cli = new CliHarness();
        Directory.CreateDirectory(Path.Combine(Home, ".claude"));
        Directory.CreateDirectory(Path.Combine(Home, ".codex"));
    }

    [TearDown]
    public void TearDown() => _cli.Dispose();

    private static void WriteSkill(string directory, string name, string body = "自己寫的") =>
        File.WriteAllText(EnsureFile(Path.Combine(directory, name, "SKILL.md")), $"---\nname: {name}\n---\n{body}");

    private static string EnsureFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    // ---- install ----

    [Test]
    public async Task Install_BothEnginesDetected_InstallsBothSkillsWithReferencesAndMarkers()
    {
        // Act
        var result = await _cli.RunAsync("skill", "install");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            foreach (var root in new[] { ClaudeSkills, CodexSkills })
            {
                Assert.That(File.ReadAllText(Path.Combine(root, "kumi", "SKILL.md")), Does.StartWith("---\nname: kumi\n"));
                Assert.That(File.Exists(Path.Combine(root, "kumi", "references", "blueprint.md")), Is.True);
                Assert.That(File.ReadAllText(Path.Combine(root, "kumi-planning", "SKILL.md")), Does.StartWith("---\nname: kumi-planning\n"));
                Assert.That(File.Exists(Path.Combine(root, "kumi-planning", "references", "structure.md")), Is.True);
                var marker = SkillMarker.Read(Path.Combine(root, "kumi"));
                Assert.That(marker?.Version, Is.EqualTo(HelpText.VersionNumber));
            }

            Assert.That(result.Out, Does.Contain("✓ Claude Code：kumi").And.Contain("✓ Codex：kumi-planning"));
        }
    }

    [Test]
    public async Task Install_ConfigDirectoryVariables_OverrideTheDefaultLocations()
    {
        // Arrange
        var claude = _cli.PathOf("custom-claude");
        var codex = _cli.PathOf("custom-codex");
        _cli.Environment["CLAUDE_CONFIG_DIR"] = claude;
        _cli.Environment["CODEX_HOME"] = codex;

        // Act
        var result = await _cli.RunAsync("skill", "install", "--agent", "all");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(File.Exists(Path.Combine(claude, "skills", "kumi", "SKILL.md")), Is.True);
            Assert.That(File.Exists(Path.Combine(codex, "skills", "kumi", "SKILL.md")), Is.True);
            Assert.That(Directory.Exists(ClaudeSkills), Is.False);
        }
    }

    [Test]
    public async Task Install_OnlyOneEngineDetected_InstallsOnlyThere()
    {
        // Arrange（只留 Codex 的設定資料夾）
        Directory.Delete(Path.Combine(Home, ".claude"));

        // Act
        var result = await _cli.RunAsync("skill", "install");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(Directory.Exists(Path.Combine(CodexSkills, "kumi")), Is.True);
            Assert.That(Directory.Exists(Path.Combine(Home, ".claude")), Is.False);
        }
    }

    [Test]
    public async Task Install_EngineFoundOnPath_CountsAsDetectedEvenWithoutConfigFolder()
    {
        // Arrange
        Directory.Delete(Path.Combine(Home, ".claude"));
        Directory.Delete(Path.Combine(Home, ".codex"));
        var bin = Directory.CreateDirectory(_cli.PathOf("bin")).FullName;
        var fake = Path.Combine(bin, "claude");
        File.WriteAllText(fake, "#!/bin/sh\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(fake, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        _cli.Environment["PATH"] = bin;

        // Act
        var result = await _cli.RunAsync("skill", "install");

        // Assert
        Assert.That(Directory.Exists(Path.Combine(ClaudeSkills, "kumi")), Is.True, result.Out + result.Error);
    }

    [Test]
    public async Task Install_NoEngineDetected_ExplainsAndWritesNothing()
    {
        // Arrange
        Directory.Delete(Path.Combine(Home, ".claude"));
        Directory.Delete(Path.Combine(Home, ".codex"));

        // Act
        var result = await _cli.RunAsync("skill", "install");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success));
            Assert.That(result.Error, Does.Contain("找不到 Claude Code 或 Codex").And.Contain("--agent"));
            Assert.That(Directory.Exists(Path.Combine(Home, ".claude")), Is.False);
        }
    }

    [Test]
    public async Task Install_AgentOption_SelectsTheEngine()
    {
        // Act
        var result = await _cli.RunAsync("skill", "install", "--agent", "codex");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(Directory.Exists(Path.Combine(CodexSkills, "kumi")), Is.True);
            Assert.That(Directory.Exists(ClaudeSkills), Is.False);
        }
    }

    [Test]
    public async Task Install_UnknownAgent_IsAUsageError()
    {
        // Act
        var result = await _cli.RunAsync("skill", "install", "--agent", "cursor");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
            Assert.That(result.Error, Does.Contain("--agent 只能是"));
        }
    }

    [Test]
    public async Task Install_ProjectFolder_UsesClaudeAndAgentsFoldersAndLeavesTheHomeAlone()
    {
        // Arrange
        var project = Directory.CreateDirectory(_cli.PathOf("repo")).FullName;

        // Act
        var result = await _cli.RunAsync("skill", "install", "--project", project);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(File.Exists(Path.Combine(project, ".claude", "skills", "kumi", "SKILL.md")), Is.True);
            Assert.That(File.Exists(Path.Combine(project, ".agents", "skills", "kumi-planning", "SKILL.md")), Is.True);
            Assert.That(Directory.Exists(ClaudeSkills), Is.False);
            Assert.That(Directory.Exists(CodexSkills), Is.False);
        }
    }

    [Test]
    public async Task Install_ProjectFolderMissing_IsAUsageError()
    {
        // Act
        var result = await _cli.RunAsync("skill", "install", "--project", _cli.PathOf("no-such-folder"));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
            Assert.That(result.Error, Does.Contain("不存在"));
        }
    }

    // ---- 標記、--force、原子替換 ----

    [Test]
    public async Task Install_FolderWithoutMarker_IsNotOverwrittenUntilForce()
    {
        // Arrange
        WriteSkill(ClaudeSkills, "kumi", "我改過的內容");

        // Act
        var first = await _cli.RunAsync("skill", "install", "--agent", "claude-code");
        var kept = File.ReadAllText(Path.Combine(ClaudeSkills, "kumi", "SKILL.md"));
        var forced = await _cli.RunAsync("skill", "install", "--agent", "claude-code", "--force");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.ExitCode, Is.EqualTo(ExitCodes.Success));
            Assert.That(first.Error, Does.Contain("不是 kumi 裝的").And.Contain("--force"));
            Assert.That(kept, Does.Contain("我改過的內容"));
            Assert.That(forced.ExitCode, Is.EqualTo(ExitCodes.Success), forced.Error);
            Assert.That(File.ReadAllText(Path.Combine(ClaudeSkills, "kumi", "SKILL.md")), Does.Not.Contain("我改過的內容"));
            Assert.That(SkillMarker.Read(Path.Combine(ClaudeSkills, "kumi")), Is.Not.Null);
        }
    }

    [Test]
    public async Task Install_FolderWithMarkerOfAnOlderVersion_ReplacesItWholesale()
    {
        // Arrange（舊版多一個現在已經不存在的檔案）
        await _cli.RunAsync("skill", "install", "--agent", "claude-code");
        var kumi = Path.Combine(ClaudeSkills, "kumi");
        File.WriteAllText(Path.Combine(kumi, "references", "removed-in-new-version.md"), "舊的");
        File.WriteAllText(Path.Combine(kumi, SkillMarker.FileName), new SkillMarker("kumi", "0.0.1", DateTimeOffset.UnixEpoch).ToJson());

        // Act
        var result = await _cli.RunAsync("skill", "install", "--agent", "claude-code");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Out, Does.Contain("已更新到"));
            Assert.That(File.Exists(Path.Combine(kumi, "references", "removed-in-new-version.md")), Is.False);
            Assert.That(SkillMarker.Read(kumi)?.Version, Is.EqualTo(HelpText.VersionNumber));
            Assert.That(Directory.GetDirectories(ClaudeSkills).Select(Path.GetFileName), Is.EquivalentTo(new[] { "kumi", "kumi-planning" }));
        }
    }

    [Test]
    public async Task Install_SameVersionAgain_SaysUpToDateWithoutRewriting()
    {
        // Arrange
        await _cli.RunAsync("skill", "install", "--agent", "claude-code");
        var kumi = Path.Combine(ClaudeSkills, "kumi");
        var before = SkillMarker.Read(kumi)!.InstalledAt;

        // Act
        var result = await _cli.RunAsync("skill", "install", "--agent", "claude-code");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Out, Does.Contain("已經是最新版"));
            Assert.That(SkillMarker.Read(kumi)!.InstalledAt, Is.EqualTo(before));
        }
    }

    // ---- 改過的檔案、不降級 ----

    [TestCase("edit")]
    [TestCase("add")]
    [TestCase("remove")]
    public async Task Install_FilesChangedAfterInstalling_AreKeptUntilForce(string change)
    {
        // Arrange
        await _cli.RunAsync("skill", "install", "--agent", "claude-code");
        var kumi = Path.Combine(ClaudeSkills, "kumi");
        var blueprint = Path.Combine(kumi, "references", "blueprint.md");
        switch (change)
        {
            case "edit": File.AppendAllText(blueprint, "\n我加的一行"); break;
            case "add": File.WriteAllText(Path.Combine(kumi, "mine.md"), "x"); break;
            default: File.Delete(blueprint); break;
        }

        // Act
        var kept = await _cli.RunAsync("skill", "install", "--agent", "claude-code");
        var stillModified = change == "remove" ? !File.Exists(blueprint) : Directory.GetFiles(kumi, "*", SearchOption.AllDirectories).Any(f => File.ReadAllText(f).Contains("我加的一行") || f.EndsWith("mine.md"));
        var forced = await _cli.RunAsync("skill", "install", "--agent", "claude-code", "--force");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(kept.ExitCode, Is.EqualTo(ExitCodes.Success));
            Assert.That(kept.Error, Does.Contain("被改過").And.Contain("--force"));
            Assert.That(stillModified, Is.True);
            Assert.That(forced.Error, Is.Empty);
            Assert.That(File.Exists(blueprint), Is.True);
            Assert.That(File.Exists(Path.Combine(kumi, "mine.md")), Is.False);
            Assert.That(File.ReadAllText(blueprint), Does.Not.Contain("我加的一行"));
        }
    }

    [Test]
    public async Task Update_ModifiedFolder_IsSkippedWithAWarning()
    {
        // Arrange（舊版加改過）
        await _cli.RunAsync("skill", "install", "--agent", "claude-code");
        var kumi = Path.Combine(ClaudeSkills, "kumi");
        File.AppendAllText(Path.Combine(kumi, "SKILL.md"), "\n我改的");
        var marker = SkillMarker.Read(kumi)! with { Version = "0.0.1" };
        File.WriteAllText(Path.Combine(kumi, SkillMarker.FileName), marker.ToJson());

        // Act
        var result = await _cli.RunAsync("skill", "update");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success));
            Assert.That(result.Error, Does.Contain("kumi　").And.Contain("被改過"));
            Assert.That(File.ReadAllText(Path.Combine(kumi, "SKILL.md")), Does.Contain("我改的"));
            Assert.That(SkillMarker.Read(kumi)!.Version, Is.EqualTo("0.0.1"));
        }
    }

    [Test]
    public async Task Status_ModifiedFolder_SaysItWasChanged()
    {
        // Arrange
        await _cli.RunAsync("skill", "install", "--agent", "claude-code");
        File.AppendAllText(Path.Combine(ClaudeSkills, "kumi", "SKILL.md"), "\n我改的");

        // Act
        var text = await _cli.RunAsync("skill", "status");
        var json = await _cli.RunAsync("skill", "status", "--json");

        // Assert
        var row = JsonNode.Parse(json.Out)!["results"]!.AsArray().Single(r => r!["agent"]!.GetValue<string>() == "claude-code" && r["skill"]!.GetValue<string>() == "kumi")!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(text.Out, Does.Contain("檔案改過"));
            Assert.That(row["result"]!.GetValue<string>(), Is.EqualTo("modified"));
        }
    }

    [Test]
    public async Task Install_MarkerFromTheOldFormatWithoutHashes_CountsAsUnmodified()
    {
        // Arrange
        await _cli.RunAsync("skill", "install", "--agent", "claude-code");
        var kumi = Path.Combine(ClaudeSkills, "kumi");
        File.WriteAllText(Path.Combine(kumi, "extra.txt"), "x");
        File.WriteAllText(Path.Combine(kumi, SkillMarker.FileName), new SkillMarker("kumi", "0.0.1", DateTimeOffset.UnixEpoch).ToJson());

        // Act
        var result = await _cli.RunAsync("skill", "install", "--agent", "claude-code");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Out, Does.Contain("已更新到"));
            Assert.That(File.Exists(Path.Combine(kumi, "extra.txt")), Is.False);
        }
    }

    [Test]
    public async Task Install_InstalledVersionIsNewer_DoesNotDowngradeUntilForce()
    {
        // Arrange
        await _cli.RunAsync("skill", "install", "--agent", "claude-code");
        var kumi = Path.Combine(ClaudeSkills, "kumi");
        File.WriteAllText(Path.Combine(kumi, SkillMarker.FileName), SkillMarker.Read(kumi)!.ToJson().Replace(HelpText.VersionNumber, "99.0.0"));

        // Act
        var kept = await _cli.RunAsync("skill", "install", "--agent", "claude-code");
        var updated = await _cli.RunAsync("skill", "update");
        var keptVersion = SkillMarker.Read(kumi)!.Version;
        var forced = await _cli.RunAsync("skill", "install", "--agent", "claude-code", "--force");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(kept.Error, Does.Contain("已安裝的版本（99.0.0）比這份 kumi 內含的新"));
            Assert.That(updated.Error, Does.Contain("不降級"));
            Assert.That(keptVersion, Is.EqualTo("99.0.0"));
            Assert.That(SkillMarker.Read(kumi)!.Version, Is.EqualTo(HelpText.VersionNumber));
            Assert.That(forced.ExitCode, Is.EqualTo(ExitCodes.Success));
        }
    }

    // ---- 中斷後的備份 ----

    [Test]
    public async Task Update_TargetMissingButBackupLeftByAnInterruptedSwap_RestoresItFirst()
    {
        // Arrange（舊版放在 .kumi-skill-old-kumi-*，目標不見了）
        await _cli.RunAsync("skill", "install", "--agent", "claude-code");
        var kumi = Path.Combine(ClaudeSkills, "kumi");
        File.WriteAllText(Path.Combine(kumi, SkillMarker.FileName), SkillMarker.Read(kumi)!.ToJson().Replace(HelpText.VersionNumber, "0.0.1"));
        Directory.Move(kumi, Path.Combine(ClaudeSkills, ".kumi-skill-old-kumi-0123456789abcdef"));

        // Act
        var result = await _cli.RunAsync("skill", "update");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Out, Does.Contain("已更新到"));
            Assert.That(SkillMarker.Read(kumi)?.Version, Is.EqualTo(HelpText.VersionNumber));
            Assert.That(Directory.GetDirectories(ClaudeSkills).Select(Path.GetFileName), Is.EquivalentTo(new[] { "kumi", "kumi-planning" }));
        }
    }

    // ---- 位置 ----

    [Test]
    public async Task Install_RelativeConfigDirectory_ResolvesAgainstTheWorkingDirectory()
    {
        // Arrange
        _cli.Environment["CLAUDE_CONFIG_DIR"] = "rel-claude";

        // Act
        var result = await _cli.RunAsync("skill", "install", "--agent", "claude-code");

        // Assert
        Assert.That(File.Exists(Path.Combine(_cli.WorkingDirectory, "rel-claude", "skills", "kumi", "SKILL.md")), Is.True, result.Error);
    }

    [Test]
    public async Task Install_LegacyNameOnlyInTheBody_IsNotTreatedAsLegacy()
    {
        // Arrange（front matter 的 name 是別的，正文才寫 name: pm-tool）
        Directory.CreateDirectory(Path.Combine(ClaudeSkills, "pm-tool"));
        File.WriteAllText(Path.Combine(ClaudeSkills, "pm-tool", "SKILL.md"), "---\nname: other\n---\nname: pm-tool\n");

        // Act
        var result = await _cli.RunAsync("skill", "install", "--agent", "claude-code", "--yes");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Directory.Exists(Path.Combine(ClaudeSkills, "pm-tool")), Is.True);
            Assert.That(result.Out + result.Error, Does.Not.Contain("舊名稱"));
        }
    }

    [Test]
    public void Replace_WhenWritingTheNewCopyFails_KeepsTheOldFolderAndLeavesNoLeftovers()
    {
        // Arrange
        var destination = Path.Combine(_cli.PathOf("skills"), "kumi");
        var good = new BundledSkill("kumi", [new BundledFile("SKILL.md", "舊版"u8.ToArray())]);
        SkillInstaller.Install(good, destination, "1.0.0", DateTimeOffset.UnixEpoch);
        var broken = new BundledSkill("kumi", [new BundledFile("SKILL.md", "新版"u8.ToArray()), new BundledFile("../escape.md", "x"u8.ToArray())]);

        // Act
        var failure = Assert.Throws<CliException>(() => SkillInstaller.Install(broken, destination, "2.0.0", DateTimeOffset.UnixEpoch));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure!.Message, Does.Contain("路徑不對"));
            Assert.That(File.ReadAllText(Path.Combine(destination, "SKILL.md")), Is.EqualTo("舊版"));
            Assert.That(SkillMarker.Read(destination)?.Version, Is.EqualTo("1.0.0"));
            Assert.That(Directory.GetFileSystemEntries(Path.GetDirectoryName(destination)!).Select(Path.GetFileName), Is.EqualTo(new[] { "kumi" }));
        }
    }

    // ---- 舊名稱 ----

    [Test]
    public async Task Install_LegacyFolderWithoutYesNonInteractive_KeepsItAndWarns()
    {
        // Arrange
        WriteSkill(ClaudeSkills, "pm-tool");

        // Act
        var result = await _cli.RunAsync("skill", "install", "--agent", "claude-code");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success));
            Assert.That(Directory.Exists(Path.Combine(ClaudeSkills, "pm-tool")), Is.True);
            Assert.That(result.Error, Does.Contain("舊名稱的 skill 還在").And.Contain("--yes"));
        }
    }

    [Test]
    public async Task Install_LegacyFolderWithYes_RemovesBothLegacyNames()
    {
        // Arrange
        WriteSkill(ClaudeSkills, "pm-tool");
        WriteSkill(ClaudeSkills, "pm-tool-planning");

        // Act
        var result = await _cli.RunAsync("skill", "install", "--agent", "claude-code", "--yes");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Directory.Exists(Path.Combine(ClaudeSkills, "pm-tool")), Is.False);
            Assert.That(Directory.Exists(Path.Combine(ClaudeSkills, "pm-tool-planning")), Is.False);
            Assert.That(result.Out, Does.Contain("已移除舊名稱的 skill"));
            Assert.That(Directory.Exists(Path.Combine(ClaudeSkills, "kumi")), Is.True);
        }
    }

    [TestCase("y\n", true)]
    [TestCase("n\n", false)]
    public async Task Install_LegacyFolderInteractive_AsksAndFollowsTheAnswer(string answer, bool removed)
    {
        // Arrange
        WriteSkill(ClaudeSkills, "pm-tool");
        _cli.IsInteractive = true;
        _cli.StandardInput = answer;

        // Act
        var result = await _cli.RunAsync("skill", "install", "--agent", "claude-code");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Out, Does.Contain("要移除嗎？"));
            Assert.That(Directory.Exists(Path.Combine(ClaudeSkills, "pm-tool")), Is.EqualTo(!removed));
        }
    }

    [Test]
    public async Task Install_FolderCalledPmToolButNotTheOldSkill_IsNotTreatedAsLegacy()
    {
        // Arrange（SKILL.md 的 name 不是 pm-tool：使用者自己的東西）
        Directory.CreateDirectory(Path.Combine(ClaudeSkills, "pm-tool"));
        File.WriteAllText(Path.Combine(ClaudeSkills, "pm-tool", "SKILL.md"), "---\nname: my-own\n---\n");

        // Act
        var result = await _cli.RunAsync("skill", "install", "--agent", "claude-code", "--yes");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Directory.Exists(Path.Combine(ClaudeSkills, "pm-tool")), Is.True);
            Assert.That(result.Out + result.Error, Does.Not.Contain("舊名稱"));
        }
    }

    // ---- update ----

    [Test]
    public async Task Update_OnlyTouchesFoldersWithMarkers()
    {
        // Arrange（kumi 有標記但是舊版；kumi-planning 是使用者自己的）
        await _cli.RunAsync("skill", "install", "--agent", "claude-code");
        Directory.Delete(Path.Combine(ClaudeSkills, "kumi-planning"), recursive: true);
        WriteSkill(ClaudeSkills, "kumi-planning", "使用者的");
        var kumi = Path.Combine(ClaudeSkills, "kumi");
        File.WriteAllText(Path.Combine(kumi, SkillMarker.FileName), new SkillMarker("kumi", "0.0.1", DateTimeOffset.UnixEpoch).ToJson());

        // Act
        var result = await _cli.RunAsync("skill", "update");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(SkillMarker.Read(kumi)?.Version, Is.EqualTo(HelpText.VersionNumber));
            Assert.That(File.ReadAllText(Path.Combine(ClaudeSkills, "kumi-planning", "SKILL.md")), Does.Contain("使用者的"));
            Assert.That(Directory.Exists(CodexSkills), Is.False);
        }
    }

    [Test]
    public async Task Update_NothingInstalled_ExplainsAndSucceeds()
    {
        // Act
        var result = await _cli.RunAsync("skill", "update");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success));
            Assert.That(result.Out, Does.Contain("沒有找到 kumi 裝的 skill").And.Contain("kumi skill install"));
            Assert.That(Directory.Exists(ClaudeSkills), Is.False);
        }
    }

    [Test]
    public async Task Update_ProjectFolder_UpdatesThatFolder()
    {
        // Arrange
        var project = Directory.CreateDirectory(_cli.PathOf("repo")).FullName;
        await _cli.RunAsync("skill", "install", "--project", project, "--agent", "codex");
        var kumi = Path.Combine(project, ".agents", "skills", "kumi");
        File.WriteAllText(Path.Combine(kumi, SkillMarker.FileName), new SkillMarker("kumi", "0.0.1", DateTimeOffset.UnixEpoch).ToJson());

        // Act
        var result = await _cli.RunAsync("skill", "update", "--project", project);

        // Assert
        Assert.That(SkillMarker.Read(kumi)?.Version, Is.EqualTo(HelpText.VersionNumber), result.Out + result.Error);
    }

    // ---- status ----

    [Test]
    public async Task Status_ListsVersionsForeignFoldersAndLegacyLeftovers()
    {
        // Arrange
        await _cli.RunAsync("skill", "install", "--agent", "claude-code");
        File.WriteAllText(Path.Combine(ClaudeSkills, "kumi", SkillMarker.FileName), new SkillMarker("kumi", "0.0.1", DateTimeOffset.UnixEpoch).ToJson());
        Directory.Delete(Path.Combine(ClaudeSkills, "kumi-planning"), recursive: true);
        WriteSkill(ClaudeSkills, "kumi-planning");
        WriteSkill(ClaudeSkills, "pm-tool");

        // Act
        var result = await _cli.RunAsync("skill", "status");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(result.Out, Does.Contain("0.0.1（不是最新").And.Contain("不是 kumi 裝的").And.Contain("舊名稱的殘留"));
            Assert.That(result.Out, Does.Contain("沒有安裝"));
        }
    }

    [Test]
    public async Task Status_Json_GivesAMachineReadableResultPerFolder()
    {
        // Arrange
        await _cli.RunAsync("skill", "install", "--agent", "codex");

        // Act
        var result = await _cli.RunAsync("skill", "status", "--json");

        // Assert
        var rows = JsonNode.Parse(result.Out)!["results"]!.AsArray();
        var codexKumi = rows.Single(row => row!["agent"]!.GetValue<string>() == "codex" && row["skill"]!.GetValue<string>() == "kumi")!;
        var claudeKumi = rows.Single(row => row!["agent"]!.GetValue<string>() == "claude-code" && row["skill"]!.GetValue<string>() == "kumi")!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(codexKumi["result"]!.GetValue<string>(), Is.EqualTo("current"));
            Assert.That(codexKumi["version"]!.GetValue<string>(), Is.EqualTo(HelpText.VersionNumber));
            Assert.That(claudeKumi["result"]!.GetValue<string>(), Is.EqualTo("not_installed"));
        }
    }

    // ---- uninstall ----

    [Test]
    public async Task Uninstall_RemovesOnlyFoldersWithMarkers()
    {
        // Arrange
        await _cli.RunAsync("skill", "install", "--agent", "claude-code");
        Directory.Delete(Path.Combine(ClaudeSkills, "kumi-planning"), recursive: true);
        WriteSkill(ClaudeSkills, "kumi-planning", "使用者的");

        // Act
        var result = await _cli.RunAsync("skill", "uninstall");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(Directory.Exists(Path.Combine(ClaudeSkills, "kumi")), Is.False);
            Assert.That(File.ReadAllText(Path.Combine(ClaudeSkills, "kumi-planning", "SKILL.md")), Does.Contain("使用者的"));
            Assert.That(result.Out, Does.Contain("已移除").And.Contain("不是 kumi 裝的"));
        }
    }

    [Test]
    public async Task Uninstall_ModifiedFolder_IsKeptUntilForce()
    {
        // Arrange
        await _cli.RunAsync("skill", "install", "--agent", "claude-code");
        var kumi = Path.Combine(ClaudeSkills, "kumi");
        File.AppendAllText(Path.Combine(kumi, "SKILL.md"), "\n我改的");

        // Act
        var kept = await _cli.RunAsync("skill", "uninstall", "--agent", "claude-code");
        var keptExists = Directory.Exists(kumi);
        var json = await _cli.RunAsync("skill", "uninstall", "--agent", "claude-code", "--json");
        var forced = await _cli.RunAsync("skill", "uninstall", "--agent", "claude-code", "--force");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(kept.ExitCode, Is.EqualTo(ExitCodes.Success));
            Assert.That(kept.Error, Does.Contain("檔案改過，要移除請加 --force"));
            Assert.That(keptExists, Is.True);
            Assert.That(Directory.Exists(Path.Combine(ClaudeSkills, "kumi-planning")), Is.False, "沒改過的照樣移除");
            Assert.That(json.Out, Does.Contain("skipped_modified"));
            Assert.That(forced.ExitCode, Is.EqualTo(ExitCodes.Success), forced.Error);
            Assert.That(Directory.Exists(kumi), Is.False);
        }
    }

    [Test]
    public async Task Uninstall_NothingInstalled_SaysSoAndSucceeds()
    {
        // Act
        var result = await _cli.RunAsync("skill", "uninstall");

        // Assert
        Assert.That((result.ExitCode, result.Out), Is.EqualTo((ExitCodes.Success, "沒有找到 kumi 裝的 skill。" + Environment.NewLine)));
    }

    // ---- 打包 ----

    [Test]
    public void Bundle_ContainsBothSkillsAndNoStrayFiles()
    {
        // Act
        var skills = SkillBundle.Skills;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(skills.Select(skill => skill.Name), Is.EqualTo(new[] { "kumi", "kumi-planning" }));
            Assert.That(skills.SelectMany(skill => skill.Files).Select(file => Path.GetFileName(file.RelativePath)), Does.Not.Contain(".DS_Store"));
            Assert.That(skills[0].Files.Select(file => file.RelativePath), Does.Contain("SKILL.md").And.Contain("references/blueprint.md"));
        }
    }
}
