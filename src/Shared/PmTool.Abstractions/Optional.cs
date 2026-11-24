namespace PmTool.Abstractions;

/// <summary>
/// 局部更新用：分辨「沒有要改這個欄位」和「把這個欄位改成 null」。
/// 預設值（default）表示沒給；用值建立（包含 null）表示要改成那個值。
/// </summary>
public readonly record struct Optional<T>
{
    public Optional(T value)
    {
        HasValue = true;
        Value = value;
    }

    public bool HasValue { get; }

    /// <summary>沒給的時候是型別的預設值，先看 <see cref="HasValue"/>。</summary>
    public T Value { get; }

    public static implicit operator Optional<T>(T value) => new(value);
}
