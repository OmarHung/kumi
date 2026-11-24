using PmTool.Abstractions;

namespace PmTool.Notifications.Tests;

/// <summary>寄信設定：權限、存與讀、密碼、欄位檢查、測試信。</summary>
[TestFixture]
public sealed class EmailSettingsTests : EmailTestBase
{
    private Actor Member => new(Organization, Guid.CreateVersion7(), false);

    private async Task<EmailSettings> GetAsync() => (await Host.EmailSettings.GetAsync(Admin, CancellationToken.None)).Value;

    private async Task<Failure> SaveInvalidAsync(EmailSettingsInput input)
    {
        var result = await Host.EmailSettings.SaveAsync(Admin, input, CancellationToken.None);
        Assert.That(result.Succeeded, Is.False, "應該被擋下來");
        return result.Failure!;
    }

    private static FailureDetail Detail(Failure failure, string path) =>
        failure.Details.Single(detail => detail.Path == path);

    [Test]
    public async Task Get_NeverSaved_ReturnsDisabledGmailWithEverythingBlank()
    {
        // Act
        var settings = await GetAsync();

        // Assert
        Assert.That(settings, Is.EqualTo(new EmailSettings(
            false, EmailProvider.Gmail, null, null, null, null, false, null, null, null, new EmailDeliveryStatus(null, null, null), null, null)));
    }

    [Test]
    public async Task EveryOperation_NotAnAdmin_IsForbidden()
    {
        // Act
        var get = await Host.EmailSettings.GetAsync(Member, CancellationToken.None);
        var save = await Host.EmailSettings.SaveAsync(Member, GmailInput(), CancellationToken.None);
        var test = await Host.EmailSettings.SendTestAsync(Member, GmailInput(), "me@example.com", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(get.Failure?.Kind, Is.EqualTo(FailureKind.Forbidden));
            Assert.That(get.Failure?.Code, Is.EqualTo(NotificationErrorCodes.EmailForbidden));
            Assert.That(save.Failure?.Code, Is.EqualTo(NotificationErrorCodes.EmailForbidden));
            Assert.That(test.Failure?.Code, Is.EqualTo(NotificationErrorCodes.EmailForbidden));
            Assert.That(Transport.Connections, Is.Empty);
        }
    }

