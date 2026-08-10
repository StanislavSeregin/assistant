namespace Assistant.App.Support;

/// <summary>
/// Continuity-note guidance: short checklist for the next wake (personal OS rhythm).
/// </summary>
public static class ContinuityHandoffGuide
{
    public const string SectionsChecklist =
        "Waiting; Open ask; Facts to keep; Next step";

    public const string ToolDescription =
        "After mail work this wake: save a short continuity note for your next wake, " +
        "then clear chat history. See handoff argument. Inbox headers return separately — " +
        "do not re-list them.";

    public const string HandoffArgumentDescription =
        "Short continuity note for your future self (reference only, not new orders). " +
        $"Sections: {SectionsChecklist}. Fill what applies; omit empty. " +
        "Briefing, not transcript: exact load-bearing facts; open waits; next step. " +
        "Prefer clear and short. Do not park undelivered outbound here — use ReplyMail / WriteMail.";

    /// <summary>Body after the [SYSTEM] line for the compact-phase nudge.</summary>
    public const string CompactNudgeBody =
        "Mail for this wake is settled. Call CommitContext with a short continuity note " +
        $"({SectionsChecklist} — fill what applies). " +
        "Note is REFERENCE for next wake, then history clears. Briefing not transcript. " +
        "Do not re-list inbox. Outbound goes via ReplyMail / WriteMail, not the note.";

    public const string FallbackCompactNotice =
        "Mail work looks done. Call CommitContext with a short continuity note " +
        $"({SectionsChecklist} — fill what applies). Briefing not transcript; inbox returns on wake.";

    public const string BootstrapBlurb =
        "When mail is settled, CommitContext with a short continuity note " +
        $"({SectionsChecklist}). Prefer clear and short; history clears; inbox returns on wake.";
}
