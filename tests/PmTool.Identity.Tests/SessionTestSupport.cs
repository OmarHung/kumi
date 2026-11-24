using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;

namespace PmTool.Identity.Tests;

/// <summary>工作階段的測試共用的步驟：開一個帶假時鐘的模組、新增會登入的人、替他開工作階段。</summary>
internal static class SessionTestSupport
{
    public static Task<IdentityTestHost> StartWithClockAsync(ManualTimeProvider time) =>
        IdentityTestHost.StartAsync(services => services.AddSingleton<TimeProvider>(time));

    /// <summary>新增一個人並用他登入，回傳登入後的身分。</summary>
    public static async Task<AuthenticatedUser> AddUserAsync(
        this IdentityTestHost host, Actor creator, OrganizationRole role = OrganizationRole.Member, string? password = null)
    {
        var person = new NewPerson("範例使用者", TestData.UniqueEmail(), password ?? TestData.Password, role);
        var created = await host.People.CreateAsync(creator, person, CancellationToken.None);
        Assert.That(created.Succeeded, Is.True, created.Failure?.Message);
        return await host.Authentication.SignInAsync(person.Email, person.Password, CancellationToken.None)
            ?? throw new InvalidOperationException("剛建立的人登入不了。");
    }

    /// <summary>用這個人的密碼登入，回傳開出來的工作階段。工作階段只能經由登入開，所以要知道密碼（預設是新增時用的）。</summary>
    public static async Task<StartedSession> StartSessionAsync(this IdentityTestHost host, AuthenticatedUser user, string? password = null) =>
        (await host.Sessions.SignInAsync(user.Email, password ?? TestData.Password, CancellationToken.None))?.Session
        ?? throw new InvalidOperationException("登入不了，開不了工作階段。");

    public static Task<ActiveSession?> FindAsync(this IdentityTestHost host, StartedSession session) =>
        host.Sessions.FindAsync(session.Token, CancellationToken.None);

    /// <summary>每個工作階段現在還有沒有效，依傳入的順序。</summary>
    public static async Task<bool[]> AreActiveAsync(this IdentityTestHost host, params StartedSession[] sessions)
    {
        var active = new bool[sessions.Length];
        for (var i = 0; i < sessions.Length; i++)
        {
            active[i] = await host.FindAsync(sessions[i]) is not null;
        }

        return active;
    }
}
