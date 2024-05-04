using System.Net;
using Currents.Protocol;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;

namespace Currents.Tests.Protocol;

public class ConnectorTests
{
    [SetUp]
    public void Setup()
    {
    }

    [Test]
    [Timeout(5000)]
    public void Connect_Insecure_IPv4_Succeeds()
    {
        using var client = new Connector(new IPEndPoint(IPAddress.Any, 0));
        using var server = new Connector(new IPEndPoint(IPAddress.Any, 4321));

        Task.Run(AcceptConnection);
        Task AcceptConnection()
        {
            server.Accept();
            return Task.CompletedTask;
        }

        bool connected = client.Connect(new IPEndPoint(IPAddress.Loopback, 4321));

        Assert.That(connected, Is.True);
    }

    [Test]
    [Timeout(5000)]
    public void Connect_Insecure_IPv6_Succeeds()
    {
        using var client = new Connector(new IPEndPoint(IPAddress.IPv6Any, 0));
        using var server = new Connector(new IPEndPoint(IPAddress.IPv6Any, 4321));

        Task.Run(AcceptConnection);
        Task AcceptConnection()
        {
            server.Accept();
            return Task.CompletedTask;
        }

        bool connected = client.Connect(new IPEndPoint(IPAddress.IPv6Loopback, 4321));

        Assert.That(connected, Is.True);
    }

    [Test]
    [TestCase(5)]
    [TestCase(10)]
    [TestCase(50)]
    [TestCase(100)]
    [TestCase(200)]
    [TestCase(500)]
    public async Task Accept_ManySimultaneous_Succeeds(int connections)
    {
        using var server = new Connector(new IPEndPoint(IPAddress.Any, 4321));

        _ = Task.Run(AcceptManyConnections);
        Task AcceptManyConnections()
        {
            while (true)
            {
                server.Accept();
            }
        }

        var clients = new List<Connector>();
        var tcs = new TaskCompletionSource();
        var connectedClients = 0;
        for (int i = 0; i < connections; i++)
        {
            var client = new Connector(new IPEndPoint(IPAddress.Any, 0));
            clients.Add(client);

            _ = Task.Run(ConnectClient);
            Task ConnectClient()
            {
                bool connected = client.Connect(new IPEndPoint(IPAddress.Loopback, 4321));
                client.Dispose();
                if (connected) {
                    connectedClients++;
                }

                if (connectedClients == connections) {
                    tcs.SetResult();
                }

                return Task.CompletedTask;
            }
        }

        await Task.WhenAny(Task.Delay(5000), tcs.Task);

        foreach (var client in clients)
        {
            Console.WriteLine($"{client.Channel.LocalEndPoint} made {client.ConnectionAttempts} connection attempts. Success? {client.Connected}");
        }

        Assert.That(connectedClients, Is.EqualTo(connections));
    }
}