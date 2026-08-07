using Assistant.App.Mail;
using Assistant.App.UI.Abstractions;
using Assistant.App.UI.Tui.Shell;
using System;
using System.Linq;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Assistant.App.UI.Tui.Workspace;

public sealed class InboxTabView : View, IWorkspaceTab
{
    private readonly IUserWorkspace _workspace;
    private readonly IUiScheduler _ui;
    private readonly ScreenHostView _host = new();
    private readonly InboxListScreen _listScreen;
    private string? _openMailId;

    public InboxTabView(IUserWorkspace workspace, IUiScheduler ui)
    {
        Title = "Inbox";
        CanFocus = true;
        TabStop = TabBehavior.TabGroup;
        Width = Dim.Fill();
        Height = Dim.Fill();

        _workspace = workspace;
        _ui = ui;
        _listScreen = new InboxListScreen(workspace, OpenMail, ReplyMail, DeleteMail);
        _host.Reset(_listScreen);
        _host.RequestPopToRoot = BackToList;
        Add(_host);

        workspace.InboxChanged += OnInboxChanged;
        RefreshTitle();
    }

    /// <summary>Active list or overlay screen inside this tab.</summary>
    public View? CurrentScreen => _host.Current;

    public void FocusContent()
    {
        // Tabs may keep a stale paint of inactive pages; re-bind from source when shown.
        if (_host.IsRootScreen)
        {
            _listScreen.Reload();
        }

        RefreshTitle();
        _host.FocusCurrent();
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
            // Drop detail/reply if that mail was purged (e.g. agent disposed) or deleted.
            // Unrelated inbox changes only refresh the list so reading stays put.
            if (_openMailId is not null && _workspace.FindMail(_openMailId) is null)
            {
                BackToList();
                RefreshTitle();
                return;
            }

            _listScreen.Reload();
            RefreshTitle();
        });
    }

    private void RefreshTitle()
    {
        var newCount = _workspace.ListInbox().Count(item => item.Status == MailStatus.New);
        var title = newCount > 0 ? $"Inbox ({newCount})" : "Inbox";
        WorkspaceTabs.SetPageTitle(this, title);
    }

    private void BackToList()
    {
        _openMailId = null;
        _host.TryPopToRoot();
        _listScreen.Reload();
        RefreshTitle();
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

    private void ReplyMail(string mailId)
    {
        var message = _workspace.FindMail(mailId);
        if (message is null)
        {
            return;
        }

        ShowReply(message, returnToDetail: false);
    }

    private void DeleteMail(string mailId)
    {
        _workspace.DeleteMail(mailId);
        _listScreen.Reload();
    }

    private void PushDetail(MailMessage message)
    {
        _openMailId = message.Id;
        _host.Push(new InboxDetailScreen(
            message,
            onBack: BackToList,
            onReply: () => ShowReply(message, returnToDetail: true),
            onDelete: () =>
            {
                _workspace.DeleteMail(message.Id);
                BackToList();
            }));
    }

    private void ShowReply(MailMessage original, bool returnToDetail)
    {
        _openMailId = original.Id;
        _host.Push(new ComposeMailScreen(
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
                if (!returnToDetail)
                {
                    BackToList();
                    return;
                }

                var message = _workspace.FindMail(original.Id) ?? original;
                PushDetail(message);
            }));
    }
}

internal sealed class InboxListScreen : View
{
    private readonly IUserWorkspace _workspace;
    private readonly Action<string> _openMail;
    private readonly Action<string> _replyMail;
    private readonly Action<string> _deleteMail;
    private readonly InboxListView _list;

    public InboxListScreen(
        IUserWorkspace workspace,
        Action<string> openMail,
        Action<string> replyMail,
        Action<string> deleteMail)
    {
        _workspace = workspace;
        _openMail = openMail;
        _replyMail = replyMail;
        _deleteMail = deleteMail;
        CanFocus = true;
        TabStop = TabBehavior.TabGroup;

        var hint = new Label
        {
            Text = "Enter open · Ctrl+R reply · Ctrl+D delete",
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
        KeyDown += OnKeyDown;
        _list.KeyDown += OnKeyDown;
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

    private void OnKeyDown(object? sender, Key key)
    {
        if (_list.SelectedItem is not { } item)
        {
            return;
        }

        if (key == Key.R.WithCtrl)
        {
            _replyMail(item.Id);
            key.Handled = true;
            return;
        }

        if (key == Key.D.WithCtrl)
        {
            _deleteMail(item.Id);
            key.Handled = true;
        }
    }
}
