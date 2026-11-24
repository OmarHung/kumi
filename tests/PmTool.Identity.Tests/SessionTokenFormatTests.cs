using System.Buffers.Text;

namespace PmTool.Identity.Tests;

/// <summary>
/// 不可能是我們發的權杖不送進資料庫。模組接上連不上的資料庫：一碰就會丟例外，
/// 所以回傳 null 而沒有丟例外，就表示沒有查資料庫。
/// </summary>
[TestFixture]
public sealed class SessionTokenFormatTests
{
    private IdentityTestHost _host = null!;

    [OneTimeSetUp]
    public void Start() => _host = IdentityTestHost.StartWithoutDatabase(configure: null);

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [TestCaseSource(nameof(MalformedTokens))]
    public async Task FindAsync_MalformedToken_ReturnsNullWithoutTheDatabase(string token)
    {
        // Act
        var found = await _host.Sessions.FindAsync(token, CancellationToken.None);

        // Assert
        Assert.That(found, Is.Null);
    }

    [TestCaseSource(nameof(MalformedTokens))]
    public async Task EndOthersAsync_MalformedToken_ReturnsZeroWithoutTheDatabase(string token)
    {
        // Act
        var ended = await _host.Sessions.EndOthersAsync(token, CancellationToken.None);

        // Assert
        Assert.That(ended, Is.Zero);
    }

    [TestCaseSource(nameof(MalformedTokens))]
    public async Task AccessTokenFindAsync_MalformedSecret_ReturnsNullWithoutTheDatabase(string token)
    {
        // Act：存取權杖的本體和工作階段的權杖同一種格式，由任何人放在 Authorization 標頭送來。
        var found = await _host.AccessTokens.FindAsync("pmt_" + token, CancellationToken.None);

        // Assert
        Assert.That(found, Is.Null);
    }

    [Test]
    public void FindAsync_WellFormedToken_AsksTheDatabase()
    {
        // Arrange：對照組。格式對的權杖會去查，連不上就丟例外；上面的測試才證明得了「沒有查」。
        var token = Base64Url.EncodeToString(new byte[32]);

        // Act & Assert：EF 會把連線錯誤包一層，所以只看有沒有丟例外。
        Assert.CatchAsync(() => _host.Sessions.FindAsync(token, CancellationToken.None));
    }

    private static IEnumerable<TestCaseData> MalformedTokens()
    {
        var valid = Base64Url.EncodeToString(new byte[32]);
        yield return Token(string.Empty, "空字串");
        yield return Token(valid[..42], "少一個字");
        yield return Token(valid + "A", "多一個字");
        yield return Token(new string('A', 10_000), "很長");
        yield return Token(valid[..42] + "=", "有補位的等號");
        yield return Token(valid[..42] + "+", "標準 base64 的加號");
        yield return Token(valid[..42] + "/", "標準 base64 的斜線");
        yield return Token(valid[..21] + " " + valid[..21], "中間有空白");
        // 43 個字的 base64url 最後一個字只用到 4 個位元，剩下 2 個位元要是 0。「B」的那 2 個位元不是 0：
        // .NET 的 Base64Url.TryDecodeFromChars 遇到這種輸入會丟 FormatException，而不是回 false。
        yield return Token(valid[..42] + "B", "最後一個字的多餘位元不是 0");
        yield return Token(valid[..42] + (char)0x0000, "有 NUL");
        yield return Token(valid[..42] + "é", "非 ASCII 的字母");
    }

    private static TestCaseData Token(string token, string name) => new TestCaseData(token).SetArgDisplayNames(name);
}
