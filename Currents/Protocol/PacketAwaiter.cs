using System.Net;
using System.Runtime.CompilerServices;

namespace Currents.Protocol;

internal class PacketAwaiter<T>
{
    private PacketConsumer _packetConsumer;
    private IPEndPoint? _targetEndPoint;
    private readonly TaskCompletionSource<PacketEvent<T>> _taskCompletionSource;

    public PacketAwaiter(PacketConsumer packetConsumer, IPEndPoint? targetEndPoint = null)
    {
        _taskCompletionSource = new TaskCompletionSource<PacketEvent<T>>();
        _targetEndPoint = targetEndPoint;
        _packetConsumer = packetConsumer;
        _packetConsumer.AddListener<T>(OnEvent);
    }

    public TaskAwaiter<PacketEvent<T>> GetAwaiter()
    {
        return _taskCompletionSource.Task.GetAwaiter();
    }

    private void OnEvent(object sender, PacketEvent<T> e)
    {
        if (_targetEndPoint != null && !e.EndPoint.Equals(_targetEndPoint))
        {
            return;
        }

        _packetConsumer.RemoveListener<T>(OnEvent);
        _taskCompletionSource.SetResult(e);
    }
}