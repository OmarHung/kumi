using System.Globalization;

namespace PmTool.Ai.Assistant;

/// <summary>
/// 這則回覆開始時的「現在」，照組織的時區。系統提示刻意不放日期（提示快取），所以寫在專案說明那一則 system 訊息裡；
/// 模型不知道今天幾號，就會排出已經過去的到期日。
/// </summary>
internal sealed record CurrentTime(DateTimeOffset Utc, TimeZoneInfo Zone)
{
    private const string Weekdays = "日一二三四五六";

    public DateTimeOffset Local => TimeZoneInfo.ConvertTime(Utc, Zone);

    public string TimeZoneId => Zone.Id;

    /// <summary>「2026-11-27（星期五）07:51」，到分鐘。</summary>
    public string Describe() => Describe(Local);

    /// <summary>某個時刻在這個時區的寫法；和「現在」同一天只寫時間，其他天加上月日。</summary>
    public string DescribeShort(DateTimeOffset moment)
    {
        var local = TimeZoneInfo.ConvertTime(moment, Zone);
        return local.Date == Local.Date
            ? local.ToString("HH:mm", CultureInfo.InvariantCulture)
            : local.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    private static string Describe(DateTimeOffset local) =>
        $"{local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}（星期{Weekdays[(int)local.DayOfWeek]}）{local.ToString("HH:mm", CultureInfo.InvariantCulture)}";
}
