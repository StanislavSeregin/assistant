namespace Assistant.App.Support;

/// <summary>
/// Continuity-note and turn-rhythm copy: one operational model, permitting language.
/// Full rhythm lives in standing; wake stays short and state-focused.
/// </summary>
public static class ContinuityHandoffGuide
{
    public const string SectionsChecklist =
        "Waiting; Open ask; Facts to keep";

    public const string StoresBlurb =
        "Stores: Checklist* = open work plan (multi-step; one-shot may skip). " +
        $"Continuity note = briefing after history clears ({SectionsChecklist}). " +
        "Mail = delivery to parent or children. " +
        "Thinking is private.";

    public const string EpisodeBlurb =
        "Episode: make progress (mail tool that delivers/clears, or Checklist* change that alters the plan), " +
        "then CommitContext. " +
        "If work remains (open checklist or a parent ask still in your inbox), another episode follows.";

    /// <summary>How to use Checklist* across episodes — permitting cheat sheet.</summary>
    public const string MultiStepBlurb =
        "Multi-step ask: ChecklistSet an ordered plan — one concrete outcome per item. " +
        "Each episode: advance about one item (do the work, ChecklistComplete it), then CommitContext. " +
        "Replan with ChecklistAdd/Remove when reality changes.";

    public const string DoneWhenBlurb =
        "Done when: open checklist is empty and no parent ask remains in your inbox. " +
        "Waiting for a new parent message is Idle. " +
        "[SYSTEM] notices are not inbox asks.";

    public const string ParentAskBlurb =
        "Parent ask stays open in the inbox until a final ReplyMail closes it. " +
        "Optional mid-work: WriteMail a short progress note to the parent (does not close the ask). " +
        "Child or other inbox mail may be handled anytime.";

    public const string ChildReportBlurb =
        "Finished child report: integrate, then DisposeSubagent — no ReplyMail to the child " +
        "(that only opens a new thread). ReplyMail a child only to answer a clarifying question.";

    public const string ToolDescription =
        "End this episode: save a short continuity note, then clear chat history. " +
        "Inbox and checklist return on the next episode — no need to re-list them. " +
        $"Note sections: {SectionsChecklist}.";

    public const string HandoffArgumentDescription =
        "Briefing for your next episode (reference only). " +
        $"Sections: {SectionsChecklist} — fill what applies; omit empty. " +
        "Facts and waits, not a todo list (the plan is Checklist*). Keep it short.";

    public const string CompactNudgeBody =
        "Progress for this episode is done. Call CommitContext with a short continuity note " +
        $"({SectionsChecklist}). Inbox and checklist return after history clears.";

    public const string FallbackCompactNotice =
        "Call CommitContext with a short continuity note " +
        $"({SectionsChecklist}). Briefing only; plan stays on Checklist*.";

    /// <summary>Standing instructions only — full rhythm once, including Done when.</summary>
    public const string BootstrapBlurb =
        $"{StoresBlurb} {EpisodeBlurb} {MultiStepBlurb} {DoneWhenBlurb} " +
        $"{ParentAskBlurb} {ChildReportBlurb} " +
        "Final ReplyMail carries the answer (outcome, paths, gaps as your role needs).";

    /// <summary>Wake footer — short; Done when / multi-step live in standing only.</summary>
    public const string WakeFooter =
        "Wake: load your role skill if named. Make episode progress, then CommitContext.";
}