    [Test]
    public async Task Save_Gmail_StoresTheSettingsWithoutRevealingThePassword()
    {
        // Arrange
        Clock.Advance(TimeSpan.FromHours(1));

        // Act
        var saved = await Host.EmailSettings.SaveAsync(Admin, GmailInput(), CancellationToken.None);
        var read = await GetAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(saved.Value, Is.EqualTo(read));
            Assert.That(read.Enabled, Is.True);
            Assert.That(read.Provider, Is.EqualTo(EmailProvider.Gmail));
            Assert.That(read.Username, Is.EqualTo("team@example.com"));
            Assert.That(read.HasPassword, Is.True);
            Assert.That(read.FromName, Is.EqualTo("Example 公司"));
            Assert.That(read.SiteUrl, Is.EqualTo(SiteUrl));
            Assert.That(read.UpdatedAt, Is.EqualTo(Clock.GetUtcNow()));
            Assert.That(read.UpdatedBy, Is.EqualTo(AdminId));
        }
    }

    [Test]
    public async Task Save_GmailWithOtherHostFields_IgnoresAndClearsThem()
    {
        // Arrange
        var input = GmailInput() with { Host = "smtp.example.com", Port = 25, Security = SmtpSecurity.None, FromAddress = "x@example.com" };

        // Act
        var read = (await Host.EmailSettings.SaveAsync(Admin, input, CancellationToken.None)).Value;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(read.Host, Is.Null);
            Assert.That(read.Port, Is.Null);
            Assert.That(read.Security, Is.Null);
            Assert.That(read.FromAddress, Is.Null);
        }
    }

    [Test]
    public async Task Save_Smtp_StoresEveryField()
    {
        // Act
        var read = (await Host.EmailSettings.SaveAsync(Admin, SmtpInput(), CancellationToken.None)).Value;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(read.Provider, Is.EqualTo(EmailProvider.Smtp));
            Assert.That(read.Host, Is.EqualTo("smtp.example.com"));
            Assert.That(read.Port, Is.EqualTo(587));
            Assert.That(read.Security, Is.EqualTo(SmtpSecurity.StartTls));
            Assert.That(read.Username, Is.EqualTo("mailer"));
            Assert.That(read.FromAddress, Is.EqualTo("noreply@example.com"));
        }
    }

    [Test]
    public async Task Save_PasswordNull_KeepsTheStoredPassword()
    {
        // Arrange
        await EnableEmailAsync(SmtpInput(password: "first-secret"));

        // Act：改別的欄位，密碼不動。
        var read = (await Host.EmailSettings.SaveAsync(Admin, SmtpInput() with { Password = null, FromName = "新名稱" }, CancellationToken.None)).Value;
        await Host.EmailSettings.SendTestAsync(Admin, SmtpInput() with { Password = null }, "me@example.com", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(read.HasPassword, Is.True);
            Assert.That(read.FromName, Is.EqualTo("新名稱"));
            Assert.That(Transport.Connections.Single().Password, Is.EqualTo("first-secret"));
        }
    }

    [Test]
    public async Task Save_NewPassword_ReplacesTheStoredOne()
    {
        // Arrange
        await EnableEmailAsync(SmtpInput(password: "first-secret"));

        // Act
        await Host.EmailSettings.SaveAsync(Admin, SmtpInput(password: "second-secret"), CancellationToken.None);
        await Host.EmailSettings.SendTestAsync(Admin, SmtpInput() with { Password = null }, "me@example.com", CancellationToken.None);

        // Assert
        Assert.That(Transport.Connections.Single().Password, Is.EqualTo("second-secret"));
    }

    [Test]
    public async Task Save_EmptyPassword_ClearsIt()
    {
        // Arrange
        await EnableEmailAsync(SmtpInput(enabled: false));

        // Act
        var read = (await Host.EmailSettings.SaveAsync(Admin, SmtpInput(enabled: false) with { Password = string.Empty }, CancellationToken.None)).Value;

        // Assert
        Assert.That(read.HasPassword, Is.False);
    }

    [Test]
    public async Task Save_SiteUrlWithTrailingSlashAndPadding_IsNormalized()
    {
        // Act
        var read = (await Host.EmailSettings.SaveAsync(
            Admin, GmailInput() with { SiteUrl = "  https://pm.example.com/  ", FromName = "  ", Username = " team@example.com " }, CancellationToken.None)).Value;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(read.SiteUrl, Is.EqualTo("https://pm.example.com"));
            Assert.That(read.FromName, Is.Null);
            Assert.That(read.Username, Is.EqualTo("team@example.com"));
        }
    }

    [Test]
    public async Task Save_Disabled_AllowsIncompleteFields()
    {
        // Act
        var result = await Host.EmailSettings.SaveAsync(
            Admin, new EmailSettingsInput(false, EmailProvider.Smtp, null, null, null, null, null, null, null, null), CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True);
    }

    [Test]
    public async Task Save_AfterTheFirstTime_KeepsTheDeliveryStatus()
    {
        // Arrange：寄過一封，狀況有記錄。
        var scene = NewItem();
        await EnableEmailAsync();
        await HandleAsync(scene.Item.Created(scene.People[1], assignee: scene.People[0]));
        PassTheDelay();
        await DispatchAsync();

        // Act
        var read = (await Host.EmailSettings.SaveAsync(Admin, GmailInput() with { FromName = "新名稱" }, CancellationToken.None)).Value;

        // Assert
        Assert.That(read.Status.LastSentAt, Is.Not.Null);
    }

    [TestCase(EmailProvider.Gmail, "username", NotificationErrorCodes.EmailFieldRequired)]
    [TestCase(EmailProvider.Gmail, "password", NotificationErrorCodes.EmailFieldRequired)]
    [TestCase(EmailProvider.Gmail, "siteUrl", NotificationErrorCodes.EmailFieldRequired)]
    [TestCase(EmailProvider.Smtp, "host", NotificationErrorCodes.EmailFieldRequired)]
    [TestCase(EmailProvider.Smtp, "port", NotificationErrorCodes.EmailFieldRequired)]
    [TestCase(EmailProvider.Smtp, "security", NotificationErrorCodes.EmailFieldRequired)]
    [TestCase(EmailProvider.Smtp, "fromAddress", NotificationErrorCodes.EmailFieldRequired)]
    [TestCase(EmailProvider.Smtp, "siteUrl", NotificationErrorCodes.EmailFieldRequired)]
    public async Task Save_EnabledWithAMissingField_ReportsItAtThatPath(EmailProvider provider, string path, string code)
    {
        // Arrange
        var complete = provider == EmailProvider.Gmail ? GmailInput() : SmtpInput();
        var input = path switch
        {
            "username" => complete with { Username = " " },
            "password" => complete with { Password = string.Empty },
            "siteUrl" => complete with { SiteUrl = null },
            "host" => complete with { Host = null },
            "port" => complete with { Port = null },
            "security" => complete with { Security = null },
            _ => complete with { FromAddress = null },
        };

        // Act
        var failure = await SaveInvalidAsync(input);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure.Kind, Is.EqualTo(FailureKind.Invalid));
            Assert.That(failure.Code, Is.EqualTo(NotificationErrorCodes.EmailSettingsInvalid));
            Assert.That(Detail(failure, path).Code, Is.EqualTo(code));
            Assert.That(failure.Details, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task Save_SeveralBadFields_ReportsAllOfThemAtOnce()
    {
        // Act
        var failure = await SaveInvalidAsync(SmtpInput() with { Host = null, Port = 22, FromAddress = "not-an-address" });

        // Assert
        Assert.That(failure.Details.Select(d => d.Path), Is.EquivalentTo(new[] { "host", "port", "fromAddress" }));
    }

    [TestCase(22)]
    [TestCase(143)]
    [TestCase(8025)]
    [TestCase(0)]
    public async Task Save_PortOutsideTheAllowedList_IsRejected(int port)
    {
        // Act
        var failure = await SaveInvalidAsync(SmtpInput() with { Port = port });

        // Assert
        Assert.That(Detail(failure, "port").Code, Is.EqualTo(NotificationErrorCodes.EmailPortNotAllowed));
    }

    [TestCase(25)]
    [TestCase(465)]
    [TestCase(587)]
    [TestCase(2525)]
    public async Task Save_AllowedPort_IsAccepted(int port)
    {
        // Act
        var result = await Host.EmailSettings.SaveAsync(Admin, SmtpInput() with { Port = port, Security = SmtpSecurity.Tls }, CancellationToken.None);

        // Assert
        Assert.That(result.Value.Port, Is.EqualTo(port));
    }

    [Test]
    public async Task Save_UnencryptedWithCredentials_IsRejectedAtSecurity()
    {
        // Act
        var failure = await SaveInvalidAsync(SmtpInput() with { Security = SmtpSecurity.None, Port = 25 });

        // Assert
        Assert.That(Detail(failure, "security").Code, Is.EqualTo(NotificationErrorCodes.EmailInsecureCredentials));
    }

    [Test]
    public async Task Save_UnencryptedWithAStoredPasswordKept_IsRejected()
    {
        // Arrange：已經存了密碼，改成不加密又沒清掉密碼，等於會用明文送。
        await EnableEmailAsync(SmtpInput(password: "secret") with { Username = null });

        // Act
        var failure = await SaveInvalidAsync(SmtpInput() with { Username = null, Password = null, Security = SmtpSecurity.None, Port = 25 });

        // Assert
        Assert.That(Detail(failure, "security").Code, Is.EqualTo(NotificationErrorCodes.EmailInsecureCredentials));
    }

    [Test]
    public async Task Save_UnencryptedWithoutCredentials_IsAccepted()
    {
        // Act
        var result = await Host.EmailSettings.SaveAsync(
            Admin, SmtpInput() with { Security = SmtpSecurity.None, Port = 25, Username = null, Password = string.Empty }, CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True);
    }

    [TestCase("smtp.example.com", true)]
    [TestCase("192.168.1.10", true)]
    [TestCase("mail-1.example.com", true)]
    [TestCase("smtp example.com", false)]
    [TestCase("smtp.example.com/path", false)]
    [TestCase("smtp.example.com:25", false)]
    [TestCase("-bad.example.com", false)]
    [TestCase("bad.example.com.", false)]
    [TestCase("[::1]", false)]
    [TestCase("smtp@example.com", false)]
    public async Task Save_Host_OnlyHostNamesAndIpv4(string host, bool accepted)
    {
        // Act
        var result = await Host.EmailSettings.SaveAsync(Admin, SmtpInput() with { Host = host }, CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.EqualTo(accepted));
    }

    [TestCase("https://pm.example.com", true)]
    [TestCase("http://localhost:8080", true)]
    [TestCase("https://example.com/pm/", true)]
    [TestCase("ftp://pm.example.com", false)]
    [TestCase("pm.example.com", false)]
    [TestCase("https://user:pass@pm.example.com", false)]
    [TestCase("https://pm.example.com?x=1", false)]
    [TestCase("https://pm.example.com/#top", false)]
    [TestCase("javascript:alert(1)", false)]
    public async Task Save_SiteUrl_OnlyAbsoluteHttpWithoutCredentialsQueryOrFragment(string url, bool accepted)
    {
        // Act
        var result = await Host.EmailSettings.SaveAsync(Admin, GmailInput() with { SiteUrl = url }, CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.EqualTo(accepted));
    }

    [TestCase("username", "a\0b@example.com")]
    [TestCase("fromName", "Example\r\nBcc: x@example.com")]
    [TestCase("siteUrl", "https://pm.example.com/\0")]
    [TestCase("host", "smtp\n.example.com")]
    [TestCase("password", "secret\0")]
    [TestCase("fromAddress", "a@example.com\u0001")]
    public async Task Save_ControlCharacters_AreRejectedAtThatField(string path, string value)
    {
        // Arrange
        var input = SmtpInput();
        input = path switch
        {
            "username" => input with { Username = value },
            "fromName" => input with { FromName = value },
            "siteUrl" => input with { SiteUrl = value },
            "host" => input with { Host = value },
            "password" => input with { Password = value },
            _ => input with { FromAddress = value },
        };

        // Act
        var failure = await SaveInvalidAsync(input);

        // Assert
        Assert.That(failure.Details, Has.Some.Matches<FailureDetail>(d => d.Path == path && d.Code == NotificationErrorCodes.EmailFieldInvalid));
    }

    [Test]
    public async Task Save_TooLongFields_AreRejected()
    {
        // Act
        var failure = await SaveInvalidAsync(SmtpInput() with
        {
            Host = new string('a', 254),
            Username = new string('u', 255),
            Password = new string('p', 257),
            FromName = new string('n', 101),
            SiteUrl = "https://" + new string('s', 200) + ".example.com",
        });

        // Assert
        Assert.That(
            failure.Details.Select(d => d.Path),
            Is.EquivalentTo(new[] { "host", "username", "password", "fromName", "siteUrl" }));
    }

    [Test]
    public async Task Save_GmailUsernameThatIsNotAnEmail_IsRejected()
    {
        // Act
        var failure = await SaveInvalidAsync(GmailInput() with { Username = "not-an-email" });

        // Assert
        Assert.That(Detail(failure, "username").Code, Is.EqualTo(NotificationErrorCodes.EmailFieldInvalid));
    }

    [Test]
    public async Task SendTest_ValidSettings_SendsTheTestMailWithTheUnsavedSettingsAndLeavesTheStoredOnesAlone()
    {
        // Arrange
        await EnableEmailAsync(SmtpInput(password: "stored-secret"));

        // Act：畫面上還沒存的設定（Gmail、沒有開）。
        var result = await Host.EmailSettings.SendTestAsync(Admin, GmailInput(enabled: false), "admin@example.com", CancellationToken.None);
        var stored = await GetAsync();

        // Assert
        var mail = Transport.Sent.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.True);
            Assert.That(mail.ToAddress, Is.EqualTo("admin@example.com"));
            Assert.That(mail.Subject, Is.EqualTo("測試信：Email 通知的設定可以用了"));
            Assert.That(Transport.Connections.Single().Host, Is.EqualTo("smtp.gmail.com"));
            Assert.That(stored.Provider, Is.EqualTo(EmailProvider.Smtp));
            Assert.That(stored.Status.LastSentAt, Is.Null);
        }
    }

    [Test]
    public async Task SendTest_IncompleteFieldsEvenWhenDisabled_AreRejectedWithoutConnecting()
    {
        // Act
        var result = await Host.EmailSettings.SendTestAsync(
            Admin, GmailInput(enabled: false) with { Password = string.Empty }, "admin@example.com", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure?.Code, Is.EqualTo(NotificationErrorCodes.EmailSettingsInvalid));
            Assert.That(result.Failure!.Details.Single().Path, Is.EqualTo("password"));
            Assert.That(Transport.Connections, Is.Empty);
        }
    }

    [Test]
    public async Task SendTest_ServerRefuses_ReturnsTheReasonAndDoesNotTouchTheStatus()
    {
        // Arrange
        Transport.OnOpen = _ => new Notifications.Email.EmailDeliveryException(
            Notifications.Email.EmailFailureKind.AuthenticationFailed, "帳號或密碼不對", "535 5.7.8 Bad credentials");

        // Act
        var result = await Host.EmailSettings.SendTestAsync(Admin, GmailInput(), "admin@example.com", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure?.Kind, Is.EqualTo(FailureKind.Invalid));
            Assert.That(result.Failure?.Code, Is.EqualTo(NotificationErrorCodes.EmailSendFailed));
            Assert.That(result.Failure?.Message, Is.EqualTo("帳號或密碼不對（535 5.7.8 Bad credentials）"));
            Assert.That((await GetAsync()).Status.LastFailedAt, Is.Null);
        }
    }

    [TestCase("provider")]
    [TestCase("host")]
    [TestCase("port")]
    [TestCase("username")]
    public async Task Save_KeepingThePasswordWhileChangingTheDestination_IsRejectedAtPassword(string changed)
    {
        // Arrange：存了 SMTP 的密碼，再改成指到別的地方、密碼留白。
        await EnableEmailAsync(SmtpInput(password: "stored-secret"));
        var input = changed switch
        {
            "provider" => GmailInput() with { Password = null },
            "host" => SmtpInput() with { Password = null, Host = "attacker.example.net" },
            "port" => SmtpInput() with { Password = null, Port = 465, Security = SmtpSecurity.Tls },
            _ => SmtpInput() with { Password = null, Username = "someone-else" },
        };

        // Act
        var failure = await SaveInvalidAsync(input);
        var testResult = await Host.EmailSettings.SendTestAsync(Admin, input, "me@example.com", CancellationToken.None);

        // Assert：存的密碼不會被送到新的目的地，儲存與寄測試信都一樣。
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Detail(failure, "password").Code, Is.EqualTo(NotificationErrorCodes.EmailFieldRequired));
            Assert.That(Detail(failure, "password").Message, Is.EqualTo("改了寄信服務、主機、連接埠或帳號，要重新輸入密碼。"));
            Assert.That(testResult.Failure?.Code, Is.EqualTo(NotificationErrorCodes.EmailSettingsInvalid));
            Assert.That(Transport.Connections, Is.Empty);
            Assert.That((await GetAsync()).Host, Is.EqualTo("smtp.example.com"));
        }
    }

    [Test]
    public async Task Save_ChangingTheDestinationWithANewPassword_IsAccepted()
    {
        // Arrange
        await EnableEmailAsync(SmtpInput(password: "stored-secret"));

        // Act
        var result = await Host.EmailSettings.SaveAsync(
            Admin, SmtpInput(password: "new-secret") with { Host = "other.example.com" }, CancellationToken.None);

        // Assert
        Assert.That(result.Value.Host, Is.EqualTo("other.example.com"));
    }

    [Test]
    public async Task Save_KeepingThePasswordAndSameDestinationWithOtherFieldsChanged_IsAccepted()
    {
        // Arrange：主機的大小寫、加密方式、寄件人名稱不算換目的地。
        await EnableEmailAsync(SmtpInput(password: "stored-secret"));

        // Act
        var result = await Host.EmailSettings.SaveAsync(
            Admin, SmtpInput() with { Password = null, Host = "SMTP.Example.com", Security = SmtpSecurity.Tls, FromName = "別的名稱" }, CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True);
    }

    [Test]
    public async Task Save_FirstTimeWithoutAPassword_CanPointAnywhere()
    {
        // Act：沒有存過密碼，沒有東西會被送走。
        var result = await Host.EmailSettings.SaveAsync(
            Admin, SmtpInput() with { Password = null, Username = null, Host = "relay.example.com" }, CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True);
    }

    [TestCase("a<b@example.com")]
    [TestCase("a>b@example.com")]
    [TestCase("a,b@example.com")]
    [TestCase("a;b@example.com")]
    [TestCase("\"a\"@example.com")]
    [TestCase("a(b)@example.com")]
    [TestCase("a[b]@example.com")]
    [TestCase("a\\b@example.com")]
    [TestCase("a b@example.com")]
    public async Task Save_AddressWithSyntaxCharacters_IsRejected(string address)
    {
        // Act
        var failure = await SaveInvalidAsync(SmtpInput() with { FromAddress = address });

        // Assert
        Assert.That(Detail(failure, "fromAddress").Code, Is.EqualTo(NotificationErrorCodes.EmailFieldInvalid));
    }

    [Test]
    public void ToString_OfTheInput_NeverShowsThePassword()
    {
        // Act
        var text = SmtpInput(password: "very-secret-value").ToString();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(text, Does.Not.Contain("very-secret-value"));
            Assert.That(text, Does.Contain("***").And.Contain("smtp.example.com"));
            Assert.That(SmtpInput() with { Password = null }, Has.Property("Password").Null);
        }
    }
}
