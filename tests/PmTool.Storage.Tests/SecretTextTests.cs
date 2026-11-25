using PmTool.Abstractions;

namespace PmTool.Storage.Tests;

public sealed class SecretTextTests
{
    // 假值一律在執行時才組出來：原始碼裡不放完整的樣式，提交前的掃描就不會把測試資料當成真的權杖。
    private static string Repeat(char c, int count) => new(c, count);

    public static IEnumerable<TestCaseData> Secrets()
    {
        yield return new TestCaseData("-----" + "BEGIN PRIVATE KEY-----\nabc").SetName("private key");
        yield return new TestCaseData("-----" + "BEGIN RSA PRIVATE KEY-----").SetName("private key with algorithm");
        yield return new TestCaseData("-----" + "BEGIN  \t OPENSSH   PRIVATE\tKEY-----").SetName("private key with loose spacing");
        yield return new TestCaseData("-----" + "BEGIN PGP PRIVATE KEY BLOCK-----").SetName("pgp private key block");
        yield return new TestCaseData("PuTTY-User-Key-File-" + "3: ssh-ed25519").SetName("putty header");
        yield return new TestCaseData("token pmt" + "_" + Repeat('a', 43)).SetName("kumi token");
        yield return new TestCaseData("x kjt" + "_" + Repeat('B', 43) + " y").SetName("job token");
        yield return new TestCaseData("sk-ant-" + "api03-" + Repeat('a', 90)).SetName("anthropic key");
        yield return new TestCaseData("sk-or-" + "v1-" + Repeat('a', 64)).SetName("openrouter key");
        yield return new TestCaseData("sk-proj-" + Repeat('a', 45)).SetName("openai project key");
        yield return new TestCaseData("AIza" + Repeat('a', 35)).SetName("google key");
        yield return new TestCaseData("ghp" + "_" + Repeat('a', 36)).SetName("github token");
        yield return new TestCaseData("github_pat" + "_" + Repeat('a', 40)).SetName("github fine grained token");
        yield return new TestCaseData("AKIA" + Repeat('A', 16)).SetName("aws access key");
        yield return new TestCaseData("xoxb" + "-" + Repeat('1', 12)).SetName("slack token");
        yield return new TestCaseData("glpat" + "-" + Repeat('a', 22)).SetName("gitlab token");
    }

    [TestCaseSource(nameof(Secrets))]
    public void Detects_private_keys_and_known_tokens(string text)
    {
        Assert.That(SecretText.ContainsSecret(text), Is.True);
    }

    [TestCase("")]
    [TestCase("伺服器放在內網，密碼在密碼管理器的「主機」項目。")]
    [TestCase("pmt_short")]
    [TestCase("BEGIN PRIVATE KEY 只是一段說明")]
    [TestCase("-----BEGIN CERTIFICATE-----")]
    public void Lets_ordinary_text_through(string text)
    {
        Assert.That(SecretText.ContainsSecret(text), Is.False);
    }

    [Test]
    public void Does_not_match_a_token_with_extra_trailing_characters_of_its_alphabet_boundary()
    {
        var longer = "pmt" + "_" + Repeat('a', 44);

        Assert.That(SecretText.ContainsSecret(longer), Is.False);
    }
}
