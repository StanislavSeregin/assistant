using Assistant.App.Mail;
using Assistant.App.UI.Abstractions;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Assistant.App.UI.Tui.Workspace;

public sealed class InboxTabView : View
{
    private readonly IUserWorkspace _workspace;
    private readonly IUiScheduler _ui;
    private readonly ScreenHostView _host = new();
    private readonly InboxListScreen _listScreen;

    public InboxTabView(IUserWorkspace workspace, IUiScheduler ui)
    {
        Title = "Inbox";
        CanFocus = true;
        TabStop = TabBehavior.TabGroup;
        Width = Dim.Fill();
        Height = Dim.Fill();

        _workspace = workspace;
        _ui = ui;
        _listScreen = new InboxListScreen(workspace, OpenMail);
        _host.Reset(_listScreen);
        Add(_host);

        workspace.InboxChanged += OnInboxChanged;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _workspace.InboxChanged -= OnInboxChanged;
        }

        base.Dispose(disposing);
    }

    private void OnInboxChanged(object? sender, EventArgs e)
    {
        _ui.Post(() =>
        {
            if (_host.IsRootScreen)
            {
                _listScreen.Reload();
            }
        });
    }

    private void BackToList()
    {
        _host.TryPopToRoot();
        _listScreen.Reload();
    }

    private void OpenMail(string mailId)
    {
        var message = _workspace.ReadMail(mailId);
        if (message is null)
        {
            return;
        }

        PushDetail(message);
    }

    private void PushDetail(MailMessage message)
    {
        var detail = new InboxDetailScreen(
            message,
            onBack: BackToList,
            onReply: () => ShowReply(message),
            onDelete: () =>
            {
                _workspace.DeleteMail(message.Id);
                BackToList();
            });
        _host.Push(detail);
    }

    private void ShowReply(MailMessage original)
    {
        var reply = new ComposeMailScreen(
            toFixed: original.From,
            subject: original.Subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase)
                ? original.Subject
                : $"Re: {original.Subject}",
            subjectEditable: false,
            isReply: true,
            onSend: (_, body) => _workspace.ReplyMail(original.Id, body),
            onDone: BackToList,
            onCancel: () =>
            {
                var message = _workspace.FindMail(original.Id) ?? original;
                PushDetail(message);
            });
        _host.Push(reply);
    }
}

internal sealed class InboxListScreen : View
{
    private readonly IUserWorkspace _workspace;
    private readonly Action<string> _openMail;
    private readonly ListView _list;
    private readonly ObservableCollection<string> _lines = [];
    private IReadOnlyList<InboxItem> _items = [];

    public InboxListScreen(IUserWorkspace workspace, Action<string> openMail)
    {
        _workspace = workspace;
        _openMail = openMail;
        CanFocus = true;
        TabStop = TabBehavior.TabGroup;

        var hint = new Label
        {
            Text = "Enter open",
            X = 0,
            Y = Pos.AnchorEnd(),
            Width = Dim.Fill(),
            CanFocus = false,
            TabStop = TabBehavior.NoStop
        };

        _list = new ListView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
            CanFocus = true,
            TabStop = TabBehavior.TabStop
        };
        _list.SetSource(_lines);
        _list.KeyDown += (_, key) =>
        {
            if (key == Key.Enter)
            {
                OpenSelected();
                key.Handled = true;
            }
        };

        Add(_list, hint);
        Reload();
    }

    public void Reload()
    {
        _items = _workspace.ListInbox().Reverse().ToArray();
        _lines.Clear();
        foreach (var i in _items)
        {
            var status = i.Status == MailStatus.New ? "NEW " : "    ";
            _lines.Add($"{status}{MailTimestamp.FormatUtc(i.Timestamp)}  {i.From}  {i.Subject}");
        }

        if (_lines.Count > 0)
        {
            _list.SelectedItem = 0;
        }
    }

    private void OpenSelected()
    {
        var index = _list.SelectedItem ?? -1;
        if (index < 0 || index >= _items.Count)
        {
            return;
        }

        _openMail(_items[index].Id);
    }
}

/// <summary>
/// Read-only mail body as a ListView subclass that owns R/D/Esc hotkeys.
/// TextView was eating letter keys as text input.
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
        var subject = new Label
        {
            Text = $"Subject: {message.Subject}",
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            CanFocus = false
        };

        var bodyLines = new ObservableCollection<string>(
            message.Body.ReplaceLineEndings("\n").Split('\n'));
        var body = new MailBodyList(onReply, onDelete, onBack)
        {
            X = 0,
            Y = 2,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
            CanFocus = true,
            TabStop = TabBehavior.TabStop
        };
        body.SetSource(bodyLines);

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

internal sealed class MailBodyList : ListView
{
    public MailBodyList(Action onReply, Action onDelete, Action onBack)
    {
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

        KeyBindings.Add(Key.R.WithCtrl, Command.Edit);
        KeyBindings.Add(Key.Esc, Command.Cancel);

        KeyDown += (_, key) =>
        {
            if (key == Key.R.WithCtrl)
            {
                onReply();
                key.Handled = true;
            }
            else if (key == Key.D.WithCtrl)
            {
                onDelete();
                key.Handled = true;
            }
            else if (key == Key.Esc)
            {
                onBack();
                key.Handled = true;
            }
        };
    }
}
