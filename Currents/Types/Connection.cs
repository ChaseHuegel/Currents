using System.Net;

namespace Currents.Protocol;

internal readonly struct Connection : IEquatable<IPEndPoint>
{
    public IPEndPoint EndPoint { get; }
    public Syn Syn { get; } // TODO replace this with specific params for relevant settings

    public Connection(IPEndPoint endPoint)
    {
        EndPoint = endPoint;
    }

    public Connection(IPEndPoint endPoint, Syn syn)
    {
        EndPoint = endPoint;
        Syn = syn;
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

    public override string ToString()
    {
        return EndPoint.ToString();
    }
}
