using System;
using System.Collections.Generic;
using System.Linq;

namespace LGSTrayUI;

public sealed class AlertStateService
{
    private readonly Dictionary<string, bool> _blinkingDevices = [];

    public event Action? Changed;

    public bool HasAnyBlinking
    {
        get
        {
            lock (_blinkingDevices)
            {
                return _blinkingDevices.Values.Any(x => x);
            }
        }
    }

    public bool IsBlinking(string deviceId)
    {
        lock (_blinkingDevices)
        {
            return _blinkingDevices.TryGetValue(deviceId, out bool blinking) && blinking;
        }
    }

    public void SetBlinking(string deviceId, bool blinking)
    {
        bool changed;
        lock (_blinkingDevices)
        {
            _blinkingDevices.TryGetValue(deviceId, out bool old);
            changed = old != blinking;
            if (blinking)
            {
                _blinkingDevices[deviceId] = true;
            }
            else
            {
                _blinkingDevices.Remove(deviceId);
            }
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    public void ClearAll()
    {
        lock (_blinkingDevices)
        {
            if (_blinkingDevices.Count == 0)
            {
                return;
            }

            _blinkingDevices.Clear();
        }

        Changed?.Invoke();
    }
}
