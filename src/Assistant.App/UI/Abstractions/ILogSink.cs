using System;

namespace Assistant.App.UI.Abstractions;

/// <summary>
/// Toolkit-agnostic log surface. Presenters write here; UI backends render.
/// Thread-safe implementations should accept calls from any thread.
/// </summary>
public interface ILogSink
{
    void BlankLine();

    /// <param name="nameAccents">
    /// Agent/node names that appear in <paramref name="text"/> and should be
    /// painted with a stable per-name color (optional).
    /// </param>
    void Header(string text, LogTone tone, DateTime timestamp, string[]? nameAccents = null);

    void BodyLine(string text, LogTone tone);

    /// <summary>Streaming append (thinking deltas). May contain newlines.</summary>
    void AppendInline(string text, LogTone tone);

    void Footer(string? text = null, LogTone tone = LogTone.Dim, string[]? nameAccents = null);
}
