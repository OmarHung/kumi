using System.Runtime.InteropServices;

namespace PmTool.Cli.Settings;

/// <summary>
/// 把 Ctrl-C 與「要求結束」（SIGTERM）接給長駐的指令。第一次只通知，讓它收尾（正在跑的工作送出結束）；
/// 第二次 Ctrl-C 照預設直接結束程序，收尾卡住時使用者還是有辦法離開。接的是真的終端機與訊號，沒辦法在測試裡接。
/// </summary>
internal static class ConsoleInterrupt
{
    public static IDisposable Register(Action onInterrupt)
    {
        var requested = 0;
        void Request()
        {
            if (Interlocked.Exchange(ref requested, 1) == 0)
            {
                onInterrupt();
            }
        }

        ConsoleCancelEventHandler handler = (_, eventArgs) =>
        {
            // 第一次攔下來讓收尾；第二次不攔，程序直接結束。
            eventArgs.Cancel = Volatile.Read(ref requested) == 0;
            Request();
        };
        Console.CancelKeyPress += handler;
        // SIGTERM（被要求結束）與 SIGHUP（終端機關掉）：第一次收尾；收尾卡住時第二次不攔，直接結束。
        void OnSignal(PosixSignalContext context)
        {
            context.Cancel = Volatile.Read(ref requested) == 0;
            Request();
        }

        var termination = PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnSignal);
        var hangup = PosixSignalRegistration.Create(PosixSignal.SIGHUP, OnSignal);
        return new Registration(() =>
        {
            Console.CancelKeyPress -= handler;
            termination.Dispose();
            hangup.Dispose();
        });
    }

    private sealed class Registration(Action release) : IDisposable
    {
        public void Dispose() => release();
    }
}
