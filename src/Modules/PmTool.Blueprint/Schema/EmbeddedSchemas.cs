using System.Reflection;

namespace PmTool.Blueprint.Schema;

/// <summary>schemas/blueprint/v0/*.schema.json 在建置時內嵌進組件，執行時不依賴檔案系統。</summary>
internal static class EmbeddedSchemas
{
    private const string ResourcePrefix = "PmTool.Blueprint.Schemas.";

    public static IReadOnlyList<string> ReadAll()
    {
        var assembly = typeof(EmbeddedSchemas).Assembly;
        return
        [
            .. assembly.GetManifestResourceNames()
                .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
                .Order(StringComparer.Ordinal)
                .Select(name => ReadText(assembly, name)),
        ];
    }

    private static string ReadText(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"找不到內嵌的 Schema：{resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
