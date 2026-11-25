using System.Net;
using System.Net.Sockets;
using PmTool.Cli.Login;

namespace PmTool.Cli.Tests.Login;

[TestFixture]
public sealed class LoopbackReceiverPortTests
{
    private static int PortOf(TcpListener listener) => ((IPEndPoint)listener.LocalEndpoint).Port;

    /// <summary>找一個現在沒人用、比伺服器收的下限還小的連接埠，當成「作業系統配了比較小的」。</summary>
    private static TcpListener ListenerOnALowPort()
    {
        for (var port = 20000; port < 20200; port++)
        {
            var probe = new TcpListener(IPAddress.Loopback, port);
            try
            {
                probe.Start();
                probe.Stop();
                return new TcpListener(IPAddress.Loopback, port);
            }
            catch (SocketException)
            {
                probe.Dispose();
            }
        }

        throw new InvalidOperationException("找不到可以用的測試連接埠。");
    }

    [Test]
    public void Start_PortFromTheSystem_IsAtLeastTheServerMinimum()
    {
        // Arrange
        using var receiver = new LoopbackReceiver("state-value-from-the-test");

        // Act
        receiver.Start();

        // Assert
        Assert.That(receiver.Port, Is.GreaterThanOrEqualTo(LoopbackReceiver.MinPort));
    }

    [Test]
    public void StartOnAllowedPort_FirstPortTooLow_MovesToAPickedHighPort()
    {
        // Arrange
        var low = ListenerOnALowPort();
        using var free = new TcpListener(IPAddress.Loopback, 0);
        free.Start();
        var highPort = PortOf(free);
        free.Stop();

        // Act
        using var listener = LoopbackReceiver.StartOnAllowedPort(low, port => new TcpListener(IPAddress.Loopback, port), () => highPort);

        // Assert
        Assert.That(PortOf(listener), Is.EqualTo(highPort));
    }

    [Test]
    public void StartOnAllowedPort_PickedPortIsTaken_TriesTheNextOne()
    {
        // Arrange
        var low = ListenerOnALowPort();
        using var taken = new TcpListener(IPAddress.Loopback, 0);
        taken.Start();
        using var free = new TcpListener(IPAddress.Loopback, 0);
        free.Start();
        var freePort = PortOf(free);
        free.Stop();
        var picks = new Queue<int>([PortOf(taken), freePort]);

        // Act
        using var listener = LoopbackReceiver.StartOnAllowedPort(low, port => new TcpListener(IPAddress.Loopback, port), picks.Dequeue);

        // Assert
        Assert.That(PortOf(listener), Is.EqualTo(freePort));
    }

    [Test]
    public void StartOnAllowedPort_EveryPickedPortIsTaken_ThrowsASocketException()
    {
        // Arrange
        var low = ListenerOnALowPort();
        using var taken = new TcpListener(IPAddress.Loopback, 0);
        taken.Start();
        var takenPort = PortOf(taken);

        // Act
        void Act() => LoopbackReceiver.StartOnAllowedPort(low, port => new TcpListener(IPAddress.Loopback, port), () => takenPort).Dispose();

        // Assert
        Assert.That(Act, Throws.InstanceOf<SocketException>());
    }
}
