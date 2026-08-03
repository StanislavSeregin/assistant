using Assistant.App.Mail;
using System;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Assistant.App.UI.Tui.Workspace;

/// <summary>
/// Read-only mail detail, laid out like <see cref="ComposeMailScreen"/> so body/subject
/// can be selected and copied without allowing edits.
/// </summary>
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

        var subjectLabel = new Label
        {
            Text = "Subject:",
            X = 0,
            Y = 1,
            CanFocus = false
        };

        var subject = new TextField
        {
            Text = message.Subject,
            X = Pos.Right(subjectLabel) + 1,
            Y = 1,
            Width = Dim.Fill(),
            ReadOnly = true,
            CanFocus = true,
            TabStop = TabBehavior.TabStop
        };

#pragma warning disable CS0618
        var body = new TextView
        {
            Text = message.Body,
            X = 0,
            Y = 2,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
            Multiline = true,
            WordWrap = true,
            ReadOnly = true,
            CanFocus = true,
            TabStop = TabBehavior.TabStop,
            TabKeyAddsTab = false
        };
#pragma warning restore CS0618

        var hint = new Label
        {
            Text = "Ctrl+R reply · Ctrl+D delete · Esc back",
            X = 0,
            Y = Pos.AnchorEnd(),
            Width = Dim.Fill(),
            CanFocus = false
        };

        Add(header, subjectLabel, subject, body, hint);

        void HandleHotkeys(object? _, Key key)
        {
            if (key == Key.Esc)
            {
                onBack();
                key.Handled = true;
            }
            else if (key == Key.R.WithCtrl)
            {
                onReply();
                key.Handled = true;
            }
            else if (key == Key.D.WithCtrl)
            {
                // TextView binds Ctrl+D to delete-char; intercept before that runs.
                onDelete();
                key.Handled = true;
            }
        }

        KeyDown += HandleHotkeys;
        subject.KeyDown += HandleHotkeys;
        body.KeyDown += HandleHotkeys;
    }
}
