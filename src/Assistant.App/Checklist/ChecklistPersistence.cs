using System.Collections.Generic;
using System.Linq;

namespace Assistant.App.Checklist;

/// <summary>DTO for LiteDB / JSON persistence of open checklist items.</summary>
public sealed class PersistedChecklistItem
{
    public string Id { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
}

public static class ChecklistPersistence
{
    public static List<PersistedChecklistItem> ToPersisted(AgentChecklist checklist) =>
        checklist.Snapshot()
            .Select(static i => new PersistedChecklistItem { Id = i.Id, Text = i.Text })
            .ToList();

    public static void ApplyPersisted(
        AgentChecklist checklist,
        IReadOnlyList<PersistedChecklistItem>? items)
    {
        if (items is null || items.Count == 0)
        {
            checklist.ReplaceAll([]);
            return;
        }

        checklist.ReplaceAll(
            items.Select(static i => new ChecklistItem { Id = i.Id, Text = i.Text }));
    }
}
