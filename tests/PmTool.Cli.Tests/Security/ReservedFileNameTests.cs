using PmTool.Cli.Commands;
using PmTool.Cli.Import.Monday;

namespace PmTool.Cli.Tests.Security;

[TestFixture]
public sealed class ReservedFileNameTests
{
    [TestCase("CON", "_CON")]
    [TestCase("nul.txt", "_nul.txt")]
    [TestCase("Com1.tar.gz", "_Com1.tar.gz")]
    [TestCase("LPT9.pdf", "_LPT9.pdf")]
    [TestCase("aux .log", "_aux .log")]
    [TestCase("COM¹.txt", "_COM¹.txt")]
    [TestCase("CONIN$", "_CONIN$")]
    [TestCase("conout$.txt", "_conout$.txt")]
    [TestCase("console.txt", "console.txt")]
    [TestCase("COM10.txt", "COM10.txt")]
    [TestCase("report.con", "report.con")]
    [TestCase("報告.pdf", "報告.pdf")]
    public void AttachmentSafeFileName_RenamesWindowsDeviceNames(string name, string expected) =>
        Assert.That(AttachmentFiles.SafeFileName(name, "fallback"), Is.EqualTo(expected));

    [TestCase("a:b*c?d\"e<f>g|h.txt", "a_b_c_d_e_f_g_h.txt")]
    [TestCase("report: final.pdf", "report_ final.pdf")]
    public void AttachmentSafeFileName_ReplacesWindowsInvalidCharactersOnEveryPlatform(string name, string expected) =>
        Assert.That(AttachmentFiles.SafeFileName(name, "fallback"), Is.EqualTo(expected));

    [TestCase("NUL", "_NUL")]
    [TestCase("prn.png", "_prn.png")]
    [TestCase("notes.txt", "notes.txt")]
    public void MondayDumpFileName_RenamesWindowsDeviceNames(string name, string expected) =>
        Assert.That(MondayDumpLayout.SafeFileName(name), Is.EqualTo(expected));
}
