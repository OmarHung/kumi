namespace PmTool.Blueprint.Schema;

/// <summary>Schema 裡的一個位置，給載入時的錯誤訊息用，例如 urn:example#/properties/name。</summary>
internal sealed record SchemaLocation(string DocumentId, string Pointer)
{
    public static SchemaLocation RootOf(string documentId) => new(documentId, string.Empty);

    public SchemaLocation Child(string segment) => this with { Pointer = JsonPointer.Append(Pointer, segment) };

    public override string ToString() => $"{DocumentId}#{Pointer}";
}
