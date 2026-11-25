using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace PmTool.Api.Endpoints;

/// <summary>
/// 網頁回報的錯誤去重：同一個人、同一個「種類加訊息」，一個視窗內只記一次。壞掉的畫面會一直丟同一個例外，
/// 全部寫進記錄只會把有用的東西擠掉。記在記憶體裡、不用計時器：每次進來才順便清掉過期的，
/// 鍵用雜湊（不留下訊息的內容），上限 <see cref="ApiLimits.ClientErrorDedupeMaxKeys"/> 個。
/// </summary>
internal sealed class ClientErrorDeduplicator(TimeProvider time)
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(ApiLimits.ClientErrorDedupeWindowSeconds);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _seen = new(StringComparer.Ordinal);

    /// <summary>這一則該不該記。第一次看到（或上次記的已經過了視窗）回 true 並記下來；視窗內重複的回 false。</summary>
    public bool ShouldLog(Guid personId, string kind, string message)
    {
        var now = time.GetUtcNow();
        var key = KeyOf(personId, kind, message);
        if (_seen.TryGetValue(key, out var last) && now >= last && now - last < Window)
        {
            return false;
        }

        if (!_seen.ContainsKey(key) && _seen.Count >= ApiLimits.ClientErrorDedupeMaxKeys)
        {
            RemoveExpired(now);
            if (_seen.Count >= ApiLimits.ClientErrorDedupeMaxKeys)
            {
                // 滿了又沒有過期的：寧可多記一筆，也不讓記憶體長大。
                return true;
            }
        }

        _seen[key] = now;
        return true;
    }

    private void RemoveExpired(DateTimeOffset now)
    {
        foreach (var pair in _seen)
        {
            if (now - pair.Value >= Window || pair.Value > now)
            {
                _seen.TryRemove(pair);
            }
        }
    }

    private static string KeyOf(Guid personId, string kind, string message)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{personId:N}\n{kind}\n{message}"));
        return Convert.ToHexString(bytes);
    }
}
