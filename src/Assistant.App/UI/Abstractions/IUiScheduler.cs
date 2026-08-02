using System;

namespace Assistant.App.UI.Abstractions;

/// <summary>
/// Marshals work onto the UI thread. Keeps lifecycle/runtime free of toolkit types.
/// </summary>
public interface IUiScheduler
{
    bool IsAvailable { get; }

    void Post(Action action);

    /// <summary>Repeating timer on the UI thread. Return false from callback to stop.</summary>
    IDisposable? AddRepeatingTimer(TimeSpan interval, Func<bool> callback);
}
