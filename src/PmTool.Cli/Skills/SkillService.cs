using PmTool.Cli.Commands;

namespace PmTool.Cli.Skills;

internal enum SkillAction
{
    Installed,
    Updated,
    UpToDate,

    /// <summary>同名資料夾沒有標記，沒有 --force 不覆蓋。</summary>
    SkippedForeign,

    /// <summary>有標記但檔案被改過，沒有 --force 不覆蓋。</summary>
    SkippedModified,

    /// <summary>已安裝的版本比這份 kumi 內含的新，沒有 --force 不降級。</summary>
    SkippedNewer,

    /// <summary>uninstall 遇到檔案改過的資料夾，沒有 --force 不刪。</summary>
    KeptModified,
    Removed,

    /// <summary>uninstall、update 找到的是別人放的資料夾，沒動。</summary>
    NotOurs,
    NotInstalled,
    LegacyRemoved,

    /// <summary>舊名稱的資料夾還在：使用者不要移除，或沒有辦法問又沒有 --yes。</summary>
    LegacyKept,
    Failed,
}

internal sealed record SkillOutcome(SkillLocation Location, string Skill, SkillAction Action, string? Detail = null)
{
    public string Path => System.IO.Path.Combine(Location.Directory, Skill);
}

/// <summary>舊名稱的處理：問、直接移除（--yes）、留著並警告。</summary>
internal enum LegacyPolicy
{
    Keep,
    Remove,
    Ask,
}

/// <summary>install、update、uninstall、status 的邏輯；不輸出任何文字，輸出在 <see cref="Commands.SkillCommands"/>。</summary>
internal sealed class SkillService(IReadOnlyList<BundledSkill> skills, string version, Func<DateTimeOffset> clock)
{
    public static SkillService Current() => new(SkillBundle.Skills, HelpText.VersionNumber, () => DateTimeOffset.UtcNow);

    public async Task<IReadOnlyList<SkillOutcome>> InstallAsync(
        IReadOnlyList<SkillLocation> locations, bool force, LegacyPolicy legacy, Func<string, Task<bool>>? confirmRemoval)
    {
        List<SkillOutcome> outcomes = [];
        foreach (var location in locations)
        {
            outcomes.AddRange(await RemoveLegacyAsync(location, legacy, confirmRemoval));
            outcomes.AddRange(skills.Select(skill => InstallOne(location, skill, force)));
        }

        return outcomes;
    }

    public IReadOnlyList<SkillOutcome> Update(IReadOnlyList<SkillLocation> locations) =>
        [.. locations.SelectMany(location => skills
            .Where(skill =>
            {
                var directory = Path.Combine(location.Directory, skill.Name);
                SkillInstaller.Recover(directory);
                return SkillMarker.Read(directory) is not null;
            })
            .Select(skill => InstallOne(location, skill, force: false)))];

    public IReadOnlyList<SkillOutcome> Uninstall(IReadOnlyList<SkillLocation> locations, bool force = false) =>
        [.. locations.SelectMany(location => skills.Select(skill => UninstallOne(location, skill, force)))];

    /// <summary>每個位置每個 skill 現在的狀態，加上舊名稱的殘留。Action 借用：Installed 是裝了且最新、Updated 是裝了但不是最新。</summary>
    public IReadOnlyList<SkillOutcome> Status(IReadOnlyList<SkillLocation> locations)
    {
        List<SkillOutcome> outcomes = [];
        foreach (var location in locations)
        {
            foreach (var skill in skills)
            {
                var directory = Path.Combine(location.Directory, skill.Name);
                var marker = SkillMarker.Read(directory);
                outcomes.Add(SkillInstaller.Inspect(directory, version) switch
                {
                    SkillState.Missing => new SkillOutcome(location, skill.Name, SkillAction.NotInstalled),
                    SkillState.Foreign => new SkillOutcome(location, skill.Name, SkillAction.NotOurs),
                    SkillState.Modified => new SkillOutcome(location, skill.Name, SkillAction.SkippedModified, marker!.Version),
                    SkillState.Newer => new SkillOutcome(location, skill.Name, SkillAction.SkippedNewer, marker!.Version),
                    SkillState.Current => new SkillOutcome(location, skill.Name, SkillAction.UpToDate, marker!.Version),
                    _ => new SkillOutcome(location, skill.Name, SkillAction.Updated, marker!.Version),
                });
            }

            outcomes.AddRange(LegacyFound(location).Select(name => new SkillOutcome(location, name, SkillAction.LegacyKept)));
        }

        return outcomes;
    }

