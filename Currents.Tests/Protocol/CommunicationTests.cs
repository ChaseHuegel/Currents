using System.Net;
using System.Text;
using Currents.Protocol;
using Microsoft.Extensions.Logging;

namespace Currents.Tests.Protocol;

public class CommunicationTests
{
    [Test]
    [Timeout(5000)]
    public void Client_Recv_String()
    {
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        ILogger clientLogger = loggerFactory.CreateLogger<ConnectionHandler>();
        ILogger serverLogger = loggerFactory.CreateLogger<ConnectionHandler>();
        using var meterFactory = new TestMeterFactory();
        var clientMetrics = new ConnectorMetrics(meterFactory);
        var serverMetrics = new ConnectorMetrics(meterFactory);

        using var client = new CrntConnector(new IPEndPoint(IPAddress.Any, 0), clientLogger, clientMetrics);
        using var server = new CrntConnector(new IPEndPoint(IPAddress.Any, 4321), serverLogger, serverMetrics);

        Task.Run(AcceptConnection);
        Task AcceptConnection()
        {
            Peer peer = server.Accept();

            byte[] buffer = Encoding.ASCII.GetBytes("Hello world!");
            peer.Send(buffer);

            return Task.CompletedTask;
        }

        bool connected = client.TryConnect(new IPEndPoint(IPAddress.Loopback, 4321), out Peer peer);
        Assert.That(connected, Is.True);

        byte[] buffer = peer.Consume();
        string str = Encoding.ASCII.GetString(buffer);
        Console.WriteLine($"Received data: {str}");
        Assert.That(str, Is.EqualTo("Hello world!"));
    }

    [Test]
    [Timeout(5000)]
    public void Server_Recv_String()
    {
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        ILogger clientLogger = loggerFactory.CreateLogger<ConnectionHandler>();
        ILogger serverLogger = loggerFactory.CreateLogger<ConnectionHandler>();
        using var meterFactory = new TestMeterFactory();
        var clientMetrics = new ConnectorMetrics(meterFactory);
        var serverMetrics = new ConnectorMetrics(meterFactory);

        using var client = new CrntConnector(new IPEndPoint(IPAddress.Any, 0), clientLogger, clientMetrics);
        using var server = new CrntConnector(new IPEndPoint(IPAddress.Any, 4321), serverLogger, serverMetrics);

        Task.Run(AcceptConnection);
        Task AcceptConnection()
        {
            bool connected = client.TryConnect(new IPEndPoint(IPAddress.Loopback, 4321), out Peer peer);
            Assert.That(connected, Is.True);

            byte[] buffer = Encoding.ASCII.GetBytes("Hello world!");
            peer.Send(buffer);

            return Task.CompletedTask;
        }

        Peer peer = server.Accept();

        byte[] buffer = peer.Consume();
        string str = Encoding.ASCII.GetString(buffer);
        Console.WriteLine($"Received data: {str}");
        Assert.That(str, Is.EqualTo("Hello world!"));
    }
}
