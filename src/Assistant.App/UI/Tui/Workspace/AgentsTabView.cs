using Assistant.App.UI.Abstractions;
using Assistant.App.UI.Tui.Shell;
using System;
using System.Collections.Generic;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Assistant.App.UI.Tui.Workspace;

public sealed class AgentsTabView : View, IWorkspaceTab
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
            ui,
            onWrite: ShowWrite,
            onDispose: DisposeChild,
            onSpawn: ShowSpawnFromTemplate);
        _host.Reset(_listScreen);
        _host.RequestPopToRoot = BackToList;
        Add(_host);

        workspace.ChildrenChanged += OnChildrenChanged;
        workspace.AgentActivityChanged += OnAgentActivityChanged;
    }

    /// <summary>Active list or overlay screen inside this tab.</summary>
    public View? CurrentScreen => _host.Current;

    /// <summary>Focus the active screen inside this tab (deepest TabStop).</summary>
    public void FocusContent()
    {
        if (_host.IsRootScreen)
        {
            _listScreen.Reload();
        }

        _host.FocusCurrent();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _workspace.ChildrenChanged -= OnChildrenChanged;
            _workspace.AgentActivityChanged -= OnAgentActivityChanged;
            _listScreen.DisposeSpin();
        }

        base.Dispose(disposing);
    }

    private void OnChildrenChanged(object? sender, EventArgs e) =>
        _ui.Post(() => _listScreen.Reload());

    private void OnAgentActivityChanged(object? sender, EventArgs e) =>
        _ui.Post(() => _listScreen.OnActivityChanged());

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
            onDone: BackToList,
            onCancel: BackToList);
        _host.Push(compose);
    }

    private void DisposeChild(ChildNodeInfo child)
    {
        _workspace.DisposeChild(child.Name);
        _listScreen.Reload();
    }

    private void ShowSpawnFromTemplate()
    {
        var picker = new SpawnTemplateScreen(
            listAvailable: _workspace.ListAvailableAgentTemplates,
            onSpawn: template => FinishSpawn(_workspace.SpawnChild(template)),
            onCustom: ShowSpawnCustom,
            onCancel: BackToList);
        _host.Push(picker);
    }

    private void ShowSpawnCustom()
    {
        var form = new SpawnChildScreen(
            onCreate: (name, description, instructions) =>
                FinishSpawn(_workspace.SpawnChild(name, description, instructions)),
            onCancel: BackToList);
        _host.Push(form);
    }

    private (bool Ok, string Message) FinishSpawn((bool Ok, string Message) result)
    {
        if (result.Ok)
        {
            BackToList();
        }

        return result;
    }
}

internal sealed class AgentsListScreen : View
{
    private static readonly TimeSpan SpinInterval = TimeSpan.FromMilliseconds(80);

    private readonly IUserWorkspace _workspace;
    private readonly IUiScheduler _ui;
    private readonly Action<ChildNodeInfo> _onWrite;
    private readonly Action<ChildNodeInfo> _onDispose;
    private readonly Action _onSpawn;
    private readonly AgentsListView _list;
    private IReadOnlyList<ChildNodeInfo> _items = [];
    private IDisposable? _spinTimer;
    private bool _spinning;

    public AgentsListScreen(
        IUserWorkspace workspace,
        IUiScheduler ui,
        Action<ChildNodeInfo> onWrite,
        Action<ChildNodeInfo> onDispose,
        Action onSpawn)
    {
        _workspace = workspace;
        _ui = ui;
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

        _list = new AgentsListView
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

    public void DisposeSpin() => StopSpinTimer();

    public void Reload()
    {
        _items = _workspace.ListChildren();
        RefreshRows();
        OnActivityChanged();
    }

    public void OnActivityChanged()
    {
        var busy = _workspace.AnyAgentBusy;
        if (busy)
        {
            EnsureSpinTimer();
            RefreshRows();
            return;
        }

        if (!_spinning && _spinTimer is null)
        {
            return;
        }

        StopSpinTimer();
        RefreshRows();
    }

    private void EnsureSpinTimer()
    {
        if (_spinTimer is not null)
        {
            return;
        }

        _spinning = true;
        _spinTimer = _ui.AddRepeatingTimer(SpinInterval, OnSpinTick);
    }

    private void StopSpinTimer()
    {
        _spinTimer?.Dispose();
        _spinTimer = null;
        _spinning = false;
        _list.ResetSpin();
    }

    private bool OnSpinTick()
    {
        if (!_workspace.AnyAgentBusy)
        {
            _spinTimer = null;
            _spinning = false;
            _list.ResetSpin();
            RefreshRows();
            return false;
        }

        _spinning = true;
        _list.AdvanceSpin();
        return true;
    }

    private void RefreshRows()
    {
        var rows = new AgentListRow[_items.Count];
        for (var i = 0; i < _items.Count; i++)
        {
            var child = _items[i];
            var activity = _workspace.GetAgentActivity(child.Name);
            rows[i] = new AgentListRow(
                child.Name,
                activity.IsBusy,
                activity.ActiveActor);
        }

        _list.SetItems(rows);
    }

    private void OpenSelected()
    {
        if (TryGetSelected(out var child))
        {
            _onWrite(child);
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
        if (_list.SelectedItem is not { } row)
        {
            return false;
        }

        foreach (var item in _items)
        {
            if (string.Equals(item.Name, row.Name, StringComparison.Ordinal))
            {
                child = item;
                return true;
            }
        }

        return false;
    }
}
