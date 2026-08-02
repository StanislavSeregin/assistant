using Assistant.App.UI.Abstractions;
using System;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Assistant.App.UI.Tui.Compose;

/// <summary>
/// Subject + multiline body. Raises <see cref="IMailComposer.SendRequested"/> on Ctrl+Enter.
/// </summary>
public sealed class ComposePaneView : View, IMailComposer
{
    private readonly TextField _subject;
#pragma warning disable CS0618 // TextView superseded by external EditorView; fine for plain compose.
    private readonly TextView _body;
#pragma warning restore CS0618
    private readonly Label _hint;

    public ComposePaneView(string defaultSubject)
    {
        // Container for focusable fields — Tab cycles peers inside; F6 switches groups.
        CanFocus = true;
        TabStop = TabBehavior.TabGroup;

        var subjectLabel = new Label
        {
            Text = "Subject:",
            X = 0,
            Y = 0,
            CanFocus = false,
            TabStop = TabBehavior.NoStop
        };

        _subject = new TextField
        {
            Text = defaultSubject,
            X = Pos.Right(subjectLabel) + 1,
            Y = 0,
            Width = Dim.Fill(),
            CanFocus = true,
            TabStop = TabBehavior.TabStop
        };

#pragma warning disable CS0618
        _body = new TextView
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
            Multiline = true,
            WordWrap = true,
            CanFocus = true,
            TabStop = TabBehavior.TabStop,
            // Default true inserts '\t' and blocks focus navigation.
            TabKeyAddsTab = false
        };
#pragma warning restore CS0618

        _hint = new Label
        {
            Text = "Ctrl+Enter send · Tab fields · F6 panes",
            X = 0,
            Y = Pos.AnchorEnd(),
            Width = Dim.Fill(),
            CanFocus = false,
            TabStop = TabBehavior.NoStop
        };

        Add(subjectLabel, _subject, _body, _hint);

        AddCommand(Command.Save, () =>
        {
            TrySend();
            return true;
        });
        KeyBindings.Add(Key.Enter.WithCtrl, Command.Save);

        _body.KeyDown += (_, key) =>
        {
            if (key == Key.Enter.WithCtrl)
            {
                TrySend();
                key.Handled = true;
            }
        };
    }

    public event EventHandler<MailComposeRequest>? SendRequested;

    public string Subject
    {
        get => _subject.Text ?? string.Empty;
        set => _subject.Text = value ?? string.Empty;
    }

    public void ClearBody()
    {
        _body.Text = string.Empty;
    }

    public void FocusBody() => _body.SetFocus();

    public void FocusSubject() => _subject.SetFocus();

    private void TrySend()
    {
        var body = _body.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(body))
        {
            return;
        }

        var subject = string.IsNullOrWhiteSpace(Subject) ? "User request" : Subject.Trim();
        SendRequested?.Invoke(this, new MailComposeRequest(subject, body));
        ClearBody();
        FocusBody();
    }
}
