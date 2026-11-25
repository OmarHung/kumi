using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using PmTool.Api.Composition;

namespace PmTool.Api.Security;

/// <summary>金鑰有沒有用憑證加密存放。啟動時看它決定要不要記一筆警告。</summary>
internal sealed record KeyProtectionStatus(bool IsEncrypted);

/// <summary>
/// 工作階段 Cookie 的加密金鑰存在資料庫裡（身分模組）；設了憑證時先用憑證加密再存，
/// 資料庫的備份或資料表外流時，拿到的金鑰解不開，偽造不了 Cookie。
/// </summary>
internal static partial class KeyProtection
{
    public const string CertificatePathSetting = "DataProtection:CertificatePath";
    public const string CertificatePasswordSetting = "DataProtection:CertificatePassword";
    public const string RequireCertificateSetting = "DataProtection:RequireCertificate";
    public const string PreviousCertificatePathsSetting = "DataProtection:PreviousCertificatePaths";

    /// <summary>
    /// 照設定加上憑證。換憑證時舊的放在 <see cref="PreviousCertificatePathsSetting"/>，用它加密的舊金鑰還解得開；
    /// 新的金鑰一律用目前的憑證加密。讀不到憑證、密碼錯或沒有私鑰時讓啟動失敗：
    /// 帶著解不開的金鑰上線，每個人都會被登出，而且看不出原因。
    /// </summary>
    public static IDataProtectionBuilder ProtectKeysFromConfiguration(this IDataProtectionBuilder builder, IConfiguration configuration)
    {
        var path = SettingValues.ReadText(configuration, CertificatePathSetting);

        // 憑證有沒有設都要讀：值不是布林時啟動就失敗，不要等到沒設憑證才發現。
        var requireCertificate = SettingValues.ReadFlag(configuration, RequireCertificateSetting, defaultValue: false);
        var previousPaths = SettingValues.ReadList(configuration, PreviousCertificatePathsSetting);
        builder.Services.AddSingleton(new KeyProtectionStatus(path is not null));
        if (path is null)
        {
            if (requireCertificate)
            {
                throw new InvalidOperationException(
                    $"{RequireCertificateSetting} 是 true，卻沒有設定 {CertificatePathSetting}：金鑰會以明文存在資料庫裡。設定憑證，或把 {RequireCertificateSetting} 拿掉。");
            }

            return previousPaths.Count == 0
                ? builder
                : throw new InvalidOperationException(
                    $"設了 {PreviousCertificatePathsSetting}，卻沒有設定目前的憑證（{CertificatePathSetting}）。");
        }

        // 密碼不去掉空白：空白也可能是密碼的一部分。
        var password = configuration[CertificatePasswordSetting];
        var current = Load(path, password, CertificatePathSetting);
        var previous = previousPaths.Select(previousPath => Load(previousPath, password, PreviousCertificatePathsSetting));
        return builder.ProtectKeysWithCertificate(current).UnprotectKeysWithAnyCertificate([current, .. previous]);
    }

    /// <summary>正式環境沒設憑證時記一筆警告：照樣能用，只是金鑰以明文存在資料庫裡。</summary>
    public static void WarnIfKeysAreNotEncrypted(this WebApplication app)
    {
        if (!app.Environment.IsProduction() || app.Services.GetRequiredService<KeyProtectionStatus>().IsEncrypted)
        {
            return;
        }

        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(KeyProtection).FullName!);
        LogKeysNotEncrypted(logger, CertificatePathSetting, RequireCertificateSetting);
    }

    private static X509Certificate2 Load(string path, string? password, string setting)
    {
        X509Certificate2 certificate;
        try
        {
            certificate = X509CertificateLoader.LoadPkcs12FromFile(path, password);
        }
        catch (Exception exception) when (exception is CryptographicException or IOException or UnauthorizedAccessException)
        {
            // 訊息不帶密碼，只帶路徑：啟動失敗的訊息會進記錄。
            throw new InvalidOperationException(
                $"讀不到 {setting} 指定的憑證（{path}）：檔案不存在、沒有讀取權限、不是 PFX，或密碼（{CertificatePasswordSetting}）不對。",
                exception);
        }

        if (certificate.HasPrivateKey)
        {
            return certificate;
        }

        certificate.Dispose();
        throw new InvalidOperationException($"{setting} 指定的憑證（{path}）沒有私鑰，解不開用它加密的金鑰。");
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "沒有設定 {Setting}：Data Protection 的金鑰以明文存在資料庫裡，AI 供應商的金鑰與寄信密碼等於明文，拿到資料庫或備份的人解得開；正式環境請設定憑證，並把 {RequireSetting} 設成 true。")]
    private static partial void LogKeysNotEncrypted(ILogger logger, string setting, string requireSetting);
}
