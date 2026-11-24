using System.Collections.Concurrent;
using PmTool.Blueprint;

namespace PmTool.Api.Tests.Support;

internal sealed record ValidatorCall(string Method, string? Text, DocumentFormat Format);

/// <summary>測試用的驗證器。真的驗證器在其他工作包完成前會丟例外，而且 API 的測試只關心 API 怎麼把請求交給它。</summary>
internal sealed class FakeBlueprintValidator : IBlueprintValidator
{
    private readonly Func<ValidationReport> _respond;
    private readonly ConcurrentQueue<ValidatorCall> _calls = new();

    private FakeBlueprintValidator(Func<ValidationReport> respond) => _respond = respond;

    public IReadOnlyList<ValidatorCall> Calls => _calls.ToArray();

    public static FakeBlueprintValidator Passing() => new(() => new ValidationReport([]));

    public static FakeBlueprintValidator Reporting(ValidationReport report) => new(() => report);

    public static FakeBlueprintValidator Throwing(Exception exception) => new(() => throw exception);

    public BlueprintValidationResult ValidateBlueprint(string? documentText, DocumentFormat format, ValidationMode mode = ValidationMode.Authoring)
    {
        _calls.Enqueue(new ValidatorCall(nameof(ValidateBlueprint), documentText, format));
        return new BlueprintValidationResult(_respond(), null);
    }

    public ChangeSetValidationResult ValidateChangeSet(string? documentText, DocumentFormat format, ValidationMode mode = ValidationMode.Authoring)
    {
        _calls.Enqueue(new ValidatorCall(nameof(ValidateChangeSet), documentText, format));
        return new ChangeSetValidationResult(_respond(), null);
    }
}
