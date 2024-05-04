using System.Buffers;
using System.Net;
using System.Net.Sockets;

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
    private readonly AutoResetEvent _recvHandle = new(false);
    private readonly byte[] _recvBuffer = new byte[ushort.MaxValue];
    private readonly SendEvent?[] _sendQueue;
    private readonly AutoResetEvent _sendHandle = new(false);
    private readonly ArrayPool<byte> _arrayPool = ArrayPool<byte>.Shared;

    public Channel() : this(new Options()) { }

    public Channel(Options options)
    {
        _recvQueue = new RecvEvent?[options.QueueSize];
        _sendQueue = new SendEvent?[options.QueueSize];

        _socket = new Socket(SocketType.Dgram, ProtocolType.Udp);
        _socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, options.IPv6Only);

        _localEndPoint = (IPEndPoint)_socket.LocalEndPoint;
        _remoteEndPoint = (IPEndPoint)_socket.RemoteEndPoint;
    }

    public void Dispose()
    {
        _open = false;
        _sendHandle.Set();
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
            _recvThread ??= new Thread(RecvThread) {
                Name = "Currents Recv Thread"
            };
            _recvThread.Start();

            _sendThread ??= new Thread(SendThread) {
                Name = "Currents Send Thread"
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

            _open = false;
        }
    }

    public void Send(PooledArraySegment<byte> segment, IPEndPoint endPoint)
    {
        lock (_sendEnqueueLock)
        {
            _sendQueue[_sendEnqueueIndex] = new SendEvent(endPoint, segment);
            _sendEnqueueIndex++;
            _sendHandle.Set();
        }
    }

    public RecvEvent Consume(int timeoutMs = Timeout.Infinite)
    {
        lock (_recvDequeueLock)
        {
            while (_recvQueue[_recvDequeueIndex] == null)
            {
                _recvHandle.WaitOne(timeoutMs);
            }

            RecvEvent dataEvent = _recvQueue[_recvDequeueIndex]!.Value;
            _recvQueue[_recvDequeueIndex] = null;
            _recvDequeueIndex++;
            return dataEvent;
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
        try
        {
            while (_open)
            {
                int bytesRec = _socket.ReceiveFrom(_recvBuffer, 0, _recvBuffer.Length, SocketFlags.None, ref _lastRecvEndPoint);

                if (!_open)
                {
                    break;
                }

                if (bytesRec > 0)
                {
                    byte[] buffer = _arrayPool.Rent(bytesRec);
                    Buffer.BlockCopy(_recvBuffer, 0, buffer, 0, bytesRec);
                    var segment = new PooledArraySegment<byte>(_arrayPool, buffer, 0, bytesRec);

                    _recvQueue[_recvEnqueueIndex] = new RecvEvent((IPEndPoint)_lastRecvEndPoint, segment);
                    _recvEnqueueIndex++;
                    _recvHandle.Set();
                }
            }
        }
        catch (Exception)
        {
            //  Expected
        }
    }

    private void SendThread()
    {
        try
        {
            while (_open)
            {
                if (_sendQueue[_sendDequeueIndex] == null)
                {
                    _sendHandle.WaitOne();
                }

                if (!_open)
                {
                    break;
                }

                SendEvent sendEvent = _sendQueue[_sendDequeueIndex]!.Value;
                _sendQueue[_sendDequeueIndex] = null;
                _sendDequeueIndex++;

                using (sendEvent.Data)
                {
                    _socket.SendTo(sendEvent.Data.Array, sendEvent.Data.Offset, sendEvent.Data.Count, SocketFlags.None, sendEvent.EndPoint);
                }
            }
        }
        catch (Exception)
        {
            //  Expected
        }
    }
}
