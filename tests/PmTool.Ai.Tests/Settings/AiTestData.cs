using PmTool.Abstractions;

namespace PmTool.Ai.Tests.Settings;

/// <summary>設定測試共用的人與連線輸入。</summary>
internal static class AiTestData
{
    public static readonly Guid OrganizationId = Guid.CreateVersion7();

    public static readonly Actor Admin = new(OrganizationId, Guid.CreateVersion7(), IsOrganizationAdmin: true);

    public static readonly Actor Member = new(OrganizationId, Guid.CreateVersion7(), IsOrganizationAdmin: false);

    public static AiConnectionInput Anthropic(
        string name = "Anthropic", string? apiKey = "sk-ant-key-0001", string[]? enabled = null, string[]? custom = null) =>
        new(name, AiProviderKind.Anthropic, null, apiKey, enabled ?? [], custom ?? []);

    public static AiConnectionInput Compatible(
        string name = "Ollama", string baseUrl = "http://ollama.example.com:11434/v1", string? apiKey = null, string[]? enabled = null, string[]? custom = null) =>
        new(name, AiProviderKind.OpenaiCompatible, baseUrl, apiKey, enabled ?? [], custom ?? []);
}
