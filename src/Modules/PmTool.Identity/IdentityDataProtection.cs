using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PmTool.Identity.Persistence;

namespace PmTool.Identity;

public static class IdentityDataProtection
{
    /// <summary>
    /// 把 Data Protection 的金鑰存在身分模組的資料庫（<c>identity.data_protection_keys</c>）。
    /// 容器沒有可以保存的磁碟（例如 Cloud Run），金鑰放在容器裡的話，每個新容器各自產生金鑰，
    /// 彼此解不開對方的 Cookie，使用者會一直被登出。要先呼叫 <see cref="IdentityModule.AddIdentityModule(IServiceCollection, PmTool.Abstractions.DatabaseConnections)"/>。
    /// </summary>
    public static IDataProtectionBuilder PersistKeysToIdentityModule(this IDataProtectionBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // 和 PersistKeysToFileSystem 一樣在建立選項時才拿依賴：模組的 DbContext 工廠可能在這之後才註冊。
        builder.Services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(provider =>
            new ConfigureOptions<KeyManagementOptions>(options => options.XmlRepository = new DatabaseXmlRepository(
                provider.GetRequiredService<IDbContextFactory<IdentityDbContext>>(),
                provider.GetService<TimeProvider>() ?? TimeProvider.System,
                provider.GetService<ILogger<DatabaseXmlRepository>>() ?? NullLogger<DatabaseXmlRepository>.Instance)));
        return builder;
    }
}
