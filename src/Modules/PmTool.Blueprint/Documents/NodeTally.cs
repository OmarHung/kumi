namespace PmTool.Blueprint.Documents;

/// <summary>
/// 深度與節點數的算法。JSON 與 YAML 共用這一份，兩種格式對同樣結構的文件才會得到同樣的結果。
/// <list type="bullet">
/// <item>節點：每一個值算一個。物件、陣列、純量（含 null）都算；物件的鍵不算。</item>
/// <item>深度：物件與陣列最多互相包了幾層。純量不加深度：根是純量時是 0，<c>[]</c> 與 <c>{"a":1}</c> 是 1，<c>[[0]]</c> 是 2。</item>
/// </list>
/// 讀到每一個值就檢查一次，超過上限立刻停，不必把整份文件讀完。
/// </summary>
internal readonly record struct NodeTally(int Depth, int Nodes)
{
    public NodeTally EnterContainer() => new(Depth + 1, Nodes + 1);

    public NodeTally ExitContainer() => this with { Depth = Depth - 1 };

    public NodeTally AddScalar() => this with { Nodes = Nodes + 1 };

    public ValidationIssue? FindViolation() =>
        Depth > DocumentLimits.MaxDepth ? DocumentIssues.TooDeep()
        : Nodes > DocumentLimits.MaxNodes ? DocumentIssues.TooManyNodes()
        : null;
}
