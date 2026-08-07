using Assistant.App.UI.Abstractions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Assistant.App.UI.Tui.Log;

/// <summary>
/// Thread-safe <see cref="ILogSink"/> that queues ops and flushes into <see cref="LogBuffer"/>
/// on the UI thread via coalescing. Swap this for another sink without touching the presenter.
/// </summary>
public sealed class CoalescingLogSink : ILogSink, IDisposable
{
    private readonly LogBuffer _buffer;
    private readonly IUiScheduler _scheduler;
    private readonly ConcurrentQueue<Action> _pending = new();
    private readonly object _timerGate = new();
    private readonly TimeSpan _interval;
    private IDisposable? _timer;
    private bool _dirty;
    private int _version;

    public CoalescingLogSink(LogBuffer buffer, IUiScheduler scheduler, TimeSpan? interval = null)
    {
        _buffer = buffer;
        _scheduler = scheduler;
        _interval = interval ?? TimeSpan.FromMilliseconds(33);
    }

    public LogBuffer Buffer => _buffer;

    public int Version => _version;

    public event EventHandler? Flushed;

    public void Start()
    {
        lock (_timerGate)
        {
            if (_timer is not null)
            {
                return;
            }

            _timer = _scheduler.AddRepeatingTimer(_interval, FlushTick);
        }
    }

    public void BlankLine() => Enqueue(() => _buffer.BlankLine());

    public void Header(string text, LogTone tone, DateTime timestamp, string[]? nameAccents = null) =>
        Enqueue(() => _buffer.Header(text, tone, timestamp, nameAccents));

    public void BodyLine(string text, LogTone tone) =>
        Enqueue(() => _buffer.BodyLine(text, tone));

    public void AppendInline(string text, LogTone tone) =>
        Enqueue(() => _buffer.AppendInline(text, tone));

    public void Footer(string? text = null, LogTone tone = LogTone.Dim, string[]? nameAccents = null) =>
        Enqueue(() => _buffer.Footer(text, tone, nameAccents));

    public void Dispose()
    {
        lock (_timerGate)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    private void Enqueue(Action op)
    {
        _pending.Enqueue(op);
        _dirty = true;
        if (_scheduler.IsAvailable)
        {
            Start();
        }
    }

    private bool FlushTick()
    {
        if (!_dirty && _pending.IsEmpty)
        {
            return true;
        }

        var batch = 0;
        while (batch < 400 && _pending.TryDequeue(out var op))
        {
            op();
            batch++;
        }

        _dirty = !_pending.IsEmpty;
        if (batch > 0)
        {
            _version++;
            Flushed?.Invoke(this, EventArgs.Empty);
        }

        return true;
    }
}
