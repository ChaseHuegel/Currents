using System.Buffers;
using System.Net;
using System.Net.Sockets;
using Currents.Protocol.Packets;
using Currents.Security.Cryptography;
using Currents.Utils;

namespace Currents.Protocol;

internal class Channel : IDisposable
{
    public struct Options()
    {
        public int QueueSize = 512;
        public bool IPv6Only = false;
    }

    public IPEndPoint LocalEndPoint => _localEndPoint;
    public IPEndPoint RemoteEndPoint => _remoteEndPoint;
    public bool IsOpen
    {
        get
        {
            lock (_stateLock)
            {
                return _open;
            }
        }
    }

    private IPEndPoint _localEndPoint;
    private IPEndPoint _remoteEndPoint;

    private EndPoint _lastRecvEndPoint = new IPEndPoint(IPAddress.Any, 0);
    private Thread? _recvThread;
    private Thread? _sendThread;
    private byte _recvDequeueIndex;
    private byte _sendEnqueueIndex;
    private volatile byte _recvEnqueueIndex;
    private volatile byte _sendDequeueIndex;
    private volatile bool _open;

    private readonly object _stateLock = new();
    private readonly object _recvDequeueLock = new();
    private readonly object _sendEnqueueLock = new();

    private readonly Socket _socket;
    private readonly RecvEvent?[] _recvQueue;
    private readonly AutoResetEvent _recvSignal = new(false);
    private readonly byte[] _recvBuffer = new byte[ushort.MaxValue];
    private readonly byte[] _sendBuffer = new byte[ushort.MaxValue + 2];
    private readonly SendEvent?[] _sendQueue;
    private readonly AutoResetEvent _sendSignal = new(false);
    private readonly ArrayPool<byte> _arrayPool = ArrayPool<byte>.Shared;
    private readonly EventWaitHandle _recvCloseHandle = new(false, EventResetMode.ManualReset);
    private readonly EventWaitHandle _sendCloseHandle = new(false, EventResetMode.ManualReset);

    public Channel() : this(new Options()) { }

    public Channel(Options options)
    {
        _recvQueue = new RecvEvent?[options.QueueSize];
        _sendQueue = new SendEvent?[options.QueueSize];

        _socket = new Socket(SocketType.Dgram, ProtocolType.Udp)
        {
            Blocking = false
        };

        _socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, options.IPv6Only);

        _localEndPoint = (IPEndPoint)_socket.LocalEndPoint;
        _remoteEndPoint = (IPEndPoint)_socket.RemoteEndPoint;
    }

    public void Dispose()
    {
        Close();
        _socket.Dispose();
    }

    public void Bind(IPEndPoint localEndPoint)
    {
        _socket.Bind(localEndPoint);
        _localEndPoint = (IPEndPoint)_socket.LocalEndPoint;
        _remoteEndPoint = (IPEndPoint)_socket.RemoteEndPoint;
    }

    public void Open()
    {
        lock (_stateLock)
        {
            if (!_socket.IsBound)
            {
                throw new InvalidOperationException($"Tried to open a {nameof(Channel)} that is not bound.");
            }

            if (_open)
            {
                return;
            }

            _open = true;
            _recvThread = new Thread(RecvThread) {
                Name = $"CRNT Recv {LocalEndPoint}"
            };
            _recvThread.Start();

            _sendThread = new Thread(SendThread) {
                Name = $"CRNT Send {LocalEndPoint}"
            };
            _sendThread.Start();
        }
    }

    public void Close()
    {
        lock (_stateLock)
        {
            if (!_socket.IsBound)
            {
                throw new InvalidOperationException($"Tried to close a {nameof(Channel)} that is not bound.");
            }

            if (!_open)
            {
                return;
            }

            _open = false;
            _sendSignal.Set();
            _recvCloseHandle.WaitOne();
            _sendCloseHandle.WaitOne();
        }
    }

    public void Send(PooledArraySegment<byte> segment, IPEndPoint endPoint)
    {
        lock (_sendEnqueueLock)
        {
            _sendQueue[_sendEnqueueIndex] = new SendEvent(endPoint, segment);
            _sendEnqueueIndex++;
            _sendSignal.Set();
        }
    }

    public RecvEvent Consume(int timeoutMs = Timeout.Infinite)
    {
        lock (_recvDequeueLock)
        {
            while (_recvQueue[_recvDequeueIndex] == null)
            {
                _recvSignal.WaitOne(timeoutMs);
            }

            RecvEvent dataEvent = _recvQueue[_recvDequeueIndex]!.Value;
            _recvQueue[_recvDequeueIndex] = null;
            _recvDequeueIndex++;
            return dataEvent;
        }
    }

    public RecvEvent ConsumeFrom(IPEndPoint targetEndPoint, int timeoutMs = Timeout.Infinite)
    {
        while (true)
        {
            RecvEvent recvEvent = Consume(timeoutMs);
            if (targetEndPoint.Equals(recvEvent.EndPoint))
            {
                return recvEvent;
            }
        }
    }

    public bool HasData()
    {
        lock (_recvDequeueLock)
        {
            return _recvQueue[_recvDequeueIndex] != null;
        }
    }

    private void RecvThread()
    {
        _recvCloseHandle.Reset();

        try
        {
            while (_open)
            {
                if (_socket.Available == 0)
                {
                    Thread.Sleep(5);
                    continue;
                }

                int bytesRec = _socket.ReceiveFrom(_recvBuffer, 0, _recvBuffer.Length, SocketFlags.None, ref _lastRecvEndPoint);

                if (!_open)
                {
                    break;
                }

                if (bytesRec == 0)
                {
                    continue;
                }

                ushort expectedChecksum = Bytes.ReadUShort(_recvBuffer, 0);
                ushort actualChecksum = Checksum16.Compute(_recvBuffer, 2, bytesRec - 2);

                if (expectedChecksum != actualChecksum)
                {
                    //  TODO raise a signal?
                    continue;
                }

                byte[] buffer = _arrayPool.Rent(bytesRec);
                Buffer.BlockCopy(_recvBuffer, 2, buffer, 0, bytesRec);
                var segment = new PooledArraySegment<byte>(_arrayPool, buffer, 0, bytesRec);

                _recvQueue[_recvEnqueueIndex] = new RecvEvent((IPEndPoint)_lastRecvEndPoint, segment);
                _recvEnqueueIndex++;
                _recvSignal.Set();
            }
        }
        catch (Exception)
        {
            //  Expected
        }

        _recvCloseHandle.Set();
    }

    private void SendThread()
    {
        _sendCloseHandle.Reset();

        try
        {
            while (_open)
            {
                _sendSignal.WaitOne();

                while (_sendQueue[_sendDequeueIndex] != null)
                {
                    SendEvent sendEvent = _sendQueue[_sendDequeueIndex]!.Value;
                    _sendQueue[_sendDequeueIndex] = null;
                    _sendDequeueIndex++;

                    int packetLength;
                    using (sendEvent.Data)
                    {
                        ushort checksum = Checksum16.Compute(sendEvent.Data.Array, sendEvent.Data.Offset, sendEvent.Data.Count);
                        Bytes.Write(_sendBuffer, 0, checksum);
                        Buffer.BlockCopy(sendEvent.Data.Array, sendEvent.Data.Offset, _sendBuffer, 2, sendEvent.Data.Count);
                        packetLength = sendEvent.Data.Count + 2;
                    }

                    _socket.SendTo(_sendBuffer, 0, packetLength, SocketFlags.None, sendEvent.EndPoint);
                }
            }
        }
        catch (Exception)
        {
            //  Expected
        }

        _sendCloseHandle.Set();
    }
}
