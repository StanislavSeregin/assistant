using System;
using System.Collections.Generic;
using System.Linq;

namespace Assistant.App.Checklist;

/// <summary>
/// Engine-owned open work plan for one LLM node. Complete/remove drop items from the open list.
/// </summary>
public sealed class AgentChecklist
{
    private readonly object _gate = new();
    private readonly List<ChecklistItem> _items = [];
    private int _nextId = 1;

    public bool HasOpenItems
    {
        get
        {
            lock (_gate)
            {
                return _items.Count > 0;
            }
        }
    }

    public IReadOnlyList<ChecklistItem> Snapshot()
    {
        lock (_gate)
        {
            return _items.ToArray();
        }
    }

    /// <summary>Replace the open plan. Returns whether state changed.</summary>
    public bool Set(IEnumerable<string> texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var cleaned = texts
            .Select(static t => (t ?? string.Empty).Trim())
            .Where(static t => t.Length > 0)
            .ToArray();

        lock (_gate)
        {
            if (SameTexts(_items, cleaned))
            {
                return false;
            }

            _items.Clear();
            foreach (var text in cleaned)
            {
                _items.Add(NewItem(text));
            }

            return true;
        }
    }

    /// <summary>Append items. Returns whether at least one item was added.</summary>
    public bool Add(IEnumerable<string> texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var cleaned = texts
            .Select(static t => (t ?? string.Empty).Trim())
            .Where(static t => t.Length > 0)
            .ToArray();
        if (cleaned.Length == 0)
        {
            return false;
        }

        lock (_gate)
        {
            foreach (var text in cleaned)
            {
                _items.Add(NewItem(text));
            }

            return true;
        }
    }

    /// <summary>Remove by id as completed. Returns false if id was missing.</summary>
    public bool Complete(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        lock (_gate)
        {
            return RemoveLocked(id.Trim());
        }
    }

    /// <summary>Drop by id without "done" semantics. Returns false if id was missing.</summary>
    public bool Remove(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        lock (_gate)
        {
            return RemoveLocked(id.Trim());
        }
    }

    public void ReplaceAll(IEnumerable<ChecklistItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        lock (_gate)
        {
            _items.Clear();
            var maxId = 0;
            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Text))
                {
                    continue;
                }

                var id = item.Id.Trim();
                _items.Add(new ChecklistItem { Id = id, Text = item.Text.Trim() });
                if (int.TryParse(id, out var n) && n > maxId)
                {
                    maxId = n;
                }
            }

            _nextId = Math.Max(_nextId, maxId + 1);
        }
    }

    private bool RemoveLocked(string id)
    {
        var index = _items.FindIndex(i => string.Equals(i.Id, id, StringComparison.Ordinal));
        if (index < 0)
        {
            return false;
        }

        _items.RemoveAt(index);
        return true;
    }

    private ChecklistItem NewItem(string text)
    {
        var id = _nextId++.ToString();
        return new ChecklistItem { Id = id, Text = text };
    }

    private static bool SameTexts(List<ChecklistItem> current, string[] cleaned)
    {
        if (current.Count != cleaned.Length)
        {
            return false;
        }

        for (var i = 0; i < cleaned.Length; i++)
        {
            if (!string.Equals(current[i].Text, cleaned[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
