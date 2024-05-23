using System.Buffers;
using System.Net;
using System.Net.Sockets;
using Currents.Events;
using Currents.Protocol;
using Currents.Security.Cryptography;
using Currents.Utils;

namespace Currents.IO;

internal class Channel : IDisposable
{
    public struct Options()
    {
        public int QueueSize = 512;
        public bool IPv6Only = false;
    }

    public Socket Socket => _socket;

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

    private readonly static EndPoint AnyEndPoint = new IPEndPoint(IPAddress.Any, 0);
    private EndPoint _lastRecvEndPoint = AnyEndPoint;
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
    private readonly SendEvent?[] _sendQueue;
    private readonly AutoResetEvent _recvSignal = new(false);
    private readonly byte[] _recvBuffer = new byte[ushort.MaxValue];
    private readonly byte[] _sendBuffer = new byte[ushort.MaxValue + 2];
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
            _recvThread = new Thread(RecvThread)
            {
                Name = $"CRNT Recv {LocalEndPoint}"
            };
            _recvThread.Start();

            _sendThread = new Thread(SendThread)
            {
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
        }

        _sendSignal.Set();
    }

    public bool TryConsume(out RecvEvent recvEvent, int timeoutMs = Timeout.Infinite)
    {
        lock (_recvDequeueLock)
        {
            if (_recvQueue[_recvDequeueIndex] == null)
            {
                _recvSignal.WaitOne(timeoutMs);
            }

            if (_recvQueue[_recvDequeueIndex] == null)
            {
                recvEvent = null!;
                return false;
            }

            recvEvent = DequeueRecvEvent();
            return true;
        }
    }

    public RecvEvent Consume()
    {
        lock (_recvDequeueLock)
        {
            while (_recvQueue[_recvDequeueIndex] == null)
            {
                _recvSignal.WaitOne();
            }

            return DequeueRecvEvent();
        }
    }

    public bool TryConsumeFrom(IPEndPoint targetEndPoint, out RecvEvent recvEvent, int timeoutMs = Timeout.Infinite)
    {
        while (true)
        {
            if (!TryConsume(out recvEvent, timeoutMs))
            {
                return false;
            }

            if (targetEndPoint.Equals(recvEvent.EndPoint))
            {
                return true;
            }
        }
    }

    public RecvEvent ConsumeFrom(IPEndPoint targetEndPoint)
    {
        while (true)
        {
            RecvEvent recvEvent = Consume();
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

    private RecvEvent DequeueRecvEvent()
    {
        RecvEvent recvEvent = _recvQueue[_recvDequeueIndex]!;
        _recvQueue[_recvDequeueIndex] = null;
        _recvDequeueIndex++;
        return recvEvent;
    }

    private void RecvThread()
    {
        _recvCloseHandle.Reset();

        while (_open)
        {
            if (_socket.Available == 0 || _recvQueue[_recvEnqueueIndex] != null)
            {
                Thread.Sleep(5);
                continue;
            }

            int bytesRecv;
            try
            {
                bytesRecv = _socket.ReceiveFrom(_recvBuffer, 0, _recvBuffer.Length, SocketFlags.None, ref _lastRecvEndPoint);
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch
            {
                //  SocketError.ConnectionReset can occur if the target endpoint isn't listening
                continue;
            }

            if (!_open)
            {
                break;
            }

            if (bytesRecv < 5)
            {
                continue;
            }

            ushort expectedChecksum = Bytes.ReadUShort(_recvBuffer, 0);
            ushort actualChecksum = Checksum16.Compute(_recvBuffer, 2, bytesRecv - 2);

            if (expectedChecksum != actualChecksum)
            {
                //  TODO raise a signal?
                continue;
            }

            byte[] buffer = _arrayPool.Rent(bytesRecv - 2);
            Buffer.BlockCopy(_recvBuffer, 2, buffer, 0, bytesRecv - 2);
            var segment = new PooledArraySegment<byte>(_arrayPool, buffer, 0, bytesRecv - 2);

            _recvQueue[_recvEnqueueIndex] = new RecvEvent((IPEndPoint)_lastRecvEndPoint, segment);
            _recvEnqueueIndex++;
            _recvSignal.Set();
        }

        _recvCloseHandle.Set();
    }

    private void SendThread()
    {
        _sendCloseHandle.Reset();

        while (_open)
        {
            _sendSignal.WaitOne();

            while (_sendQueue[_sendDequeueIndex] != null)
            {
                SendEvent sendEvent = _sendQueue[_sendDequeueIndex]!;
                _sendQueue[_sendDequeueIndex] = null;
                _sendDequeueIndex++;

                int packetLength;
                using (sendEvent.Data)
                {
                    //  TODO if the checksum is computed from sendEVent.Data.Array instead of _sendBuffer, the checksum in recv mismatches?
                    Buffer.BlockCopy(sendEvent.Data.Array, sendEvent.Data.Offset, _sendBuffer, 2, sendEvent.Data.Count);
                    ushort checksum = Checksum16.Compute(_sendBuffer, 2, sendEvent.Data.Count);
                    Bytes.Write(_sendBuffer, 0, checksum);
                    packetLength = sendEvent.Data.Count + 2;
                }

                try
                {
                    _socket.SendTo(_sendBuffer, 0, packetLength, SocketFlags.None, sendEvent.EndPoint);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch
                {
                    //  SocketError.ConnectionReset can occur if the target endpoint isn't listening
                    continue;
                }
            }
        }

        _sendCloseHandle.Set();
    }
}
