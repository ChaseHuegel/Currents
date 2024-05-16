using System.Diagnostics.Metrics;

namespace Currents.Tests.Protocol;

internal class TestMeterFactory : IMeterFactory
{
    private List<Meter> _meters = [];

    public Meter Create(MeterOptions options)
    {
        var meter = new Meter(options);
        _meters.Add(meter);
        return meter;
    }

    public void Dispose()
    {
        foreach (var meter in _meters)
        {
            meter.Dispose();
        }

        _meters.Clear();
    }
}