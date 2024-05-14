using System.Diagnostics;
using System.Net;
using Currents.Protocol;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;

namespace Currents.Tests.Protocol;

public class ConnectorTests
{
    [Test]
    [Timeout(5000)]
    public async Task Close_Succeeds()
    {
        using var server = new Connector(new IPEndPoint(IPAddress.Any, 4321));

        _ = Task.Run(AcceptConnection);
        Task AcceptConnection()
        {
            server.Accept();
            return Task.CompletedTask;
        }

        await Task.Delay(5);
        server.Close();

        Assert.That(server.Channel.IsOpen, Is.False);
    }

    [Test]
    [Timeout(5000)]
    public async Task Connect_AfterReopen_Succeeds()
    {
        using var client = new Connector(new IPEndPoint(IPAddress.Any, 0));
        using var server = new Connector(new IPEndPoint(IPAddress.Any, 4321));

        _ = Task.Run(AcceptConnection);
        await Task.Delay(5);
        server.Close();
        Assert.That(server.Channel.IsOpen, Is.False);

        _ = Task.Run(AcceptConnection);
        await Task.Delay(5);
        Assert.That(server.Channel.IsOpen, Is.True);

        bool connected = client.Connect(new IPEndPoint(IPAddress.Loopback, 4321));

        Assert.That(connected, Is.True);

        Task AcceptConnection()
        {
            server.Accept();
            return Task.CompletedTask;
        }
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
    [TestCase(1000)]
    [Timeout(10000)]
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

        var connectTasks = new List<Task>();
        for (int i = 0; i < connections; i++)
        {
            var client = new Connector(new IPEndPoint(IPAddress.Any, 0));
            var task = Task.Run(ConnectClient);
            connectTasks.Add(task);

            Task ConnectClient()
            {
                bool connected = client.Connect(new IPEndPoint(IPAddress.Loopback, 4321));
                client.Dispose();

                if (!connected) {
                    Assert.Fail();
                }

                return Task.CompletedTask;
            }
        }

        Stopwatch sw = Stopwatch.StartNew();
        await Task.WhenAll(connectTasks);
        sw.Stop();

        Console.WriteLine($"Accepting {connections} clients took {sw.ElapsedMilliseconds}ms");
    }
}