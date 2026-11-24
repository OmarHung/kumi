using PmTool.Blueprint.Model;

namespace PmTool.Blueprint.Planning;

/// <summary>
/// 有些修改不會丟資料，卻可能讓既有的資料不再符合新的設定：類型被拿掉、類型不能再掛在某個類型底下、角色被拿掉。
/// 變更集沒有地方交代這些，有沒有受影響的資料也要查了才知道，所以計畫只把它們列出來，
/// 由套用那一步在還有這樣的資料時擋下。
/// </summary>
internal static class ApplyTimeCheckRules
{
    public static MigrationOutcome Check(ProjectBlueprint? current, ChangeSetRequest changeSet) =>
        current is null
            ? MigrationOutcome.Of(Array.Empty<DataImpact>())
            : MigrationOutcome.Of(
                RemovedItemTypes(current, changeSet.Blueprint)
                    .Concat(RemovedParents(current, changeSet.Blueprint))
                    .Concat(RemovedRoles(current, changeSet.Blueprint)));

    private static IEnumerable<DataImpact> RemovedItemTypes(ProjectBlueprint current, ProjectBlueprint next) =>
        RemovedKeys(current.ItemTypes.Select(type => type.Key), next.ItemTypes.Select(type => type.Key))
            .Select(key => new ItemTypeRemovalImpact(key));

    /// <summary>
    /// 依新藍圖的類型順序、再依原本上層清單的順序。上層類型整個被拿掉的不列：
    /// 那已經由類型的移除涵蓋，底下沒有那種項目，就不會有掛在它底下的項目。
    /// </summary>
    private static IEnumerable<DataImpact> RemovedParents(ProjectBlueprint current, ProjectBlueprint next)
    {
        var currentPositions = KeyPositions.First(current.ItemTypes, type => type.Key);
        var nextPositions = KeyPositions.First(next.ItemTypes, type => type.Key);
        for (var index = 0; index < next.ItemTypes.Count; index++)
        {
            var type = next.ItemTypes[index];
            if (nextPositions[type.Key] != index || !currentPositions.TryGetValue(type.Key, out var position))
            {
                continue;
            }

            foreach (var parent in RemovedKeys(current.ItemTypes[position].Parents, type.Parents).Where(nextPositions.ContainsKey))
            {
                yield return new ParentRemovalImpact(type.Key, parent);
            }
        }
    }

    private static IEnumerable<DataImpact> RemovedRoles(ProjectBlueprint current, ProjectBlueprint next) =>
        RemovedKeys(current.Roles.Select(role => role.Key), next.Roles.Select(role => role.Key))
            .Select(key => new RoleRemovalImpact(key));

    /// <summary>原本有、現在沒有的 key，依原本的順序，重複的只算一次。</summary>
    private static IEnumerable<string> RemovedKeys(IEnumerable<string> before, IEnumerable<string> after)
    {
        var remaining = after.ToHashSet(StringComparer.Ordinal);
        return before.Distinct(StringComparer.Ordinal).Where(key => !remaining.Contains(key));
    }
}
