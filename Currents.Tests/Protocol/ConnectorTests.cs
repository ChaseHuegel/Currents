using System.Diagnostics;
using System.Net;
using Currents.IO;
using Currents.Metrics;
using Currents.Protocol;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;

namespace Currents.Tests.Protocol;

public partial class ConnectorTests
{
    [Test]
    [Timeout(5000)]
    public async Task Server_Close_Succeeds()
    {
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        ILogger serverLogger = loggerFactory.CreateLogger<ConnectionHandler>();
        using var meterFactory = new TestMeterFactory();
        var serverMetrics = new ConnectorMetrics(meterFactory);

        using var server = new CrntConnector(new IPEndPoint(IPAddress.Any, 4321), serverLogger, serverMetrics);

        _ = Task.Run(AcceptConnection);
        Task AcceptConnection()
        {
            server.Accept();
            return Task.CompletedTask;
        }

        await Task.Delay(5);
        server.Close();

        Assert.That(server.Active, Is.False);
    }

    [Test]
    [Timeout(5000)]
    public async Task Connect_AfterReopen_Succeeds()
    {
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        ILogger clientLogger = loggerFactory.CreateLogger<ConnectionHandler>();
        ILogger serverLogger = loggerFactory.CreateLogger<ConnectionHandler>();
        using var meterFactory = new TestMeterFactory();
        var clientMetrics = new ConnectorMetrics(meterFactory);
        var serverMetrics = new ConnectorMetrics(meterFactory);

        using var client = new CrntConnector(new IPEndPoint(IPAddress.Any, 0), clientLogger, clientMetrics);
        using var server = new CrntConnector(new IPEndPoint(IPAddress.Any, 4321), serverLogger, serverMetrics);

        _ = Task.Run(AcceptConnection);
        await Task.Delay(5);
        server.Close();
        Assert.That(server.Active, Is.False);

        _ = Task.Run(AcceptConnection);
        await Task.Delay(5);
        Assert.That(server.Active, Is.True);

        bool connected = client.TryConnect(new IPEndPoint(IPAddress.Loopback, 4321));

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
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        ILogger clientLogger = loggerFactory.CreateLogger<ConnectionHandler>();
        ILogger serverLogger = loggerFactory.CreateLogger<ConnectionHandler>();
        using var meterFactory = new TestMeterFactory();
        var clientMetrics = new ConnectorMetrics(meterFactory);
        var serverMetrics = new ConnectorMetrics(meterFactory);

        var recvPacketCollector = new MetricCollector<int>(meterFactory, ConnectorMetrics.MeterName, ConnectorMetrics.RecvPacketMeterName);
        var recvBytesCollector = new MetricCollector<int>(meterFactory, ConnectorMetrics.MeterName, ConnectorMetrics.RecvBytesMeterName);
        var sentPacketCollector = new MetricCollector<int>(meterFactory, ConnectorMetrics.MeterName, ConnectorMetrics.SentPacketMeterName);
        var sentBytesCollector = new MetricCollector<int>(meterFactory, ConnectorMetrics.MeterName, ConnectorMetrics.SentBytesMeterName);
        var connectionOpenedCollector = new MetricCollector<int>(meterFactory, ConnectorMetrics.MeterName, ConnectorMetrics.ConnectionOpenedMeterName);
        var connectionAcceptedCollector = new MetricCollector<int>(meterFactory, ConnectorMetrics.MeterName, ConnectorMetrics.ConnectionAcceptedMeterName);

        using var client = new CrntConnector(new IPEndPoint(IPAddress.Any, 0), clientLogger, clientMetrics);
        using var server = new CrntConnector(new IPEndPoint(IPAddress.Any, 4321), serverLogger, serverMetrics);

        Task.Run(AcceptConnection);
        Task AcceptConnection()
        {
            server.Accept();
            return Task.CompletedTask;
        }

        bool connected = client.TryConnect(new IPEndPoint(IPAddress.Loopback, 4321));

        var recvPackets = recvPacketCollector.GetMeasurementSnapshot();
        var recvBytes = recvBytesCollector.GetMeasurementSnapshot();
        var sentPackets = sentPacketCollector.GetMeasurementSnapshot();
        var sentBytes = sentBytesCollector.GetMeasurementSnapshot();
        var conenctionsOpened = connectionOpenedCollector.GetMeasurementSnapshot();
        var connectionsAccepted = connectionAcceptedCollector.GetMeasurementSnapshot();

        Assert.That(connected, Is.True);
    }

