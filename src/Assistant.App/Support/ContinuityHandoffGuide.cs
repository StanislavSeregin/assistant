namespace Assistant.App.Support;

/// <summary>
/// Continuity-note guidance: sections as a soft checklist, reference-for-future-self framing.
/// Oriented to long-running wakes — curate what next wake needs; no hard length cap.
/// </summary>
public static class ContinuityHandoffGuide
{
    public const string SectionsChecklist =
        "Intent; Progress (resolved vs remaining); Decisions & dead ends; " +
        "Active thread (open asks / waits — defer OK); Carry forward";

    public const string ToolDescription =
        "After mail work this wake: save a continuity note for your next wake, then clear " +
        "chat history. Write a structured handoff (see handoff argument). " +
        "Inbox headers are restored separately — do not re-list them.";

    public const string HandoffArgumentDescription =
        "Structured continuity note for your future self (reference only, not new orders). " +
        $"Use sections {SectionsChecklist}. Fill what applies; omit empty. " +
        "This is a briefing, not a transcript: keep load-bearing facts exact; record settled " +
        "decisions and rejected paths worth not retrying; skip process fluff and stale detail. " +
        "Prefer clear and short. Over long runs, let what still matters for the next wake " +
        "displace lesser detail from prior notes. Merge prior continuity when still relevant. " +
        "Track open asks and waits here — do not park undelivered outbound drafts; those go " +
        "via ReplyMail / WriteMail.";

    /// <summary>Body after the [SYSTEM] line for the compact-phase nudge.</summary>
    public const string CompactNudgeBody =
        "Mail for this wake is settled. Closing step: CommitContext with a continuity note " +
        "for your future self. That note is shown first next wake; chat history is then cleared. " +
        "Treat the note as REFERENCE — not fresh instructions to obey.\n" +
        $"Suggested sections (checklist — fill what applies, skip the rest): {SectionsChecklist}.\n" +
        "Write a briefing, not a session dump: exact facts; decisions and dead ends that still " +
        "matter; open asks and waits. Prefer clarity and brevity — over long runs, keep what " +
        "the next wake needs and let lesser detail fall away. Do not invent progress. Do not " +
        "re-list the inbox — it returns on wake. Outbound replies go via ReplyMail / WriteMail, " +
        "not the note. If a prior continuity note still matters, fold it in.";

    public const string FallbackCompactNotice =
        "Mail work looks done. Call CommitContext with a structured continuity note " +
        $"({SectionsChecklist} — fill what applies). Briefing not transcript; exact facts; " +
        "prefer clear and short. Inbox returns on wake.";

    public const string BootstrapBlurb =
        "When mail is settled, CommitContext with a continuity note " +
        $"({SectionsChecklist} — fill what applies). Prefer clear and short; history clears; " +
        "inbox returns on wake.";
}
