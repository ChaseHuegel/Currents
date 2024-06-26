using System.Net;
using Currents.Events;
using Currents.IO;

namespace Currents.Protocol;

internal class PacketSignal<T> : IDisposable
{
    private PacketEvent<T> _event;
    private PacketConsumer _packetConsumer;
    private IPEndPoint? _targetEndPoint;
    private readonly EventWaitHandle _waitHandle;

    public PacketSignal(PacketConsumer packetConsumer, IPEndPoint? targetEndPoint = null)
    {
        _waitHandle = new EventWaitHandle(false, EventResetMode.ManualReset);
        _targetEndPoint = targetEndPoint;
        _packetConsumer = packetConsumer;
        _packetConsumer.AddListener<T>(OnEvent);
    }

    public void Dispose()
    {
        _packetConsumer.RemoveListener<T>(OnEvent);
        _waitHandle.Dispose();
    }

    public PacketEvent<T> WaitOne()
    {
        _waitHandle.WaitOne();
        return _event;
    }

    private void OnEvent(object sender, PacketEvent<T> e)
    {
        if (_targetEndPoint != null && !e.EndPoint.Equals(_targetEndPoint))
        {
            return;
        }

        _packetConsumer.RemoveListener<T>(OnEvent);
        _event = e;
        _waitHandle.Set();
    }
}