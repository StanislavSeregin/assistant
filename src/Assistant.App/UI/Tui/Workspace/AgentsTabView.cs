using Assistant.App.UI.Abstractions;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Assistant.App.UI.Tui.Workspace;

public sealed class AgentsTabView : View
{
    private readonly IUserWorkspace _workspace;
    private readonly IUiScheduler _ui;
    private readonly ScreenHostView _host = new();
    private readonly AgentsListScreen _listScreen;

    public AgentsTabView(IUserWorkspace workspace, IUiScheduler ui)
    {
        Title = "Agents";
        CanFocus = true;
        TabStop = TabBehavior.TabGroup;
        Width = Dim.Fill();
        Height = Dim.Fill();

        _workspace = workspace;
        _ui = ui;
        _listScreen = new AgentsListScreen(
            workspace,
            onWrite: ShowWrite,
            onDispose: ShowDisposeConfirm,
            onSpawn: ShowSpawn);
        _host.Reset(_listScreen);
        Add(_host);

        workspace.ChildrenChanged += OnChildrenChanged;
    }

    /// <summary>Raised after a successful new-mail send.</summary>
    public event Action? OutgoingMailSent;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _workspace.ChildrenChanged -= OnChildrenChanged;
        }

        base.Dispose(disposing);
    }

    private void OnChildrenChanged(object? sender, EventArgs e)
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

    private void ShowWrite(ChildNodeInfo child)
    {
        var compose = new ComposeMailScreen(
            toFixed: child.Name,
            subject: _workspace.DefaultMailSubject,
            subjectEditable: true,
            isReply: false,
            onSend: (subject, body) => _workspace.WriteMail(child.Name, subject, body),
            onDone: () =>
            {
                BackToList();
                OutgoingMailSent?.Invoke();
            },
            onCancel: BackToList);
        _host.Push(compose);
    }

    private void ShowDisposeConfirm(ChildNodeInfo child)
    {
        var confirm = new ConfirmScreen(
            $"Dispose '{child.Name}' and its subtree?",
            onYes: () =>
            {
                _workspace.DisposeChild(child.Name);
                BackToList();
            },
            onNo: BackToList);
        _host.Push(confirm);
    }

    private void ShowSpawn()
    {
        var defaults = _workspace.GetSpawnDefaults();
        var form = new SpawnChildScreen(
            defaults,
            onCreate: (name, description, instructions) =>
            {
                var (ok, message) = _workspace.SpawnChild(name, description, instructions);
                if (ok)
                {
                    BackToList();
                }

                return (ok, message);
            },
            onCancel: BackToList);
        _host.Push(form);
    }
}

internal sealed class AgentsListScreen : View
{
    private readonly IUserWorkspace _workspace;
    private readonly Action<ChildNodeInfo> _onWrite;
    private readonly Action<ChildNodeInfo> _onDispose;
    private readonly Action _onSpawn;
    private readonly ListView _list;
    private readonly ObservableCollection<string> _lines = [];
    private IReadOnlyList<ChildNodeInfo> _items = [];

    public AgentsListScreen(
        IUserWorkspace workspace,
        Action<ChildNodeInfo> onWrite,
        Action<ChildNodeInfo> onDispose,
        Action onSpawn)
    {
        _workspace = workspace;
        _onWrite = onWrite;
        _onDispose = onDispose;
        _onSpawn = onSpawn;
        CanFocus = true;
        TabStop = TabBehavior.TabGroup;

        var hint = new Label
        {
            Text = "Ctrl+W write · Ctrl+D dispose · Ctrl+N new · Enter write",
            X = 0,
            Y = Pos.AnchorEnd(),
            Width = Dim.Fill(),
            CanFocus = false
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

        Add(_list, hint);
        KeyDown += OnKeyDown;
        _list.KeyDown += OnKeyDown;
        Reload();
    }

    public void Reload()
    {
        _items = _workspace.ListChildren();
        _lines.Clear();
        foreach (var c in _items)
        {
            _lines.Add($"{c.Name}  —  {c.Description}");
        }

        if (_lines.Count > 0)
        {
            _list.SelectedItem = 0;
        }
    }

    private void OnKeyDown(object? sender, Key key)
    {
        if (key == Key.N.WithCtrl)
        {
            _onSpawn();
            key.Handled = true;
            return;
        }

        if (!TryGetSelected(out var child))
        {
            return;
        }

        if (key == Key.W.WithCtrl || key == Key.Enter)
        {
            _onWrite(child);
            key.Handled = true;
        }
        else if (key == Key.D.WithCtrl)
        {
            _onDispose(child);
            key.Handled = true;
        }
    }

    private bool TryGetSelected(out ChildNodeInfo child)
    {
        child = default!;
        var index = _list.SelectedItem ?? -1;
        if (index < 0 || index >= _items.Count)
        {
            return false;
        }

        child = _items[index];
        return true;
    }
}
