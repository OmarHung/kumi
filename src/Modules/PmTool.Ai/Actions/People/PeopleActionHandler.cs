using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Identity;

namespace PmTool.Ai.Actions.People;

/// <summary>
/// 人員管理建議（internal/HANDOFF-ai-coverage-2.md 4.2）：新增人員、改組織角色、改職稱與負責範圍、停用、啟用。
/// 只有組織管理員能提；套用時身分模組再照使用者的權限檢查一次（誰能改誰、不能動自己、最後一位擁有者…）。
/// 新增人員的初始密碼是卡片上的輸入（<c>password:&lt;步驟&gt;</c>）：不經過模型、不進 State，只在套用那一步讀。
/// 個人資料服務用到才向容器要：沒有身分模組的測試主機不必為了別種建議註冊它。
/// </summary>
internal sealed class PeopleActionHandler(IPeopleDirectory people, IServiceProvider services) : IActionProposalHandler
{
    private const string NotAdmin = "人員管理只有組織管理員能做。";

    public string Kind => ActionKinds.People;

    public async Task<Result<PreparedAction>> PrepareAsync(ActionContext context, JsonElement payload, CancellationToken cancellationToken)
    {
        var actor = context.Actor;
        if (!actor.IsOrganizationAdmin)
        {
            return new Failure(FailureKind.Forbidden, AiErrorCodes.Forbidden, NotAdmin);
        }

        var directory = await people.ListAsync(actor.OrganizationId, cancellationToken);
        var me = directory.FirstOrDefault(p => p.PersonId == actor.PersonId);
        if (me is null || me.IsDisabled || me.Role is not (OrganizationRole.Owner or OrganizationRole.Admin))
        {
            return new Failure(FailureKind.Forbidden, AiErrorCodes.Forbidden, NotAdmin);
        }

        var issues = new List<FailureDetail>();
        if (PayloadJson.Changes(payload, issues) is not { } changes)
        {
            return PayloadJson.Invalid("人員建議不合規定，沒有建立。", issues);
        }

        var planned = new PeoplePlanner(actor, me.Role, directory).Plan(changes);
        if (planned.Failure is { } failure)
        {
            return failure;
        }

        var (steps, cards, inputs) = planned.Value;
        var title = steps.Count == 1 ? $"建議調整人員（{cards[0].Label}）" : $"建議調整人員（{steps.Count} 項）";
        return new PreparedAction(title, cards, PeopleStep.Write(steps), inputs);
    }

    public async Task<ActionStepResult> ApplyStepAsync(
        ActionContext context, JsonElement state, int index, IReadOnlyDictionary<string, string> inputs, CancellationToken cancellationToken)
    {
        // 一次新增好幾個人時，只有第 0 步被拒絕才會退回待決定：所以第一步先把每個新增步驟的密碼全部檢查完，
        // 不讓第 3 個人的密碼太短時前兩個人已經建好、建議卻變成已套用而不能重按。
        if (index == 0 && CheckAllPasswords(state, inputs) is { } rejected)
        {
            return ActionStepResult.Failed(rejected);
        }

        var step = PeopleStep.Read(state, index);
        var actor = context.Actor;
        return step.Action switch
        {
            PeopleStep.Create => await CreateAsync(actor, step, inputs, index, cancellationToken),
            PeopleStep.SetRole => FromResult(await people.SetRoleAsync(actor, step.Person!.Value, PeopleStep.ParseRole(step.Role)!.Value, cancellationToken)),
            PeopleStep.Disable => FromResult(await people.SetDisabledAsync(actor, step.Person!.Value, true, cancellationToken)),
            PeopleStep.Enable => FromResult(await people.SetDisabledAsync(actor, step.Person!.Value, false, cancellationToken)),
            PeopleStep.SetProfile => await SetProfileAsync(actor, step, cancellationToken),
            _ => throw new InvalidOperationException($"不認得的人員步驟：{step.Action}。"),
        };
    }

