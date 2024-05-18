using System.Net;

namespace Currents.Protocol;

public readonly struct DataEvent(IPEndPoint endPoint, byte[] data)
{
    public readonly IPEndPoint EndPoint = endPoint;
    public readonly byte[] Data = data;
}
