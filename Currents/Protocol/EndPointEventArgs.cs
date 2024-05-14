
namespace Currents.Protocol;

using System.Net;

internal readonly struct EndPointEventArgs(IPEndPoint endPoint)
{
    public readonly IPEndPoint EndPoint = endPoint;
}