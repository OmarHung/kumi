using PmTool.Ai;

namespace PmTool.Api.Endpoints;

/// <summary>要存的 AI 開關與預設。<c>default</c> 是 null 或省略時不指定預設。</summary>
/// <c>allowPersonalConnections</c> 省略（null）時沿用目前存的值：只改開關與預設的舊用戶端，不能無聲無息地把它重設成「不允許」。
internal sealed record AiSettingsRequest(bool Enabled, AiSelectionData? Default = null, bool? AllowPersonalConnections = null)
{
    public AiSettingsInput ToInput(bool currentAllowPersonal) =>
        new(Enabled, Default?.ToSelection()) { AllowPersonalConnections = AllowPersonalConnections ?? currentAllowPersonal };
}

/// <summary>選了哪個連線的哪個模型、多深的思考（<c>effort</c> 是 null 時用模型自己的預設）。</summary>
internal sealed record AiSelectionData(Guid ConnectionId, string Model, AiEffort? Effort = null)
{
    public AiSelection ToSelection() => new(ConnectionId, Model, Effort);

    public static AiSelectionData? From(AiSelection? selection) =>
        selection is null ? null : new AiSelectionData(selection.ConnectionId, selection.Model, selection.Effort);
}

/// <summary>
/// 要存的連線。列舉是 snake_case（<c>kind</c> 是 anthropic、openai、google、openai_compatible、openrouter）。
/// <c>apiKey</c> 是 null 或省略時沿用已經存的金鑰（新增時是沒有金鑰），空字串是清掉。
/// </summary>
internal sealed record AiConnectionRequest(
    string Name,
    AiProviderKind Kind,
    IReadOnlyList<string> EnabledModels,
    string? BaseUrl = null,
    string? ApiKey = null,
    IReadOnlyList<string>? CustomModels = null)
{
    public AiConnectionInput ToInput() => new(Name, Kind, BaseUrl, ApiKey, EnabledModels, CustomModels ?? []);

    // 帶著金鑰，不能在記錄或例外訊息裡露出來。
    public override string ToString() =>
        $"AiConnectionRequest {{ Name = {Name}, Kind = {Kind}, BaseUrl = {BaseUrl}, ApiKey = {(ApiKey is null ? "null" : "***")} }}";
}

/// <summary>「讀取模型」用的、還沒存的設定。<c>apiKey</c> 是 null 且帶了 <c>connectionId</c> 時用那個連線存的金鑰。</summary>
internal sealed record AiDiscoverRequest(AiProviderKind Kind, string? BaseUrl = null, string? ApiKey = null, Guid? ConnectionId = null)
{
    public AiDiscoveryInput ToInput() => new(Kind, BaseUrl, ApiKey, ConnectionId);

    public override string ToString() =>
        $"AiDiscoverRequest {{ Kind = {Kind}, BaseUrl = {BaseUrl}, ApiKey = {(ApiKey is null ? "null" : "***")}, ConnectionId = {ConnectionId} }}";
}

/// <summary>AI 設定在 API 上的形狀。金鑰不會出現，只說有沒有存（<c>hasApiKey</c>）與最後幾個字（<c>apiKeyHint</c>）。</summary>
internal sealed record AiSettingsData(
    bool Enabled,
    bool AllowPersonalConnections,
    AiSelectionData? Default,
    IReadOnlyList<AiConnectionData> Connections,
    DateTimeOffset? UpdatedAt,
    PersonReference? UpdatedBy)
{
    public static AiSettingsData From(AiSettings settings, PeopleNames names) => new(
        settings.Enabled,
        settings.AllowPersonalConnections,
        AiSelectionData.From(settings.Default),
        [.. settings.Connections.Select(c => AiConnectionData.From(c, names))],
        settings.UpdatedAt,
        settings.UpdatedBy is { } person ? new PersonReference(person, names.NameOf(person)) : null);

    public static IEnumerable<Guid> PeopleOf(AiSettings settings) =>
        settings.Connections.Select(c => c.UpdatedBy).Concat(settings.UpdatedBy is { } person ? [person] : []);
}

internal sealed record AiConnectionData(
    Guid Id,
    string Name,
    AiProviderKind Kind,
    string? BaseUrl,
    bool HasApiKey,
    string? ApiKeyHint,
    IReadOnlyList<AiModelData> Models,
    DateTimeOffset? ModelsCheckedAt,
    AiUsageData Usage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    PersonReference UpdatedBy)
{
    public static AiConnectionData From(AiConnection connection, PeopleNames names) => new(
        connection.Id,
        connection.Name,
        connection.Kind,
        connection.BaseUrl,
        connection.HasApiKey,
        connection.ApiKeyHint,
        [.. connection.Models.Select(m => new AiModelData(m.Id, m.DisplayName, m.Efforts, m.Enabled, m.Custom))],
        connection.ModelsCheckedAt,
        new AiUsageData(connection.Usage.Month, connection.Usage.InputTokens, connection.Usage.OutputTokens, connection.Usage.Replies),
        connection.CreatedAt,
        connection.UpdatedAt,
        new PersonReference(connection.UpdatedBy, names.NameOf(connection.UpdatedBy)));
}

internal sealed record AiModelData(string Id, string DisplayName, IReadOnlyList<AiEffort> Efforts, bool Enabled, bool Custom);

internal sealed record AiUsageData(string Month, long InputTokens, long OutputTokens, int Replies);

internal sealed record AiDiscoveredModelsData(IReadOnlyList<AiDiscoveredModelData> Models)
{
    public static AiDiscoveredModelsData From(IEnumerable<AiDiscoveredModel> models) =>
        new([.. models.Select(m => new AiDiscoveredModelData(m.Id, m.DisplayName, m.Efforts))]);
}

/// <summary>成員能選的 AI。</summary>
internal sealed record AiOptionsData(
    bool Available, bool CanAddPersonal, IReadOnlyList<AiConnectionOptionData> Connections, AiSelectionData? Default)
{
    public static AiOptionsData From(AiOptions options) => new(
        options.Available,
        options.CanAddPersonal,
        [.. options.Connections.Select(c => new AiConnectionOptionData(
            c.Id, c.Name, c.Kind, [.. c.Models.Select(m => new AiModelOptionData(m.Id, m.DisplayName, m.Efforts, m.DefaultEffort))], c.Personal, c.Hosted, c.Notice))],
        AiSelectionData.From(options.Default));
}

/// <param name="Hosted">組織擁有者開的主機來源；<c>id</c> 是來源的編號。</param>
/// <param name="Notice">主機來源現在不能用時給成員看的一句話；能用是 null（寫出 null，不省略）。</param>
internal sealed record AiConnectionOptionData(
    Guid Id, string Name, AiProviderKind Kind, IReadOnlyList<AiModelOptionData> Models, bool Personal, bool Hosted, string? Notice);

internal sealed record AiDiscoveredModelData(string Id, string DisplayName, IReadOnlyList<AiEffort> Efforts);

/// <param name="DefaultEffort">不指定思考深度時實際用的等級；不知道是 null（寫出 null，不省略）。</param>
internal sealed record AiModelOptionData(string Id, string DisplayName, IReadOnlyList<AiEffort> Efforts, AiEffort? DefaultEffort = null);
