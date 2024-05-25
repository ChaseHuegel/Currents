using System.Timers;
using Currents.Types;

namespace Currents.Tests.Protocol;

public class SlidingWindowTests
{
    [Test]
    public void Pop_IsOrdered()
    {
        var window = new SlidingWindow<byte>(5);
        window.ItemAccepted += OnPopped;

        window.TryInsert(0, 1);
        Console.WriteLine("Inserted 0");
        window.TryAccept(0);
        Console.WriteLine("Accepted 0");
        window.TryInsert(1, 2);
        Console.WriteLine("Inserted 1");
        window.TryAccept(1);
        Console.WriteLine("Accepted 1");
        window.TryInsert(4, 5);
        Console.WriteLine("Inserted 4");
        window.TryAccept(4);
        Console.WriteLine("Accepted 4");
        window.TryInsert(3, 4);
        Console.WriteLine("Inserted 3");
        window.TryAccept(3);
        Console.WriteLine("Accepted 3");
        window.TryInsert(2, 3);
        Console.WriteLine("Inserted 2");
        window.TryAccept(2);
        Console.WriteLine("Accepted 2");

        void OnPopped(object? sender, (byte, byte) e)
        {
            Console.WriteLine($"Popped sequence: {e.Item1} value: {e.Item2}");
        }
    }

    [Test]
    public async Task IO_IsOrdered()
    {
        var tcs = new TaskCompletionSource();

        var client = new SlidingIO(5, 5);
        var server = new SlidingIO(5, 5);

        client.Sent += ClientSend;
        client.Received += OnClientReceived;

        server.Sent += ServerSend;
        server.Received += OnServerReceived;

        void ClientSend(object? sender, (byte, byte) e)
        {
            server.Recv(e.Item1, e.Item2);
        }

        void ServerSend(object? sender, (byte, byte) e)
        {
            client.Recv(e.Item1, e.Item2);
        }

        void OnServerReceived(object? sender, (byte, byte) e)
        {
            server.Accept(e.Item1);
            Console.WriteLine($"Server received sequence: {e.Item1} value: {e.Item2}");

            if (e.Item1 == 9)
            {
                tcs.SetResult();
            }
        }

        void OnClientReceived(object? sender, (byte, byte) e)
        {
            client.Accept(e.Item1);
            Console.WriteLine($"Client received sequence: {e.Item1} value: {e.Item2}");
        }

        client.Send(0, 1);
        client.Send(1, 2);
        client.Send(3, 4);
        client.Send(8, 9);
        client.Send(7, 8);
        client.Send(4, 5);
        client.Send(2, 3);
        client.Send(5, 6);
        client.Send(6, 7);
        client.Send(9, 10);

        await tcs.Task;

        Console.WriteLine($"Retransmissions: {client.Retransmissions}");
    }

    private class SlidingIO
    {
        public EventHandler<(byte, byte)>? Sent;
        public EventHandler<(byte, byte)>? Received;

        public int Retransmissions { get; private set; }

        private Random _random = new();

        private SlidingWindow<byte> _sendWindow;
        private SlidingWindow<byte> _recvWindow;
        private System.Timers.Timer?[] _retransmitters = new System.Timers.Timer?[256];

        public SlidingIO(byte windowSize, byte peerWindowSize)
        {
            _sendWindow = new SlidingWindow<byte>(peerWindowSize);
            _sendWindow.ItemAvailable += OnSendAvailable;
            _sendWindow.ItemAccepted += OnSendAccepted;

            _recvWindow = new SlidingWindow<byte>(windowSize);
            _recvWindow.ItemAccepted += OnRecvPopped;
        }

        public void Send(byte sequence, byte value)
        {
            _sendWindow.TryInsert(sequence, value);
        }

        public void Accept(byte sequence)
        {
            _sendWindow.TryAccept(sequence);
        }

        public void Recv(byte sequence, byte value)
        {
            _recvWindow.TryInsert(sequence, value);
            _recvWindow.TryAccept(sequence);
        }

        private void OnSendAvailable(object? sender, (byte, byte) e)
        {
            if (DoSend(e))
            {
                return;
            }

            var timer = new System.Timers.Timer(100);
            timer.Elapsed += OnRetransmit;
            timer.Start();
            _retransmitters[e.Item1] = timer;

            void OnRetransmit(object? sender, ElapsedEventArgs elapsedEvent)
            {
                Retransmissions++;
                DoSend(e);
            }
        }

        private void OnSendAccepted(object? sender, (byte, byte) e)
        {
            _retransmitters[e.Item1]?.Stop();
        }

        private bool DoSend((byte, byte) e)
        {
            if (_random.NextDouble() < 0.5d)
            {
                return false;
            }

            Sent?.Invoke(this, e);
            return true;
        }

        private void OnRecvPopped(object? sender, (byte, byte) e)
        {
            Received?.Invoke(this, e);
        }
    }

}
