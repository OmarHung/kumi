using System.Text.Json.Serialization;
using PmTool.Blueprint.Model;
using PmTool.Blueprint.Planning;
using PmTool.Work;

namespace PmTool.Api.Endpoints;

/// <summary>
/// 一項資料影響在 API 上的形狀：共同的 kind、affected、blocksApply，加上每一種自己的屬性。
/// 不像活動那樣把所有屬性攤平、用不到的寫 null：七種的屬性幾乎不重疊，攤平之後大半是 null，
/// 網頁本來就要先看 kind 才知道怎麼顯示。
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(StateMoveImpactData), "state_move")]
[JsonDerivedType(typeof(FieldDropImpactData), "field_drop")]
[JsonDerivedType(typeof(FieldConversionImpactData), "field_conversion")]
[JsonDerivedType(typeof(ItemTypeRemovalImpactData), "item_type_removal")]
[JsonDerivedType(typeof(ParentRemovalImpactData), "parent_removal")]
[JsonDerivedType(typeof(RoleRemovalImpactData), "role_removal")]
[JsonDerivedType(typeof(FieldDetachImpactData), "field_detach")]
internal abstract record ImpactData(long Affected, bool BlocksApply)
{
    // 不認得的種類是合約加了新的影響、這裡沒跟上，屬於伺服器的錯；略過它的話，畫面上就少了一項會動到資料的事。
    public static ImpactData FromPlanned(PlannedImpact planned) => planned.Impact switch
    {
        StateMoveImpact move => new StateMoveImpactData(
            planned.Affected, planned.BlocksApply, move.Workflow, move.From, move.To),
        FieldDropImpact drop => new FieldDropImpactData(planned.Affected, planned.BlocksApply, drop.Field),
        FieldConversionImpact conversion => new FieldConversionImpactData(
            planned.Affected, planned.BlocksApply, conversion.Field, conversion.From, conversion.To, conversion.OnFailure),
        ItemTypeRemovalImpact removal => new ItemTypeRemovalImpactData(
            planned.Affected, planned.BlocksApply, removal.ItemType),
        ParentRemovalImpact parent => new ParentRemovalImpactData(
            planned.Affected, planned.BlocksApply, parent.ItemType, parent.Parent),
        RoleRemovalImpact role => new RoleRemovalImpactData(planned.Affected, planned.BlocksApply, role.Role),
        FieldDetachImpact detach => new FieldDetachImpactData(planned.Affected, planned.BlocksApply, detach.ItemType, detach.Field),
        _ => throw new InvalidOperationException($"不認得的資料影響：{planned.Impact.GetType().Name}。"),
    };
}

internal sealed record StateMoveImpactData(long Affected, bool BlocksApply, string Workflow, string From, string To)
    : ImpactData(Affected, BlocksApply);

internal sealed record FieldDropImpactData(long Affected, bool BlocksApply, string Field)
    : ImpactData(Affected, BlocksApply);

/// <param name="OnFailure">轉不過去的值怎麼處理；不會失敗的轉換是 null（照樣寫出來）。</param>
internal sealed record FieldConversionImpactData(
    long Affected, bool BlocksApply, string Field, FieldType FromType, FieldType ToType, ConversionFailurePolicy? OnFailure)
    : ImpactData(Affected, BlocksApply);

internal sealed record ItemTypeRemovalImpactData(long Affected, bool BlocksApply, string ItemType)
    : ImpactData(Affected, BlocksApply);

internal sealed record ParentRemovalImpactData(long Affected, bool BlocksApply, string ItemType, string Parent)
    : ImpactData(Affected, BlocksApply);

internal sealed record RoleRemovalImpactData(long Affected, bool BlocksApply, string Role)
    : ImpactData(Affected, BlocksApply);

/// <param name="ItemType">不再有這個欄位的類型。</param>
internal sealed record FieldDetachImpactData(long Affected, bool BlocksApply, string ItemType, string Field)
    : ImpactData(Affected, BlocksApply);
