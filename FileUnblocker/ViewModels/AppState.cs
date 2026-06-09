namespace FileUnblocker.ViewModels;

/// <summary>
/// Authoritative application lifecycle state.
/// All UI behaviour (button enables, drag acceptance, progress visibility)
/// derives exclusively from this value — never from ad-hoc boolean flags.
///
/// Legal transitions:
///   Idle          ??? Dragging        (DragEnter over drop zone)
///   Idle          ??? Unblocking      (Unblock started)
///   Dragging      ??? Idle            (DragLeave / drop rejected)
///   Dragging      ??? DropProcessing  (valid Drop received)
///   DropProcessing??? Idle         (paths validated and queued)
///   Unblocking    ??? Finalizing      (all work done, about to show summary)
///   Unblocking    ??? Idle         (cancelled)
///   Finalizing    ??? Idle            (summary dialog dismissed)
///   *      ??? Error  (unhandled exception during operation)
///   Error         ??? Idle            (user acknowledges / automatic recovery)
/// </summary>
public enum AppState
{
    /// <summary>Ready to accept new files or start a new operation.</summary>
    Idle,

    /// <summary>
    /// User is actively dragging files over the drop zone.
    /// Buttons remain enabled; only the visual drop affordance changes.
    /// </summary>
    Dragging,

    /// <summary>
    /// Dropped paths are being validated and queued.
    /// This is a brief synchronous transient — typically sub-millisecond.
    /// </summary>
    DropProcessing,

    /// <summary>Files are actively being unblocked on a background thread.</summary>
    Unblocking,

    /// <summary>
    /// All workers have finished and the result summary dialog is being shown modally.
    /// No new operation may start until the dialog is dismissed and state returns to Idle.
    /// </summary>
    Finalizing,

    /// <summary>
    /// A non-fatal exception occurred during an operation.
    /// The app resets to Idle automatically after logging the error.
    /// </summary>
    Error
}
