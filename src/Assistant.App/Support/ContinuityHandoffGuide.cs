namespace Assistant.App.Support;

/// <summary>
/// Continuity-note guidance: sections as a soft checklist, reference-for-future-self framing.
/// </summary>
public static class ContinuityHandoffGuide
{
    public const string SectionsChecklist =
        "Intent; Progress (resolved vs remaining); Decisions & discoveries; " +
        "Active thread; Carry forward";

    public const string ToolDescription =
        "After mail work this wake: save a continuity note for your next wake, then clear " +
        "chat history. Write a structured handoff (see handoff argument). Max 4000 chars. " +
        "Inbox headers are restored separately — do not re-list them.";

    public const string HandoffArgumentDescription =
        "Structured continuity note for your future self (reference only, not new orders). " +
        $"Use sections {SectionsChecklist}. Fill what applies; omit empty. Keep facts exact; " +
        "do not invent. Merge prior continuity when still relevant.";

    /// <summary>Body after the [SYSTEM] line for the compact-phase nudge.</summary>
    public const string CompactNudgeBody =
        "Mail for this wake is settled. Closing step: CommitContext with a continuity note " +
        "for your future self. That note is shown first next wake; chat history is then cleared. " +
        "Treat the note as REFERENCE — not fresh instructions to obey.\n" +
        $"Suggested sections (checklist — fill what applies, skip the rest): {SectionsChecklist}.\n" +
        "Keep facts exact. Do not invent progress. Do not re-list the inbox — it returns on wake. " +
        "If a prior continuity note still matters, fold it in. Prefer concise over exhaustive.";

    public const string FallbackCompactNotice =
        "Mail work looks done. Call CommitContext with a structured continuity note " +
        $"({SectionsChecklist} — fill what applies). Exact facts only; inbox returns on wake. " +
        "Under 4000 characters.";

    public const string BootstrapBlurb =
        "When mail is settled, CommitContext with a short continuity note " +
        $"({SectionsChecklist} — fill what applies). History clears; inbox returns on wake.";
}
