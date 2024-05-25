using System.Net;
using System.Text;
using Currents.IO;
using Currents.Metrics;
using Currents.Protocol;
using Microsoft.Extensions.Logging;

namespace Currents.Tests.Protocol;

public class CommunicationTests
{
    [Test]
    [Timeout(5000)]
    public void Client_Recv_String_InOrder()
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

            for (int i = 0; i < 10; i++)
            {
                byte[] buffer = Encoding.ASCII.GetBytes($"Hello world {i}!");
                peer.Send(buffer);
            }

            return Task.CompletedTask;
        }

        bool connected = client.TryConnect(new IPEndPoint(IPAddress.Loopback, 4321), out Peer peer);
        Assert.That(connected, Is.True);

        for (int i = 0; i < 10; i++)
        {
            byte[] buffer = peer.Consume();
            string str = Encoding.ASCII.GetString(buffer);
            Console.WriteLine($"Received data: {str}");
            Assert.That(str, Is.EqualTo($"Hello world {i}!"));
        }
    }

    [Test]
    [Timeout(5000)]
    public void Server_Recv_String_InOrder()
    {
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        ILogger clientLogger = loggerFactory.CreateLogger<ConnectionHandler>();
        ILogger serverLogger = loggerFactory.CreateLogger<ConnectionHandler>();
        using var meterFactory = new TestMeterFactory();
        var clientMetrics = new ConnectorMetrics(meterFactory);
        var serverMetrics = new ConnectorMetrics(meterFactory);

        using var client = new CrntConnector(new IPEndPoint(IPAddress.Any, 0), clientLogger, clientMetrics);
        using var server = new CrntConnector(new IPEndPoint(IPAddress.Any, 4321), serverLogger, serverMetrics);

        Task.Run(Connect);
        Task Connect()
        {
            bool connected = client.TryConnect(new IPEndPoint(IPAddress.Loopback, 4321), out Peer peer);
            Assert.That(connected, Is.True);

            for (int i = 0; i < 10; i++)
            {
                byte[] buffer = Encoding.ASCII.GetBytes($"Hello world {i}!");
                peer.Send(buffer);
            }

            return Task.CompletedTask;
        }

        Peer peer = server.Accept();

        for (int i = 0; i < 10; i++)
        {
            byte[] buffer = peer.Consume();
            string str = Encoding.ASCII.GetString(buffer);
            Console.WriteLine($"Received data: {str}");
            Assert.That(str, Is.EqualTo($"Hello world {i}!"));
        }
    }
}
