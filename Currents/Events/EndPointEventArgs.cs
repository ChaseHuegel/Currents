namespace Currents.Events;

using System.Net;

internal readonly struct EndPointEventArgs(IPEndPoint endPoint)
{
    public readonly IPEndPoint EndPoint = endPoint;
}