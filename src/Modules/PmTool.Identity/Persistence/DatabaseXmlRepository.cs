using System.Xml;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace PmTool.Identity.Persistence;

/// <summary>
/// 把 Data Protection 的金鑰存在 <c>identity.data_protection_keys</c>。介面是同步的，所以用同步的 EF 呼叫；
/// 金鑰環會快取，只有啟動、定期更新、遇到不認得的金鑰時才讀，不在每個請求的路徑上。
/// </summary>
internal sealed partial class DatabaseXmlRepository(
    IDbContextFactory<IdentityDbContext> contexts, TimeProvider time, ILogger<DatabaseXmlRepository> logger) : IXmlRepository
{
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var db = contexts.CreateDbContext();
        var stored = db.DataProtectionKeys.AsNoTracking().OrderBy(key => key.Id).Select(key => new { key.Id, key.Xml }).ToList();
        var elements = new List<XElement>(stored.Count);
        foreach (var row in stored)
        {
            try
            {
                elements.Add(XElement.Parse(row.Xml));
            }
            catch (XmlException)
            {
                // 一列壞掉不能讓每個帶 Cookie 的請求都 500：略過它，其他金鑰照用。只記編號，XML 的內容是金鑰，不進記錄。
                LogUnreadableKey(logger, row.Id);
            }
        }

        return elements.AsReadOnly();
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        ArgumentNullException.ThrowIfNull(element);

        // 只新增、不更新：Data Protection 撤銷金鑰也是另外寫一筆，不改舊的。
        using var db = contexts.CreateDbContext();
        db.DataProtectionKeys.Add(new DataProtectionKeyRow
        {
            // 欄位不接受 null；介面的參數理論上可能是 null。
            FriendlyName = friendlyName ?? $"key-{Guid.NewGuid():N}",
            Xml = element.ToString(SaveOptions.DisableFormatting),
            CreatedAt = time.GetUtcNow(),
        });
        db.SaveChanges();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "identity.data_protection_keys 的第 {KeyId} 列不是合法的 XML，略過這一把金鑰。")]
    private static partial void LogUnreadableKey(ILogger logger, int keyId);
}
