using System.Buffers;
using System.Net;
using System.Net.Sockets;

namespace Currents.Protocol;

internal class Channel : IDisposable
{
    public struct Options()
    {
        public int QueueSize = 256;
        public bool IPv6Only = false;
    }

    public Socket Socket => _socket;
    public IPEndPoint LocalEndPoint => _localEndPoint;
    public IPEndPoint RemoteEndPoint => _remoteEndPoint;

    private IPEndPoint _localEndPoint;
    private IPEndPoint _remoteEndPoint;

    private EndPoint _lastRecvEndPoint = new IPEndPoint(IPAddress.Any, 0);
    private Thread? _recvThread;
    private byte _dequeueIndex;
    private volatile byte _enqueueIndex;
    private volatile bool _open;

    private readonly object _stateLock = new object();
    private readonly object _dequeueLock = new object();

    private readonly Socket _socket;
    private readonly AutoResetEvent _recvHandle = new(false);
    private readonly byte[] _recvBuffer = new byte[ushort.MaxValue];
    private readonly ArrayPool<byte> _arrayPool = ArrayPool<byte>.Shared;
    private readonly DataReceivedEvent?[] _circularQueue;

    public Channel() : this(new Options()) { }

    public Channel(Options options)
    {
        _circularQueue = new DataReceivedEvent?[options.QueueSize];

        _socket = new Socket(SocketType.Dgram, ProtocolType.Udp);
        _socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, options.IPv6Only);

        _localEndPoint = (IPEndPoint)_socket.LocalEndPoint;
        _remoteEndPoint = (IPEndPoint)_socket.RemoteEndPoint;
    }

    public void Dispose()
    {
        _open = false;
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
            _recvThread ??= new Thread(Recv);
            _recvThread.Start();
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

    public DataReceivedEvent Consume(int timeoutMs = Timeout.Infinite)
    {
        lock (_dequeueLock)
        {
            while (_circularQueue[_dequeueIndex] == null)
            {
                _recvHandle.WaitOne(timeoutMs);
            }

            DataReceivedEvent dataEvent = _circularQueue[_dequeueIndex]!.Value;
            _circularQueue[_dequeueIndex] = null;
            _dequeueIndex++;
            return dataEvent;
        }
    }

    public bool HasData()
    {
        lock (_dequeueLock)
        {
            return _circularQueue[_dequeueIndex] != null;
        }
    }

    private void Recv()
    {
        try
        {
            while (_open)
            {
                int bytesRec = _socket.ReceiveFrom(_recvBuffer, 0, _recvBuffer.Length, SocketFlags.None, ref _lastRecvEndPoint);

                if (bytesRec > 0 && _open)
                {
                    byte[] buffer = _arrayPool.Rent(bytesRec);
                    Buffer.BlockCopy(_recvBuffer, 0, buffer, 0, bytesRec);
                    var segment = new PooledArraySegment<byte>(_arrayPool, buffer, 0, bytesRec);

                    _circularQueue[_enqueueIndex] = new DataReceivedEvent((IPEndPoint)_lastRecvEndPoint, segment);
                    _enqueueIndex++;
                    _recvHandle.Set();
                }
            }
        }
        catch when (!_open)
        {
            //  Expected
        }
    }
}
