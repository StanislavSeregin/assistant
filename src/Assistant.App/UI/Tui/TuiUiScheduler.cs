using Assistant.App.UI.Abstractions;
using System;
using System.Collections.Generic;
using Terminal.Gui.App;

namespace Assistant.App.UI.Tui;

/// <summary>
/// Terminal.Gui-backed <see cref="IUiScheduler"/>. Attach after <see cref="IApplication"/> exists.
/// </summary>
public sealed class TuiUiScheduler : IUiScheduler
{
    private IApplication? _app;
    private readonly List<(TimeSpan Interval, Func<bool> Callback)> _pendingTimers = [];
    private readonly object _gate = new();

    public bool IsAvailable
    {
        get
        {
            lock (_gate)
            {
                return _app is not null;
            }
        }
    }

    public void Attach(IApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        List<(TimeSpan Interval, Func<bool> Callback)> pending;
        lock (_gate)
        {
            _app = app;
            pending = [.. _pendingTimers];
            _pendingTimers.Clear();
        }

        foreach (var (interval, callback) in pending)
        {
            app.AddTimeout(interval, callback);
        }
    }

    public void Detach()
    {
        lock (_gate)
        {
            _app = null;
        }
    }

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        IApplication? app;
        lock (_gate)
        {
            app = _app;
        }

        if (app is null)
        {
            action();
            return;
        }

        app.Invoke(action);
    }

    public IDisposable? AddRepeatingTimer(TimeSpan interval, Func<bool> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        IApplication? app;
        lock (_gate)
        {
            app = _app;
            if (app is null)
            {
                _pendingTimers.Add((interval, callback));
                return null;
            }
        }

        var token = app.AddTimeout(interval, callback)
            ?? throw new InvalidOperationException("Failed to register UI timeout.");
        return new TimeoutLease(app, token);
    }

    private sealed class TimeoutLease(IApplication app, object token) : IDisposable
    {
        public void Dispose() => app.RemoveTimeout(token);
    }
}
