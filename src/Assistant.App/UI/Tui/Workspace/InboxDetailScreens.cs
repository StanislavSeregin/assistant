using Assistant.App.Mail;
using Assistant.App.UI.Formatting;
using System;
using System.Collections.ObjectModel;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Assistant.App.UI.Tui.Workspace;

internal sealed class InboxDetailScreen : View
{
    public InboxDetailScreen(
        MailMessage message,
        Action onBack,
        Action onReply,
        Action onDelete)
    {
        CanFocus = true;
        TabStop = TabBehavior.TabGroup;

        var header = new Label
        {
            Text = $"From: {message.From}  Id: {message.Id}",
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            CanFocus = false
        };
        var subject = new Label
        {
            Text = $"Subject: {message.Subject}",
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            CanFocus = false
        };

        var body = new MailBodyList(message.Body, onReply, onDelete, onBack)
        {
            X = 0,
            Y = 2,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
            CanFocus = true,
            TabStop = TabBehavior.TabStop
        };

        var hint = new Label
        {
            Text = "Ctrl+R reply · Ctrl+D delete · Esc back",
            X = 0,
            Y = Pos.AnchorEnd(),
            Width = Dim.Fill(),
            CanFocus = false
        };

        Add(header, subject, body, hint);
    }
}

/// <summary>
/// Read-only wrapped body. ListView avoids TextView eating letter keys as input.
/// </summary>
internal sealed class MailBodyList : ListView
{
    private readonly string[] _paragraphs;
    private readonly ObservableCollection<string> _lines = [];
    private int _wrapWidth = -1;

    public MailBodyList(string body, Action onReply, Action onDelete, Action onBack)
    {
        _paragraphs = body.ReplaceLineEndings("\n").Split('\n');
        SetSource(_lines);

        AddCommand(Command.Edit, () =>
        {
            onReply();
            return true;
        });
        AddCommand(Command.Cancel, () =>
        {
            onBack();
            return true;
        });
        AddCommand(Command.DeleteAll, () =>
        {
            onDelete();
            return true;
        });

        KeyBindings.Add(Key.R.WithCtrl, Command.Edit);
        KeyBindings.Add(Key.D.WithCtrl, Command.DeleteAll);
        KeyBindings.Remove(Key.Esc);
        KeyBindings.Add(Key.Esc, Command.Cancel);

        ViewportChanged += (_, _) => EnsureWrapped();
    }

    private void EnsureWrapped()
    {
        var width = Math.Max(1, Viewport.Width);
        if (width == _wrapWidth)
        {
            return;
        }

        _wrapWidth = width;
        var selected = Math.Max(0, SelectedItem ?? 0);
        _lines.Clear();
        foreach (var paragraph in _paragraphs)
        {
            foreach (var segment in TextWrapping.Wrap(paragraph, width))
            {
                _lines.Add(segment);
            }
        }

        if (_lines.Count > 0)
        {
            SelectedItem = Math.Clamp(selected, 0, _lines.Count - 1);
        }
    }
}
