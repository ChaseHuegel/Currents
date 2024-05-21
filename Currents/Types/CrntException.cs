namespace Currents.Protocol;

public class CrntException : Exception
{
    public CrntException()
    {
    }

    public CrntException(string message) : base(message)
    {
    }

    public CrntException(string message, Exception innerException) : base(message, innerException)
    {
    }
}