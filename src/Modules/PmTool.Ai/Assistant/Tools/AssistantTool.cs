using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace PmTool.Ai.Assistant.Tools;

/// <summary>
/// 助理的一個工具。參數的 Schema 自己寫（只用 type、properties、required、additionalProperties、description），
/// 結果是 JSON 字串。預期中的失敗回 <c>ok:false</c>，不丟例外；沒料到的例外記下來、回一般的失敗，不把內部細節給模型。
/// </summary>
internal abstract class AssistantTool(string name, string description, string schema, AssistantToolContext context) : AIFunction
{
    private static readonly JsonElement NoParameters = Parse("""{"type":"object","properties":{},"additionalProperties":false}""");

    private readonly JsonElement _schema = Parse(schema);

    public override string Name => name;

    public override string Description => description;

    public override JsonElement JsonSchema => _schema;

    protected AssistantToolContext Context => context;

    /// <summary>沒有參數的工具用的 Schema。</summary>
    protected static string NoParametersSchema => NoParameters.GetRawText();

    protected static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    protected abstract Task<string> RunAsync(AIFunctionArguments arguments, CancellationToken cancellationToken);

    protected sealed override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        try
        {
            return ToolResults.Limit(await RunAsync(arguments, cancellationToken));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // 不帶例外的訊息：可能有連線字串或其他內部資訊，而且模型用不到。
            context.Logger.LogWarning(exception, "助理的工具 {Tool} 執行時出錯。", name);
            return ToolResults.Error("工具執行時出錯了，請稍後再試。");
        }
    }
}
