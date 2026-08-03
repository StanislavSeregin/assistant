using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Assistant.App.UI.Tui.Workspace;

/// <summary>
/// Pick a free agent template, or open the blank custom-identity form via Ctrl+N.
/// </summary>
internal sealed class SpawnTemplateScreen : View
{
    private const string HintWithTemplates = "Enter spawn · Ctrl+N custom · Esc cancel";
    private const string HintEmpty = "No free templates · Ctrl+N custom · Esc cancel";

    private readonly Func<IReadOnlyList<AgentTemplate>> _listAvailable;
    private readonly Func<AgentTemplate, (bool Ok, string Message)> _onSpawn;
    private readonly Action _onCustom;
    private readonly Action _onCancel;
    private readonly ListView _list;
    private readonly ObservableCollection<string> _lines = [];
    private readonly Label _status;
    private IReadOnlyList<AgentTemplate> _templates = [];

    public SpawnTemplateScreen(
        Func<IReadOnlyList<AgentTemplate>> listAvailable,
        Func<AgentTemplate, (bool Ok, string Message)> onSpawn,
        Action onCustom,
        Action onCancel)
    {
        _listAvailable = listAvailable;
        _onSpawn = onSpawn;
        _onCustom = onCustom;
        _onCancel = onCancel;
        CanFocus = true;
        TabStop = TabBehavior.TabGroup;

        var title = new Label
        {
            Text = "New agent from template",
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            CanFocus = false
        };

        _list = new ListView
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
            CanFocus = true,
            TabStop = TabBehavior.TabStop
        };
        _list.SetSource(_lines);

        _status = new Label
        {
            Text = HintWithTemplates,
            X = 0,
            Y = Pos.AnchorEnd(),
            Width = Dim.Fill(),
            CanFocus = false
        };

        Add(title, _list, _status);
        KeyDown += OnKeyDown;
        _list.KeyDown += OnKeyDown;
        Reload();
    }

    public void Reload()
    {
        _templates = _listAvailable();
        _lines.Clear();
        foreach (var t in _templates)
        {
            _lines.Add($"{t.Name}  —  {t.Description}");
        }

        if (_lines.Count > 0)
        {
            _list.SelectedItem = 0;
        }

        _status.Text = _templates.Count == 0 ? HintEmpty : HintWithTemplates;
    }

    private void OnKeyDown(object? sender, Key key)
    {
        if (key == Key.Esc)
        {
            _onCancel();
            key.Handled = true;
            return;
        }

        if (key == Key.N.WithCtrl)
        {
            _onCustom();
            key.Handled = true;
            return;
        }

        if (key == Key.Enter || key == Key.Enter.WithCtrl)
        {
            TrySpawn();
            key.Handled = true;
        }
    }

    private void TrySpawn()
    {
        var index = _list.SelectedItem ?? -1;
        if (index < 0 || index >= _templates.Count)
        {
            _status.Text = _templates.Count == 0
                ? "No free templates. Ctrl+N for custom."
                : "No template selected.";
            return;
        }

        var (ok, message) = _onSpawn(_templates[index]);
        if (!ok)
        {
            _status.Text = message;
        }
    }
}

/// <summary>
/// Blank identity form for a custom agent (WriteMail assigns work after spawn).
/// </summary>
internal sealed class SpawnChildScreen : View
{
    private readonly TextField _name;
    private readonly TextField _description;
#pragma warning disable CS0618
    private readonly TextView _instructions;
#pragma warning restore CS0618
    private readonly Label _status;
    private readonly Func<string, string, string, (bool Ok, string Message)> _onCreate;
    private readonly Action _onCancel;

    public SpawnChildScreen(
        Func<string, string, string, (bool Ok, string Message)> onCreate,
        Action onCancel)
    {
        _onCreate = onCreate;
        _onCancel = onCancel;
        CanFocus = true;
        TabStop = TabBehavior.TabGroup;

        var title = new Label
        {
            Text = "New agent (identity only — WriteMail after)",
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            CanFocus = false
        };

        var nameLabel = new Label { Text = "Name:", X = 0, Y = 1, CanFocus = false };
        _name = new TextField
        {
            Text = string.Empty,
            X = 10,
            Y = 1,
            Width = Dim.Fill(),
            CanFocus = true,
            TabStop = TabBehavior.TabStop
        };

        var descLabel = new Label { Text = "Role:", X = 0, Y = 2, CanFocus = false };
        _description = new TextField
        {
            Text = string.Empty,
            X = 10,
            Y = 2,
            Width = Dim.Fill(),
            CanFocus = true,
            TabStop = TabBehavior.TabStop
        };

        var instrLabel = new Label { Text = "Instructions:", X = 0, Y = 3, CanFocus = false };
#pragma warning disable CS0618
        _instructions = new TextView
        {
            Text = string.Empty,
            X = 0,
            Y = 4,
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
            Text = "Ctrl+Enter create · Esc cancel",
            X = 0,
            Y = Pos.AnchorEnd(),
            Width = Dim.Fill(),
            CanFocus = false
        };

        Add(title, nameLabel, _name, descLabel, _description, instrLabel, _instructions, _status);

        KeyDown += (_, key) =>
        {
            if (key == Key.Esc)
            {
                _onCancel();
                key.Handled = true;
            }
            else if (key == Key.Enter.WithCtrl)
            {
                TryCreate();
                key.Handled = true;
            }
        };
    }

    private void TryCreate()
    {
        var name = _name.Text?.Trim() ?? string.Empty;
        var description = _description.Text?.Trim() ?? string.Empty;
        var instructions = _instructions.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            _status.Text = "Name is empty.";
            return;
        }

        var (ok, message) = _onCreate(name, description, instructions);
        if (!ok)
        {
            _status.Text = message;
        }
    }
}
