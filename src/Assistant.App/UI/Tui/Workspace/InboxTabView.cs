using Assistant.App.Mail;
using Assistant.App.UI.Abstractions;
using System;
using System.Linq;
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
        _host.RequestPopToRoot = BackToList;
        Add(_host);

        workspace.InboxChanged += OnInboxChanged;
    }

    public event Action? OutgoingMailSent;

    public void FocusContent() => _host.FocusCurrent();

    public void ActivateList()
    {
        BackToList();
        FocusContent();
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
        _host.Push(new InboxDetailScreen(
            message,
            onBack: BackToList,
            onReply: () => ShowReply(message),
            onDelete: () =>
            {
                _workspace.DeleteMail(message.Id);
                BackToList();
            }));
    }

    private void ShowReply(MailMessage original)
    {
        _host.Push(new ComposeMailScreen(
            toFixed: original.From,
            subject: original.Subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase)
                ? original.Subject
                : $"Re: {original.Subject}",
            subjectEditable: false,
            isReply: true,
            onSend: (_, body) => _workspace.ReplyMail(original.Id, body),
            onDone: () =>
            {
                BackToList();
                OutgoingMailSent?.Invoke();
            },
            onCancel: () =>
            {
                var message = _workspace.FindMail(original.Id) ?? original;
                PushDetail(message);
            }));
    }
}

internal sealed class InboxListScreen : View
{
    private readonly IUserWorkspace _workspace;
    private readonly Action<string> _openMail;
    private readonly InboxListView _list;

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

        _list = new InboxListView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(1)
        };
        _list.AcceptSelected += OpenSelected;

        Add(_list, hint);
        Reload();
    }

    public void Reload() =>
        _list.SetItems(_workspace.ListInbox().Reverse().ToArray());

    private void OpenSelected()
    {
        if (_list.SelectedItem is { } item)
        {
            _openMail(item.Id);
        }
    }
}
