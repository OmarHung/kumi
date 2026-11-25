namespace PmTool.Cli.Host;

/// <summary>
/// 機器憑證的每個請求都帶機器編號：伺服器在認證時比對配對時存的編號，憑證被拿到別台電腦時，
/// 不只是登記，連領登入碼、回報、結束都會被拒絕並警告擁有者。
/// </summary>
internal static class HostMachineHeader
{
    public const string Name = "X-Kumi-Machine";

    public static IReadOnlyDictionary<string, string> For(string machineKey) =>
        new Dictionary<string, string> { [Name] = machineKey };
}