    [Test]
    [Timeout(5000)]
    public void Connect_Insecure_IPv6_Succeeds()
    {
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        ILogger clientLogger = loggerFactory.CreateLogger<ConnectionHandler>();
        ILogger serverLogger = loggerFactory.CreateLogger<ConnectionHandler>();
        using var meterFactory = new TestMeterFactory();
        var clientMetrics = new ConnectorMetrics(meterFactory);
        var serverMetrics = new ConnectorMetrics(meterFactory);

        using var client = new CrntConnector(new IPEndPoint(IPAddress.IPv6Any, 0), clientLogger, clientMetrics);
        using var server = new CrntConnector(new IPEndPoint(IPAddress.IPv6Any, 4321), serverLogger, serverMetrics);

        Task.Run(AcceptConnection);
        Task AcceptConnection()
        {
            server.Accept();
            return Task.CompletedTask;
        }

        bool connected = client.TryConnect(new IPEndPoint(IPAddress.IPv6Loopback, 4321));

        Assert.That(connected, Is.True);
    }

    [Test]
    [TestCase(5)]
    [TestCase(10)]
    [TestCase(50)]
    [TestCase(100)]
    public async Task Accept_ManySimultaneous_Succeeds(int connections)
    {
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Trace));
        ILogger clientLogger = loggerFactory.CreateLogger<ConnectionHandler>();
        ILogger serverLogger = loggerFactory.CreateLogger<ConnectionHandler>();
        using var meterFactory = new TestMeterFactory();
        var clientMetrics = new ConnectorMetrics(meterFactory);
        var serverMetrics = new ConnectorMetrics(meterFactory);

        var recvPacketCollector = new MetricCollector<int>(meterFactory, ConnectorMetrics.MeterName, ConnectorMetrics.RecvPacketMeterName);
        var recvBytesCollector = new MetricCollector<int>(meterFactory, ConnectorMetrics.MeterName, ConnectorMetrics.RecvBytesMeterName);
        var sentPacketCollector = new MetricCollector<int>(meterFactory, ConnectorMetrics.MeterName, ConnectorMetrics.SentPacketMeterName);
        var sentBytesCollector = new MetricCollector<int>(meterFactory, ConnectorMetrics.MeterName, ConnectorMetrics.SentBytesMeterName);
        var connectionOpenedCollector = new MetricCollector<int>(meterFactory, ConnectorMetrics.MeterName, ConnectorMetrics.ConnectionOpenedMeterName);
        var connectionAcceptedCollector = new MetricCollector<int>(meterFactory, ConnectorMetrics.MeterName, ConnectorMetrics.ConnectionAcceptedMeterName);

        using var server = new CrntConnector(new IPEndPoint(IPAddress.Any, 4321), serverLogger, serverMetrics);
        server.Start();

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
            var task = Task.Run(ConnectClient);
            connectTasks.Add(task);

            Task ConnectClient()
            {
                var client = new CrntConnector(new IPEndPoint(IPAddress.Any, 0), clientLogger, clientMetrics);
                bool connected = client.TryConnect(new IPEndPoint(IPAddress.Loopback, 4321));
                client.Dispose();

                if (!connected) {
                    Assert.Fail();
                }

                return Task.CompletedTask;
            }
        }

        var timeout = Task.Delay(5000);
        Stopwatch sw = Stopwatch.StartNew();
        await Task.WhenAny(timeout, Task.WhenAll(connectTasks));
        sw.Stop();

        var recvPackets = recvPacketCollector.GetMeasurementSnapshot();
        var recvBytes = recvBytesCollector.GetMeasurementSnapshot();
        var sentPackets = sentPacketCollector.GetMeasurementSnapshot();
        var sentBytes = sentBytesCollector.GetMeasurementSnapshot();
        var connectionsOpened = connectionOpenedCollector.GetMeasurementSnapshot();
        var connectionsAccepted = connectionAcceptedCollector.GetMeasurementSnapshot();

        Console.WriteLine($"Accepting {connectTasks.Count(task => task.IsCompleted)}/{connections} clients took {sw.ElapsedMilliseconds}ms");
        Assert.That(timeout.IsCompletedSuccessfully, Is.False);
    }
}