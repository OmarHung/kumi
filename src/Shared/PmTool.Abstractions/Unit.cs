namespace PmTool.Abstractions;

/// <summary>沒有東西要回傳的成功。<see cref="Result{T}"/> 需要一個型別，刪除這類操作用它。</summary>
public readonly record struct Unit
{
    public static readonly Unit Value = default;
}
