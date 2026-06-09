namespace FileUnblocker.ViewModels;

/// <summary>
/// Immutable data shown in the post-processing summary dialog.
/// </summary>
public sealed class ResultSummaryViewModel
{
    // ?? Raw counts ????????????????????????????????????????????????????????
    public int Unblocked     { get; init; }
    public int AlreadyClear  { get; init; }
    public int ArchivesProcessed { get; init; }
    public int Failed   { get; init; }
    public TimeSpan Elapsed  { get; init; }

    // ?? Derived display ???????????????????????????????????????????????????
    public int  Total       => Unblocked + AlreadyClear + ArchivesProcessed + Failed;
    public bool HasFailures => Failed > 0;
    public bool HasArchives => ArchivesProcessed > 0;
    public bool AllClear    => Failed == 0;

    /// <summary>One-line headline shown at the top of the dialog.</summary>
    public string Headline => AllClear
        ? (Unblocked > 0 ? $"{Unblocked} file{(Unblocked == 1 ? "" : "s")} unblocked successfully" : "All files were already clean — nothing to do")
        : $"Done — {Failed} file{(Failed == 1 ? "" : "s")} couldn't be unblocked";

    /// <summary>Sub-headline colour hex.</summary>
    public string HeadlineColour => AllClear ? "#10B981" : "#F87171";

    /// <summary>Elapsed time formatted as mm:ss.</summary>
    public string ElapsedDisplay =>
        Elapsed.TotalSeconds < 1
      ? "< 1 second"
            : Elapsed.TotalMinutes >= 1
     ? $"{(int)Elapsed.TotalMinutes}m {Elapsed.Seconds}s"
      : $"{Elapsed.Seconds}s";

    /// <summary>Success-rate percentage string.</summary>
    public string SuccessRateDisplay => Total > 0
        ? $"{Math.Round((Unblocked + AlreadyClear + ArchivesProcessed) * 100.0 / Total, 1)}%"
        : "—";
}
