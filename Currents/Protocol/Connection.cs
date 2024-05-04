using System.Diagnostics.Contracts;
using System.Net;

namespace Currents.Protocol;

public readonly struct Connection : IEquatable<IPEndPoint>
{
    public IPEndPoint EndPoint { get; }

    public Connection(IPEndPoint endPoint)
    {
        EndPoint = endPoint;
    }

    public static implicit operator IPEndPoint(Connection connection) => connection.EndPoint;

    public override bool Equals(object obj)
    {
        if (obj is not Connection other)
        {
            return false;
        }

        return other.EndPoint.Equals(EndPoint);
    }

    public override int GetHashCode()
    {
        return EndPoint.GetHashCode();
    }

    public bool Equals(IPEndPoint other)
    {
        return EndPoint.Equals(other);
    }
}
