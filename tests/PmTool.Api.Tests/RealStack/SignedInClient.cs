namespace PmTool.Api.Tests.RealStack;

/// <summary>已經登入的用戶端（會記住 Cookie）與登入的人。</summary>
internal sealed record SignedInClient(HttpClient Client, Guid PersonId) : IDisposable
{
    public void Dispose() => Client.Dispose();
}
