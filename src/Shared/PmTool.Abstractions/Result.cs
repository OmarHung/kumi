namespace PmTool.Abstractions;

/// <summary>
/// 一個可能被規則擋下來的操作的結果：不是值，就是失敗。
/// 「這個轉換不允許」「欄位的值不對」是預期中的結果，不用例外表達；例外留給真正出錯的情況。
/// </summary>
public readonly record struct Result<T>
{
    private readonly T? _value;

    private Result(T value)
    {
        _value = value;
        Failure = null;
    }

    private Result(Failure failure)
    {
        _value = default;
        Failure = failure;
    }

    public Failure? Failure { get; }

    public bool Succeeded => Failure is null;

    /// <summary>成功時的值。失敗時取值是程式寫錯了，所以丟例外。</summary>
    public T Value => Failure is null
        ? _value!
        : throw new InvalidOperationException($"這個操作失敗了（{Failure.Code}），沒有值可以取。");

    public static implicit operator Result<T>(T value) => new(value);

    public static implicit operator Result<T>(Failure failure) => new(failure);
}