    /// <summary>位置底下有標記的 skill（update 要不要做、setup 要不要提 update）。</summary>
    public bool HasInstalled(IReadOnlyList<SkillLocation> locations) =>
        locations.Any(location => skills.Any(skill => SkillMarker.Read(Path.Combine(location.Directory, skill.Name)) is not null));

    private SkillOutcome InstallOne(SkillLocation location, BundledSkill skill, bool force)
    {
        var directory = Path.Combine(location.Directory, skill.Name);
        SkillInstaller.Recover(directory);
        var state = SkillInstaller.Inspect(directory, version);
        if (!force && state is SkillState.Foreign or SkillState.Modified or SkillState.Newer)
        {
            return new SkillOutcome(location, skill.Name, state switch
            {
                SkillState.Foreign => SkillAction.SkippedForeign,
                SkillState.Modified => SkillAction.SkippedModified,
                _ => SkillAction.SkippedNewer,
            }, SkillMarker.Read(directory)?.Version);
        }

        if (state == SkillState.Current && !force)
        {
            return new SkillOutcome(location, skill.Name, SkillAction.UpToDate, version);
        }

        try
        {
            SkillInstaller.Install(skill, directory, version, clock());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CliException)
        {
            return new SkillOutcome(location, skill.Name, SkillAction.Failed, exception.Message);
        }

        return new SkillOutcome(location, skill.Name, state is SkillState.Missing ? SkillAction.Installed : SkillAction.Updated, version);
    }

    private SkillOutcome UninstallOne(SkillLocation location, BundledSkill skill, bool force)
    {
        var directory = Path.Combine(location.Directory, skill.Name);
        switch (SkillInstaller.Inspect(directory, version))
        {
            case SkillState.Missing:
                return new SkillOutcome(location, skill.Name, SkillAction.NotInstalled);
            case SkillState.Foreign:
                return new SkillOutcome(location, skill.Name, SkillAction.NotOurs);
        }

        // Newer 也可能同時是改過的：直接看標記，不靠 Inspect 的優先順序。
        if (!force && SkillMarker.Read(directory) is { } marker && SkillInstaller.IsModified(directory, marker))
        {
            return new SkillOutcome(location, skill.Name, SkillAction.KeptModified);
        }

        try
        {
            SkillInstaller.Remove(directory);
            return new SkillOutcome(location, skill.Name, SkillAction.Removed);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new SkillOutcome(location, skill.Name, SkillAction.Failed, exception.Message);
        }
    }

    private async Task<IReadOnlyList<SkillOutcome>> RemoveLegacyAsync(
        SkillLocation location, LegacyPolicy policy, Func<string, Task<bool>>? confirm)
    {
        List<SkillOutcome> outcomes = [];
        foreach (var name in LegacyFound(location))
        {
            var directory = Path.Combine(location.Directory, name);
            var remove = policy switch
            {
                LegacyPolicy.Remove => true,
                LegacyPolicy.Ask when confirm is not null => await confirm(directory),
                _ => false,
            };
            if (!remove)
            {
                outcomes.Add(new SkillOutcome(location, name, SkillAction.LegacyKept));
                continue;
            }

            try
            {
                SkillInstaller.Remove(directory);
                outcomes.Add(new SkillOutcome(location, name, SkillAction.LegacyRemoved));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                outcomes.Add(new SkillOutcome(location, name, SkillAction.Failed, exception.Message));
            }
        }

        return outcomes;
    }

    private static IEnumerable<string> LegacyFound(SkillLocation location) =>
        SkillBundle.LegacyNames.Where(name => SkillInstaller.IsLegacy(Path.Combine(location.Directory, name), name));
}
