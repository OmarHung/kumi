using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using PmTool.Api.Composition;
using IPNetwork = System.Net.IPNetwork;

namespace PmTool.Api.Security;

/// <summary>
/// 放在反向代理或平台的前端（Cloud Run、負載平衡器）後面時，讀它轉送的用戶端 IP 與 scheme。
/// 預設不讀：直接連進來的請求可以自己帶 X-Forwarded-For，信了就能冒用任何 IP，限流形同虛設。
/// </summary>
/// <param name="Enabled">要不要讀轉送的標頭。</param>
/// <param name="TrustAllProxies">信任任何來源送的標頭。只能用在外面只連得到平台前端的環境。</param>
/// <param name="KnownProxies">信任的代理 IP。</param>
/// <param name="KnownNetworks">信任的網段。</param>
/// <param name="ForwardLimit">要往回看幾層代理（前面有 CDN 加 nginx 之類好幾層時才要加大）。</param>
internal sealed record ProxySettings(
    bool Enabled,
    bool TrustAllProxies,
    IReadOnlyList<IPAddress> KnownProxies,
    IReadOnlyList<IPNetwork> KnownNetworks,
    int ForwardLimit = ProxySettings.DefaultForwardLimit)
{
    public const string EnabledSetting = "ForwardedHeaders:Enabled";
    public const string KnownProxiesSetting = "ForwardedHeaders:KnownProxies";
    public const string KnownNetworksSetting = "ForwardedHeaders:KnownNetworks";
    public const string TrustAllProxiesSetting = "ForwardedHeaders:TrustAllProxies";
    public const string ForwardLimitSetting = "ForwardedHeaders:ForwardLimit";

    /// <summary>預設只看最後一層：用戶端自己帶的 X-Forwarded-For 排在前面，信任的代理加上的才是最後一個。</summary>
    public const int DefaultForwardLimit = 1;

    /// <summary>上限：層數愈多，用戶端能自己偽造的部分愈多，十層已經遠超過實際的部署。</summary>
    public const int MaxForwardLimit = 10;

    public static readonly ProxySettings Disabled = new(false, false, [], []);

    /// <exception cref="InvalidOperationException">設定不合規定，訊息說得出是哪個設定。</exception>
    public static ProxySettings From(IConfiguration configuration)
    {
        // 沒打開時也檢查：設錯的值要在部署時就發現，不要等到哪天打開才爆。
        var forwardLimit = ReadForwardLimit(configuration);
        if (!SettingValues.ReadFlag(configuration, EnabledSetting, defaultValue: false))
        {
            return Disabled;
        }

        var trustAll = SettingValues.ReadFlag(configuration, TrustAllProxiesSetting, defaultValue: false);
        var proxies = SettingValues.ReadList(configuration, KnownProxiesSetting).Select(ParseProxy).ToArray();
        var networks = SettingValues.ReadList(configuration, KnownNetworksSetting).Select(ParseNetwork).ToArray();
        if (!trustAll && proxies.Length == 0 && networks.Length == 0)
        {
            // 什麼都不信任的話，中介軟體會把「沒有已知代理」當成「信任所有來源」，和設定的人想的正好相反。
            throw new InvalidOperationException(
                $"{EnabledSetting} 是 true，但沒有設定信任的代理：請設 {KnownProxiesSetting}、{KnownNetworksSetting}，"
                + $"或在外面只連得到平台前端的環境設 {TrustAllProxiesSetting}=true。");
        }

        return new ProxySettings(true, trustAll, proxies, networks, forwardLimit);
    }

    /// <summary>
    /// 預設的 KnownProxies 與 KnownIPNetworks 有本機位址，先清掉，只留設定的。
    /// 兩份清單都是空的時，中介軟體信任任何來源，這就是 <see cref="TrustAllProxies"/>。
    /// </summary>
    public void Apply(ForwardedHeadersOptions options)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = ForwardLimit;
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        if (TrustAllProxies)
        {
            return;
        }

        foreach (var proxy in KnownProxies)
        {
            options.KnownProxies.Add(proxy);
        }

        foreach (var network in KnownNetworks)
        {
            options.KnownIPNetworks.Add(network);
        }
    }

    private static int ReadForwardLimit(IConfiguration configuration)
    {
        var raw = SettingValues.ReadText(configuration, ForwardLimitSetting);
        if (raw is null)
        {
            return DefaultForwardLimit;
        }

        // NumberStyles.None：只收純數字，不收正負號、小數與千分位；太大的數字 TryParse 也會擋下。
        return int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value is >= 1 and <= MaxForwardLimit
            ? value
            : throw new InvalidOperationException($"{ForwardLimitSetting} 只能是 1 到 {MaxForwardLimit} 的整數，現在是「{raw}」。");
    }

    private static IPAddress ParseProxy(string value) =>
        IPAddress.TryParse(value, out var address)
            ? address
            : throw new InvalidOperationException($"{KnownProxiesSetting} 有一個不是 IP 位址的值：「{value}」。");

    /// <summary>
    /// 一定要寫成 CIDR，而且位址的主機部分是 0：10.0.0.1/8 這種寫法看不出本來想信任的是一台還是整個網段，
    /// IPNetwork 會默默把它當成 10.0.0.0/8，信任的範圍比寫的人以為的大。
    /// </summary>
    private static IPNetwork ParseNetwork(string value)
    {
        var slash = value.IndexOf('/', StringComparison.Ordinal);
        if (slash > 0
            && IPAddress.TryParse(value[..slash], out var address)
            && IPNetwork.TryParse(value, out var network)
            && network.BaseAddress.Equals(address))
        {
            return network;
        }

        throw new InvalidOperationException($"{KnownNetworksSetting} 有一個不是網段（CIDR，例如 10.0.0.0/8）的值：「{value}」。");
    }
}
