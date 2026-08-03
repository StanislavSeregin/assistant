using Assistant.App.UI.Abstractions;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Assistant.App.UI.Tui.Workspace;

/// <summary>
/// Shared compose form for new mail and replies.
/// </summary>
internal sealed class ComposeMailScreen : View
{
    private readonly TextField _subject;
#pragma warning disable CS0618
    private readonly TextView _body;
#pragma warning restore CS0618
    private readonly Func<string, string, (bool Ok, string Message)> _onSend;
    private readonly Action _onDone;
    private readonly Action _onCancel;
    private readonly Label _status;

    public ComposeMailScreen(
        string toFixed,
        string subject,
        bool subjectEditable,
        bool isReply,
        Func<string, string, (bool Ok, string Message)> onSend,
        Action onDone,
        Action onCancel)
    {
        _onSend = onSend;
        _onDone = onDone;
        _onCancel = onCancel;
        CanFocus = true;
        TabStop = TabBehavior.TabGroup;

        var titleLabel = new Label
        {
            Text = isReply
                ? $"You → {toFixed} · reply"
                : $"You → {toFixed} · mail",
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            CanFocus = false
        };

        var subjectLabel = new Label
        {
            Text = "Subject:",
            X = 0,
            Y = 1,
            CanFocus = false
        };

        _subject = new TextField
        {
            Text = subject,
            X = Pos.Right(subjectLabel) + 1,
            Y = 1,
            Width = Dim.Fill(),
            ReadOnly = !subjectEditable,
            CanFocus = subjectEditable,
            TabStop = subjectEditable ? TabBehavior.TabStop : TabBehavior.NoStop
        };

#pragma warning disable CS0618
        _body = new TextView
        {
            Text = string.Empty,
            X = 0,
            Y = 2,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
            Multiline = true,
            WordWrap = true,
            CanFocus = true,
            TabStop = TabBehavior.TabStop,
            TabKeyAddsTab = false
        };
#pragma warning restore CS0618

        _status = new Label
        {
            Text = "Ctrl+Enter send · Esc cancel",
            X = 0,
            Y = Pos.AnchorEnd(),
            Width = Dim.Fill(),
            CanFocus = false
        };

        Add(titleLabel, subjectLabel, _subject, _body, _status);

        KeyDown += (_, key) =>
        {
            if (key == Key.Esc)
            {
                _onCancel();
                key.Handled = true;
            }
            else if (key == Key.Enter.WithCtrl)
            {
                TrySend();
                key.Handled = true;
            }
        };

        _body.KeyDown += (_, key) =>
        {
            if (key == Key.Enter.WithCtrl)
            {
                TrySend();
                key.Handled = true;
            }
        };
    }

    private void TrySend()
    {
        var body = _body.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(body))
        {
            _status.Text = "Body is empty.";
            return;
        }

        var subject = string.IsNullOrWhiteSpace(_subject.Text) ? "User request" : _subject.Text.Trim();
        var (ok, message) = _onSend(subject, body);
        if (!ok)
        {
            _status.Text = message;
            return;
        }

        _onDone();
    }
}

internal sealed class ConfirmScreen : View
{
    public ConfirmScreen(string question, Action onYes, Action onNo)
    {
        CanFocus = true;
        TabStop = TabBehavior.TabGroup;

        var label = new Label
        {
            Text = question,
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            CanFocus = false
        };
        var hint = new Label
        {
            Text = "Ctrl+Y yes · Esc no",
            X = 0,
            Y = 2,
            Width = Dim.Fill(),
            CanFocus = false
        };
        Add(label, hint);

        KeyDown += (_, key) =>
        {
            if (key == Key.Y.WithCtrl)
            {
                onYes();
                key.Handled = true;
            }
            else if (key == Key.Esc)
            {
                onNo();
                key.Handled = true;
            }
        };
    }
}
