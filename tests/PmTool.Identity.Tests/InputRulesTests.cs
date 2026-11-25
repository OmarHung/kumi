using PmTool.Abstractions;

namespace PmTool.Identity.Tests;

/// <summary>Email、顯示名稱、電腦名稱、權杖名稱的輸入規則：看不見的字元與全形寫法。</summary>
[TestFixture]
public sealed class InputRulesTests
{
    private IdentityTestHost _host = null!;
    private AuthenticatedUser _owner = null!;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _host = await IdentityTestHost.StartAsync();
        _owner = await _host.StartWithOwnerAsync();
    }

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task SignIn_WithFullWidthEmailInOtherCase_Succeeds()
    {
        // Arrange
        var id = Guid.NewGuid().ToString("N");
        var email = $"user-{id}@example.com";
        var created = await _host.People.CreateAsync(_owner.ToActor(), new NewPerson("範例使用者", email, TestData.Password, OrganizationRole.Member), CancellationToken.None);
        var fullWidth = ToFullWidth(email.ToUpperInvariant());

        // Act
        var user = await _host.Authentication.SignInAsync(fullWidth, TestData.Password, CancellationToken.None);

        // Assert
        Assert.That(created.Succeeded, Is.True);
        Assert.That(user?.PersonId, Is.EqualTo(created.Value.PersonId));
    }

    [Test]
    public async Task CreateAsync_FullWidthEmail_StoresTheNormalizedForm()
    {
        // Arrange
        var email = TestData.UniqueEmail();

        // Act
        var result = await _host.People.CreateAsync(_owner.ToActor(), new NewPerson("範例使用者", ToFullWidth(email), TestData.Password, OrganizationRole.Member), CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Value.Email, Is.EqualTo(email));
    }

    [Test]
    public async Task CreateAsync_EmailThatDiffersOnlyInFullWidth_ReturnsEmailTaken()
    {
        // Arrange
        var email = TestData.UniqueEmail();
        await _host.People.CreateAsync(_owner.ToActor(), new NewPerson("範例使用者", email, TestData.Password, OrganizationRole.Member), CancellationToken.None);

        // Act
        var result = await _host.People.CreateAsync(_owner.ToActor(), new NewPerson("另一位使用者", ToFullWidth(email), TestData.Password, OrganizationRole.Member), CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Code, Is.EqualTo(IdentityErrorCodes.EmailTaken));
    }

    [TestCase(0x200B, TestName = "Email 帶零寬空白")]
    [TestCase(0x202E, TestName = "Email 帶雙向控制字元")]
    [TestCase(0x3164, TestName = "Email 帶韓文填充字")]
    [TestCase(0x2800, TestName = "Email 帶點字空白")]
    [TestCase(0xFE00, TestName = "Email 帶變體選擇符")]
    [TestCase(0xE0100, TestName = "Email 帶補充的變體選擇符")]
    public async Task CreateAsync_EmailWithInvisibleCharacter_ReturnsEmailInvalid(int codePoint)
    {
        // Arrange
        var email = $"user{char.ConvertFromUtf32(codePoint)}-{Guid.NewGuid():N}@example.com";

        // Act
        var result = await _host.People.CreateAsync(_owner.ToActor(), new NewPerson("範例使用者", email, TestData.Password, OrganizationRole.Member), CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Details.Select(d => (d.Code, d.Path)), Is.EqualTo(new[] { (IdentityErrorCodes.EmailInvalid, "email") }));
    }

    [TestCase(0x202E, TestName = "顯示名稱帶雙向控制字元")]
    [TestCase(0x200D, TestName = "顯示名稱帶零寬連接字元")]
    [TestCase(0x3164, TestName = "顯示名稱帶韓文填充字")]
    [TestCase(0x0301, TestName = "顯示名稱以組合字元開頭")]
    [TestCase(0xFE0E, TestName = "顯示名稱帶文字樣式的變體選擇符")]
    [TestCase(0xFE00, TestName = "顯示名稱帶變體選擇符")]
    [TestCase(0xE01EF, TestName = "顯示名稱帶補充的變體選擇符")]
    public async Task CreateAsync_DisplayNameWithInvisibleCharacter_ReturnsFailure(int codePoint)
    {
        // Arrange
        var name = codePoint == 0x0301 ? char.ConvertFromUtf32(codePoint) + "範例" : "範例" + char.ConvertFromUtf32(codePoint) + "使用者";

        // Act
        var result = await _host.People.CreateAsync(_owner.ToActor(), new NewPerson(name, TestData.UniqueEmail(), TestData.Password, OrganizationRole.Member), CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Details.Select(d => d.Path), Is.EqualTo(new[] { "displayName" }));
    }

    [Test]
    public async Task CreateAsync_DisplayNameWithEmojiPresentationSelector_Succeeds()
    {
        // Arrange：U+FE0F 是 emoji 的圖形樣式，留著
        var name = "範例" + "\u2764\uFE0F";

        // Act
        var result = await _host.People.CreateAsync(_owner.ToActor(), new NewPerson(name, TestData.UniqueEmail(), TestData.Password, OrganizationRole.Member), CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True, result.Failure?.Message);
    }

    [Test]
    public async Task CreateAsync_DisplayNameWithCombiningCharacterAfterBaseLetter_Succeeds()
    {
        // Arrange
        var name = "Jose" + (char)0x0301;

        // Act
        var result = await _host.People.CreateAsync(_owner.ToActor(), new NewPerson(name, TestData.UniqueEmail(), TestData.Password, OrganizationRole.Member), CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True);
    }

    [Test]
    public async Task CreateAsync_TokenNameWithFillerCharacter_IsRejected()
    {
        // Arrange
        var token = new NewAccessToken("ci" + (char)0x3164, [AccessTokenScope.ItemsRead], 30);

        // Act
        var result = await _host.AccessTokens.CreateAsync(_owner.ToActor(), token, CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Details.Select(d => d.Path), Is.EqualTo(new[] { "name" }));
    }

    private static string ToFullWidth(string ascii) =>
        string.Concat(ascii.Select(c => c is >= '!' and <= '~' ? (char)(c - '!' + 0xFF01) : c));
}
