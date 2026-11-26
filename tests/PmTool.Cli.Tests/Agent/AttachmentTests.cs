using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using PmTool.Cli.Agent;
using PmTool.Cli.Api;
using PmTool.Cli.Host;

namespace PmTool.Cli.Tests.Agent;

[TestFixture]
public sealed class AttachmentTests
{
    private const string Server = "https://pm.example.com";
    private const string WorkDirectory = "/tmp/kumi-agent-work";

    private string _directory = null!;

    [SetUp]
    public void SetUp() => _directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"kumi-attachment-test-{Guid.NewGuid():N}")).FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    private StagedAttachment Stage(string name, byte[] content, string contentType, string kind)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, content);
        return new StagedAttachment(path, contentType, kind);
    }

    // 解析

    [Test]
    public void Job_Parse_ReadsTheAttachmentsOfTheWireShape()
    {
        // Arrange
        var (image, _) = TestAttachments.Image();
        var wire = TestJobs.ToWire(TestJobs.Make());
        wire["attachments"] = new JsonArray(TestAttachments.ToWire(image));
        using var document = JsonDocument.Parse(wire.ToJsonString());

        // Act
        var job = AgentJob.Parse(document.RootElement);

        // Assert
        Assert.That(job!.Attachments, Is.EqualTo(new[] { image }));
    }

    [Test]
    public void Job_Parse_TreatsAMissingAttachmentsFieldFromAnOldServerAsEmpty()
    {
        // Arrange
        using var document = JsonDocument.Parse(TestJobs.ToWire(TestJobs.Make()).ToJsonString());

        // Assert
        Assert.That(AgentJob.Parse(document.RootElement)!.Attachments, Is.Empty);
    }

    [Test]
    public void Validation_RejectsAnAttachmentEntryThatCouldNotBeRead()
    {
        // Arrange
        var wire = TestJobs.ToWire(TestJobs.Make());
        wire["attachments"] = new JsonArray(new JsonObject { ["id"] = "not-a-guid" });
        using var document = JsonDocument.Parse(wire.ToJsonString());

        // Assert
        Assert.That(JobValidation.Check(AgentJob.Parse(document.RootElement)!, Server), Is.EqualTo("附件的種類不正確"));
    }

    // 驗證

    [Test]
    public void Validation_AcceptsImagesAndPdfsForClaudeAndImagesForCodex()
    {
        // Arrange
        var (image, _) = TestAttachments.Image();
        var (pdf, _) = TestAttachments.Pdf();

        // Assert
        Assert.That(JobValidation.Check(TestAttachments.With(TestJobs.Make(), image, pdf), Server), Is.Null);
        Assert.That(JobValidation.Check(TestAttachments.With(TestJobs.Make(EngineKinds.Codex), image), Server), Is.Null);
    }

    [Test]
    public void Validation_RejectsAPdfForCodex()
    {
        // Arrange
        var (pdf, _) = TestAttachments.Pdf();

        // Assert
        Assert.That(JobValidation.Check(TestAttachments.With(TestJobs.Make(EngineKinds.Codex), pdf), Server), Is.EqualTo("Codex 讀不到 PDF"));
    }

    [Test]
    public void Validation_RejectsMoreThanFiftyAttachments()
    {
        // Arrange
        var attachments = Enumerable.Range(0, 51).Select(_ => TestAttachments.Image(size: 1).Attachment).ToArray();

        // Assert
        Assert.That(JobValidation.Check(TestAttachments.With(TestJobs.Make(), attachments), Server), Is.EqualTo("附件太多"));
    }

    [Test]
    public void Validation_RejectsAKindThatDoesNotMatchTheContentTypeOrIsUnknown()
    {
        // Arrange
        var (image, _) = TestAttachments.Image();

        // Assert
        Assert.That(JobValidation.Check(TestAttachments.With(TestJobs.Make(), image with { Kind = AttachmentKinds.Pdf }), Server), Is.EqualTo("附件的種類不正確"));
        Assert.That(JobValidation.Check(TestAttachments.With(TestJobs.Make(), image with { Kind = "text" }), Server), Is.EqualTo("附件的種類不正確"));
        Assert.That(JobValidation.Check(TestAttachments.With(TestJobs.Make(), image with { ContentType = "image/svg+xml" }), Server), Is.EqualTo("附件的種類不正確"));
    }

    [TestCase(AttachmentKinds.Image, "image/png", 5L * 1024 * 1024 + 1)]
    [TestCase(AttachmentKinds.Pdf, "application/pdf", 10L * 1024 * 1024 + 1)]
    [TestCase(AttachmentKinds.Image, "image/png", 0)]
    [TestCase(AttachmentKinds.Image, "image/png", -1)]
    public void Validation_RejectsASizeOutsideTheLimitOfItsKind(string kind, string contentType, long size)
    {
        // Arrange
        var attachment = new AgentAttachment(Guid.NewGuid(), "x", contentType, kind, size, new string('a', 64));

        // Assert
        Assert.That(JobValidation.Check(TestAttachments.With(TestJobs.Make(), attachment), Server), Is.EqualTo("附件的大小不正確"));
    }

    [Test]
    public void Validation_RejectsATotalOverFifteenMebibytes()
    {
        // Arrange
        var big = new AgentAttachment(Guid.NewGuid(), "x", "application/pdf", AttachmentKinds.Pdf, 8L * 1024 * 1024, new string('a', 64));

        // Assert
        Assert.That(JobValidation.Check(TestAttachments.With(TestJobs.Make(), big, big), Server), Is.EqualTo("附件的總大小太大"));
    }

    [TestCase("")]
    [TestCase("ABCDEF")]
    [TestCase("g000000000000000000000000000000000000000000000000000000000000000")]
    public void Validation_RejectsAMalformedSha256(string sha)
    {
        // Arrange
        var (image, _) = TestAttachments.Image();

        // Assert
        Assert.That(JobValidation.Check(TestAttachments.With(TestJobs.Make(), image with { Sha256 = sha }), Server), Is.EqualTo("附件的雜湊不正確"));
    }

    // 命令列與標準輸入

    [Test]
    public void Claude_Plan_WithoutAttachmentsKeepsThePlainTextInputAndNoInputFormat()
    {
        // Act
        var plan = new ClaudeCodeSpec().Plan(TestJobs.Make(), WorkDirectory, []);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Arguments, Does.Not.Contain("--input-format"));
            Assert.That(plan.StandardInput, Is.EqualTo(TestJobs.Make().Prompt));
        }
    }

    [Test]
    public void Claude_Plan_WithAttachmentsSendsOneStreamJsonLineWithTextThenImageThenDocumentBlocks()
    {
        // Arrange
        var job = TestJobs.Make();
        var image = Stage("attachment-1.png", [1, 2, 3], "image/png", AttachmentKinds.Image);
        var pdf = Stage("attachment-2.pdf", [4, 5], "application/pdf", AttachmentKinds.Pdf);

        // Act
        var plan = new ClaudeCodeSpec().Plan(job, WorkDirectory, [image, pdf]);

        // Assert
        var args = plan.Arguments.ToList();
        var lines = plan.StandardInput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        using var line = JsonDocument.Parse(lines[0]);
        var message = line.RootElement.GetProperty("message");
        var blocks = message.GetProperty("content").EnumerateArray().ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(args[args.IndexOf("--input-format") + 1], Is.EqualTo("stream-json"));
            Assert.That(plan.StandardInput, Does.EndWith("\n"));
            Assert.That(lines, Has.Length.EqualTo(1));
            Assert.That(line.RootElement.GetProperty("type").GetString(), Is.EqualTo("user"));
            Assert.That(message.GetProperty("role").GetString(), Is.EqualTo("user"));
            Assert.That(blocks.Select(block => block.GetProperty("type").GetString()), Is.EqualTo(new[] { "text", "image", "document" }));
            Assert.That(blocks[0].GetProperty("text").GetString(), Is.EqualTo(job.Prompt));
            Assert.That(blocks[1].GetProperty("source").GetProperty("type").GetString(), Is.EqualTo("base64"));
            Assert.That(blocks[1].GetProperty("source").GetProperty("media_type").GetString(), Is.EqualTo("image/png"));
            Assert.That(blocks[1].GetProperty("source").GetProperty("data").GetString(), Is.EqualTo(Convert.ToBase64String([1, 2, 3])));
            Assert.That(blocks[2].GetProperty("source").GetProperty("media_type").GetString(), Is.EqualTo("application/pdf"));
            Assert.That(blocks[2].GetProperty("source").GetProperty("data").GetString(), Is.EqualTo(Convert.ToBase64String([4, 5])));
        }
    }

    [Test]
    public void Claude_Plan_WithAttachmentsKeepsEveryIsolationFlag()
    {
        // Arrange
        var image = Stage("attachment-1.png", [1], "image/png", AttachmentKinds.Image);

        // Act
        var withFiles = new ClaudeCodeSpec().Plan(TestJobs.Make(), WorkDirectory, [image]).Arguments.ToList();
        var without = new ClaudeCodeSpec().Plan(TestJobs.Make(), WorkDirectory).Arguments;

        // Assert
        withFiles.RemoveRange(withFiles.IndexOf("--input-format"), 2);
        Assert.That(withFiles, Is.EqualTo(without));
    }

    [Test]
    public void Codex_Plan_PassesEachImageAsItsOwnImageOptionBeforeTheFinalStdinMarker()
    {
        // Arrange
        var first = Stage("attachment-1.png", [1], "image/png", AttachmentKinds.Image);
        var second = Stage("attachment-2.jpg", [2], "image/jpeg", AttachmentKinds.Image);

        // Act
        var plan = new CodexSpec(["shell_tool"]).Plan(TestJobs.Make(EngineKinds.Codex), _directory, [first, second]);

        // Assert
        var args = plan.Arguments.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(args[^1], Is.EqualTo("-"));
            Assert.That(args.GetRange(args.Count - 3, 2), Is.EqualTo(new[] { "--image=attachment-1.png", "--image=attachment-2.jpg" }));
            Assert.That(plan.StandardInput, Is.EqualTo(TestJobs.Make(EngineKinds.Codex).Prompt));
        }
    }

    [Test]
    public void Codex_Plan_WithoutAttachmentsHasNoImageFlag()
    {
        // Act
        var plan = new CodexSpec([]).Plan(TestJobs.Make(EngineKinds.Codex), WorkDirectory);

        // Assert
        Assert.That(plan.Arguments, Has.None.StartsWith("--image").And.None.EqualTo("-i"));
    }

    // 下載與核對

    [Test]
    public async Task Stage_DownloadsEachAttachmentUnderAGenericNameIgnoringTheServersFileName()
    {
        // Arrange
        var (image, imageBytes) = TestAttachments.Image(fileName: "../../etc/passwd.png");
        var (pdf, pdfBytes) = TestAttachments.Pdf(fileName: "$(rm -rf).pdf");
        var source = new FakeAttachmentSource().Add(image, imageBytes).Add(pdf, pdfBytes);
        var job = TestAttachments.With(TestJobs.Make(), image, pdf);

        // Act
        var staged = await AttachmentStaging.StageAsync(job, _directory, source, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(staged.Select(file => Path.GetFileName(file.Path)), Is.EqualTo(new[] { "attachment-1.png", "attachment-2.pdf" }));
            Assert.That(File.ReadAllBytes(staged[0].Path), Is.EqualTo(imageBytes));
            Assert.That(File.ReadAllBytes(staged[1].Path), Is.EqualTo(pdfBytes));
            Assert.That(staged.Select(file => file.Kind), Is.EqualTo(new[] { AttachmentKinds.Image, AttachmentKinds.Pdf }));
            Assert.That(source.Opened, Is.EqualTo(new[] { (job.JobId, image.Id), (job.JobId, pdf.Id) }));
        }
    }

    [Test]
    public async Task Stage_WithNoAttachmentsNeverTouchesTheSource()
    {
        // Act
        var staged = await AttachmentStaging.StageAsync(TestJobs.Make(), _directory, null, CancellationToken.None);

        // Assert
        Assert.That(staged, Is.Empty);
    }

    [Test]
    public void Stage_RejectsContentWhoseHashDiffersEvenWhenTheSizeMatches()
    {
        // Arrange
        var (image, bytes) = TestAttachments.Image();
        var tampered = (byte[])bytes.Clone();
        tampered[0] ^= 1;
        var source = new FakeAttachmentSource().Add(image, tampered);

        // Act
        var failure = Assert.ThrowsAsync<AttachmentStagingException>(
            () => AttachmentStaging.StageAsync(TestAttachments.With(TestJobs.Make(), image), _directory, source, CancellationToken.None));

        // Assert
        Assert.That(failure!.Message, Does.StartWith("下載附件失敗").And.Contain("SHA-256"));
    }

    [Test]
    public void Stage_RejectsATooShortAndATooLongBody()
    {
        // Arrange
        var (image, bytes) = TestAttachments.Image(size: 40);
        var job = TestAttachments.With(TestJobs.Make(), image);

        // Act
        var tooShort = Assert.ThrowsAsync<AttachmentStagingException>(
            () => AttachmentStaging.StageAsync(job, _directory, new FakeAttachmentSource().Add(image, bytes[..10]), CancellationToken.None));
        var tooLong = Assert.ThrowsAsync<AttachmentStagingException>(
            () => AttachmentStaging.StageAsync(job, Directory.CreateDirectory(Path.Combine(_directory, "b")).FullName, new FakeAttachmentSource().Add(image, [.. bytes, .. bytes]), CancellationToken.None));

        // Assert
        Assert.That(tooShort!.Message, Does.Contain("大小"));
        Assert.That(tooLong!.Message, Does.Contain("大小"));
    }

    [Test]
    public void Stage_ReportsADownloadErrorWithoutTheFileNameAndStopsAtTheFirstFailure()
    {
        // Arrange
        var (image, _) = TestAttachments.Image(fileName: "機密客戶名單.png");
        var (second, secondBytes) = TestAttachments.Image();
        var source = new FakeAttachmentSource().Add(second, secondBytes);

        // Act
        var failure = Assert.ThrowsAsync<AttachmentStagingException>(
            () => AttachmentStaging.StageAsync(TestAttachments.With(TestJobs.Make(), image, second), _directory, source, CancellationToken.None));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure!.Message, Does.StartWith("下載附件失敗").And.Not.Contain("機密客戶名單"));
            Assert.That(source.Opened, Has.Count.EqualTo(1));
        }
    }

    // 下載的端點

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });
        }
    }

    [Test]
    public async Task AgentSource_DownloadsFromTheLocalAgentEndpointWithTheToken()
    {
        // Arrange
        var handler = new RecordingHandler();
        var job = Guid.NewGuid();
        var attachment = Guid.NewGuid();
        var source = new AgentAttachmentSource(new ApiClient(new HttpClient(handler), Server, "pmt_agent"));

        // Act
        using var download = await source.OpenAsync(job, attachment, CancellationToken.None);

        // Assert
        var request = handler.Requests.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(request.Method, Is.EqualTo(HttpMethod.Get));
            Assert.That(request.RequestUri!.PathAndQuery, Is.EqualTo($"/v1/agent/jobs/{job}/attachments/{attachment}"));
            Assert.That(request.Headers.Authorization!.ToString(), Is.EqualTo("Bearer pmt_agent"));
        }
    }

    [Test]
    public async Task HostSource_DownloadsFromTheHostEndpointWithTheLeaseAndTheMachineHeader()
    {
        // Arrange
        var handler = new RecordingHandler();
        var job = Guid.NewGuid();
        var attachment = Guid.NewGuid();
        var api = new ApiClient(new HttpClient(handler), Server, "kha_credential") { ExtraHeaders = HostMachineHeader.For("machine-0001") };
        var source = new HostAttachmentSource(api, 7);

        // Act
        using var download = await source.OpenAsync(job, attachment, CancellationToken.None);

        // Assert
        var request = handler.Requests.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(request.RequestUri!.PathAndQuery, Is.EqualTo($"/v1/host-agent/jobs/{job}/attachments/{attachment}?lease=7"));
            Assert.That(request.Headers.Authorization!.ToString(), Is.EqualTo("Bearer kha_credential"));
            Assert.That(request.Headers.GetValues(HostMachineHeader.Name), Is.EqualTo(new[] { "machine-0001" }));
        }
    }
}
