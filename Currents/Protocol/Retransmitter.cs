
namespace Currents.Protocol;

using System.Net;
using Timer = System.Timers.Timer;

internal class Retransmitter : IDisposable
{
    public EventHandler<EndPointEventArgs>? Expired;

    public IPEndPoint EndPoint
    {
        get
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(Retransmitter));
            }

            return _endPoint;
        }
    }

    public byte Retransmissions
    {
        get
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(Retransmitter));
            }

            return _retransmissions;
        }
    }

    private readonly byte _maxRetransmissions;
    private readonly ushort _retransmissionTimeout;
    private readonly Timer _timer;
    private readonly Channel _channel;
    private readonly PooledArraySegment<byte> _data;
    private readonly IPEndPoint _endPoint;

    private byte _retransmissions;
    private bool _disposed;

    /// <summary>
    ///  Retransmits data at an interval of <paramref name="retransmissionTimeoutMs"/>, up to <paramref name="maxRetransmissions"/> times.
    /// </summary>
    /// <param name="maxRetransmissions">
    /// The maximum number of times consecutive retransmission(s) will be attempted
    /// before the connection is considered broken.  The valid range for this value
    /// is 0 to 255.  A value of 0 indicates retransmission should be attempted
    /// forever.  This is a negotiable parameter, both peers must agree on the same
    /// value for this parameter.
    /// </param>
    /// <param name="retransmissionTimeoutMs">
    /// The timeout value for retransmission of unacknowledged packets.  This value
    /// is specified in milliseconds. The valid range is 100 to 65535.  This is a
    /// negotiable parameter, both peers must agree on the same value for this
    /// parameter.
    /// </param>
    public Retransmitter(Channel channel, byte maxRetransmissions, ushort retransmissionTimeoutMs, PooledArraySegment<byte> data, IPEndPoint endPoint)
    {
        _channel = channel;
        _maxRetransmissions = maxRetransmissions;
        _retransmissionTimeout = retransmissionTimeoutMs;
        _data = data;
        _endPoint = endPoint;

        _timer = new Timer(_retransmissionTimeout)
        {
            AutoReset = false
        };
        _timer.Elapsed += OnElapsed;
        _timer.Start();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Elapsed -= OnElapsed;
        _timer.Stop();
    }

    private void OnElapsed(object sender, System.Timers.ElapsedEventArgs e)
    {
        if (_maxRetransmissions > 0 && _retransmissions >= _maxRetransmissions)
        {
            _timer.Stop();
            Expired?.Invoke(this, new EndPointEventArgs(_endPoint));
            return;
        }

        _timer.Stop();
        _channel.Send(_data, _endPoint);
        _retransmissions++;
        _timer.Start();
    }
}
