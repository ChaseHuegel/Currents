using System.Net;
using Currents.Protocol;

namespace Currents.Events;

internal class RecvEvent(IPEndPoint endPoint, PooledArraySegment<byte> data) : IDisposable
{
    public IPEndPoint EndPoint => _endPoint;
    public PooledArraySegment<byte> Data => _data;

    private readonly IPEndPoint _endPoint = endPoint;
    private readonly PooledArraySegment<byte> _data = data;

    public void Dispose()
    {
        _data.Dispose();
    }
}
