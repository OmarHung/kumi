using PmTool.Blueprint.Model;

namespace PmTool.Blueprint.Planning;

/// <summary>套用變更集時要對既有資料做的一件事。</summary>
public abstract record DataImpact
{
    /// <summary>受影響的項目數。要查資料才知道，計畫這一步還不碰資料庫，所以目前一律是 null。</summary>
    public long? AffectedItems { get; init; }
}

/// <summary>狀態被拿掉了，停在這個狀態的項目要搬到另一個狀態。</summary>
/// <param name="Workflow">新藍圖裡的流程。</param>
/// <param name="From">被拿掉的狀態。</param>
/// <param name="To">項目搬去的狀態，在新藍圖的這個流程裡。</param>
public sealed record StateMoveImpact(string Workflow, string From, string To) : DataImpact;

/// <summary>欄位被拿掉了，它的資料會被封存。</summary>
public sealed record FieldDropImpact(string Field) : DataImpact;

/// <summary>欄位換了型別，既有的值要轉換。</summary>
/// <param name="OnFailure">轉不過去的值怎麼處理。不會失敗的轉換是 null。</param>
public sealed record FieldConversionImpact(string Field, FieldType From, FieldType To, ConversionFailurePolicy? OnFailure)
    : DataImpact;

/// <summary>類型被拿掉了。套用時這個類型底下不能還有項目。</summary>
public sealed record ItemTypeRemovalImpact(string ItemType) : DataImpact;

/// <summary>類型不能再掛在某個類型底下。套用時不能還有這樣掛著的項目。</summary>
/// <param name="ItemType">被限制的類型。</param>
/// <param name="Parent">不再允許的上層類型；它本身還在新藍圖裡。</param>
public sealed record ParentRemovalImpact(string ItemType, string Parent) : DataImpact;

/// <summary>自訂角色被拿掉了。套用時不能還有成員是這個角色。</summary>
public sealed record RoleRemovalImpact(string Role) : DataImpact;

/// <summary>
/// 欄位從某個類型的欄位清單拿掉了，但欄位本身還在藍圖裡。這個類型的項目在這個欄位的值會被封存並從項目上拿掉；
/// 不拿掉的話，值留在項目上卻看不到、也清不掉。值是封存不是刪除，所以不需要變更集另外寫明。
/// </summary>
/// <param name="ItemType">不再有這個欄位的類型。</param>
/// <param name="Field">欄位的 key。</param>
public sealed record FieldDetachImpact(string ItemType, string Field) : DataImpact;
