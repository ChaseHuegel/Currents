using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Currents.Protocol;

namespace Currents.Tests.Protocol;

public class SpeedTests
{
    [Test]
    [Repeat(10)]
    [Timeout(5000)]
    public void CRNT_Connect_Insecure_IPv4()
    {
        using var client = new Connector(new IPEndPoint(IPAddress.Any, 0));
        using var server = new Connector(new IPEndPoint(IPAddress.Any, 4321));

        Task.Run(AcceptConnection);
        Task AcceptConnection()
        {
            server.Accept();
            return Task.CompletedTask;
        }

        Stopwatch sw = Stopwatch.StartNew();
        bool connected = client.Connect(new IPEndPoint(IPAddress.Loopback, 4321));
        sw.Stop();

        Console.WriteLine($"CRNT took {sw.ElapsedMilliseconds}ms to connect.");
        Assert.That(connected, Is.True);
    }

    [Test]
    [Repeat(10)]
    [Timeout(5000)]
    public void TCP_Connect_Insecure_IPv4()
    {
        using var client = new TcpClient(AddressFamily.InterNetwork);
        using var server = new TcpListener(IPAddress.Any, 4321);

        Task.Run(AcceptConnection);
        Task AcceptConnection()
        {
            server.Start();
            server.AcceptTcpClient();
            return Task.CompletedTask;
        }

        Stopwatch sw = Stopwatch.StartNew();
        client.Connect(IPAddress.Loopback, 4321);
        sw.Stop();

        Console.WriteLine($"TCP took {sw.ElapsedMilliseconds}ms to connect.");
        Assert.That(client.Connected, Is.True);
    }
}