    private static Failure? CheckAllPasswords(JsonElement state, IReadOnlyDictionary<string, string> inputs)
    {
        var details = new List<FailureDetail>();
        var count = state.GetProperty("steps").GetArrayLength();
        for (var i = 0; i < count; i++)
        {
            if (PeopleStep.Read(state, i).Action != PeopleStep.Create)
            {
                continue;
            }

            var key = $"password:{i}";
            if (!inputs.TryGetValue(key, out var password) || password.Length < IdentityRules.MinPasswordLength)
            {
                details.Add(new FailureDetail(AiErrorCodes.FieldInvalid, $"inputs.{key}", $"初始密碼至少要 {IdentityRules.MinPasswordLength} 個字元。"));
            }
            else if (password.Length > IdentityRules.MaxPasswordLength)
            {
                details.Add(new FailureDetail(AiErrorCodes.FieldInvalid, $"inputs.{key}", $"初始密碼不能超過 {IdentityRules.MaxPasswordLength} 個字元。"));
            }
        }

        return details.Count == 0
            ? null
            : new Failure(FailureKind.Invalid, AiErrorCodes.FieldInvalid, "初始密碼不合規定，沒有套用。") { Details = details };
    }

    private async Task<ActionStepResult> CreateAsync(
        Actor actor, PeopleStep step, IReadOnlyDictionary<string, string> inputs, int index, CancellationToken cancellationToken)
    {
        // 密碼只從這一次的輸入讀；長度不夠就當作這一步被拒絕（還沒寫入），訊息不回顯密碼。
        if (!inputs.TryGetValue($"password:{index}", out var password)
            || password.Length is < IdentityRules.MinPasswordLength or > IdentityRules.MaxPasswordLength)
        {
            return ActionStepResult.Failed(new Failure(
                FailureKind.Invalid,
                AiErrorCodes.FieldInvalid,
                $"初始密碼要 {IdentityRules.MinPasswordLength} 到 {IdentityRules.MaxPasswordLength} 個字元。"));
        }

        var created = await people.CreateAsync(
            actor, new NewPerson(step.DisplayName!, step.Email!, password, PeopleStep.ParseRole(step.Role)!.Value), cancellationToken);
        if (created.Failure is not null || (step.JobTitle is null && step.Responsibilities is null))
        {
            return FromResult(created);
        }

        var profiled = await services.GetRequiredService<IPersonProfileService>().UpdateProfileAsync(
            actor, created.Value.PersonId, new PersonProfile(step.JobTitle, step.Responsibilities), cancellationToken);
        if (profiled.Failure is null)
        {
            return ActionStepResult.Done();
        }

        // 帳號已經建好了：回「寫進去了一部分」，即使是第一步也不會退回待決定（再按一次會重複建人），卡片上那一步寫明帳號已建立。
        return ActionStepResult.PartlyDone(new Failure(FailureKind.Conflict, profiled.Failure.Code, "帳號已建立，職稱沒有設定成功。"));
    }

    private async Task<ActionStepResult> SetProfileAsync(Actor actor, PeopleStep step, CancellationToken cancellationToken)
    {
        // 服務是整個換掉兩個欄位：沒要改的那個用套用當下的值，不拿建議建立時的舊值蓋掉別人剛改的。
        var found = await people.FindManyAsync(actor.OrganizationId, [step.Person!.Value], cancellationToken);
        if (!found.TryGetValue(step.Person.Value, out var current))
        {
            return ActionStepResult.Failed(new Failure(FailureKind.NotFound, IdentityErrorCodes.NotFound, "找不到這個人。"));
        }

        var profile = new PersonProfile(
            step.JobTitle is null ? current.JobTitle : step.JobTitle,
            step.Responsibilities is null ? current.Responsibilities : step.Responsibilities);
        var updated = await services.GetRequiredService<IPersonProfileService>().UpdateProfileAsync(actor, step.Person.Value, profile, cancellationToken);
        return FromResult(updated);
    }

    private static ActionStepResult FromResult(Result<PersonSummary> result) =>
        result.Failure is { } failure ? ActionStepResult.Failed(failure) : ActionStepResult.Done();
}
