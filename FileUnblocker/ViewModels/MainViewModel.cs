using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using System.Threading;

namespace FileUnblocker.ViewModels;

/// <summary>Snapshot of processing state reported on every file tick.</summary>
public record ProcessingProgress(
    int      Processed,
    int      Total,
    string   CurrentFile,
    double   FilesPerSec,
    TimeSpan Elapsed,
    TimeSpan? Eta,
    string   Phase        // "Scanning" | "Unblocking"
);

public sealed class MainViewModel : INotifyPropertyChanged
{
    // ?? Services ?????????????????????????????????????????????????????????????
    private readonly FileUnblockerService _service = new();

    // ?? Collections ??????????????????????????????????????????????????????????
    public ObservableCollection<FileItem> Items { get; } = [];
    public ICollectionView ItemsView { get; }

    // ?? Authoritative application state machine ???????????????????????????????
    private AppState _appState = AppState.Idle;

    /// <summary>
    /// The single source of truth for what the application is currently doing.
    /// All CanExecute predicates and UI visibility bindings derive from this.
    /// </summary>
    public AppState AppState
    {
  get => _appState;
        private set
        {
     if (_appState == value) return;
            _appState = value;
            OnProp(nameof(AppState));
            OnProp(nameof(IsBusy));
    OnProp(nameof(IsIdle));
            OnProp(nameof(IsDragging));
            // Refresh every command's CanExecute whenever state changes.
       RaiseAllCommandsCanExecuteChanged();
  }
 }

    /// <summary>True while any background operation is active (not safe to start another).</summary>
    public bool IsBusy => _appState is AppState.Unblocking
       or AppState.Finalizing
     or AppState.DropProcessing;

    /// <summary>True only when fully idle and ready for user interaction.</summary>
    public bool IsIdle => _appState == AppState.Idle;

    /// <summary>True while the user is hovering a drag over the drop zone.</summary>
    public bool IsDragging => _appState == AppState.Dragging;

    /// <summary>True when the results list is empty — drives the empty-state overlay.</summary>
    public bool IsEmpty => Items.Count == 0;

    // ?? Filter / Sort ?????????????????????????????????????????????????????????
    public static IReadOnlyList<string> FilterOptions { get; } =
  ["All", "Blocked", "Unblocked", "Already Clear", "Failed", "Archives"];

    private string _selectedFilter = "All";
    public string SelectedFilter
    {
        get => _selectedFilter;
   set { _selectedFilter = value; OnProp(nameof(SelectedFilter)); ItemsView.Refresh(); }
}

    private string _selectedSort = "File Name";
    public string SelectedSort
    {
        get => _selectedSort;
    set { _selectedSort = value; OnProp(nameof(SelectedSort)); ApplySort(); }
    }

    private bool _sortDescending;
    public bool SortDescending
    {
        get => _sortDescending;
  set { _sortDescending = value; OnProp(nameof(SortDescending)); ApplySort(); }
    }

    // ?? Options ???????????????????????????????????????????????????????????????
    private bool _recursive = true;
    public bool Recursive
  {
        get => _recursive;
      set { _recursive = value; OnProp(nameof(Recursive)); }
    }

    private bool _expandArchives = true;
    public bool ExpandArchives
    {
        get => _expandArchives;
        set { _expandArchives = value; OnProp(nameof(ExpandArchives)); }
    }

    // ?? Real-time feedback properties ?????????????????????????????????????????
    private string _currentFile = string.Empty;
    public string CurrentFile
    {
get => _currentFile;
   private set { _currentFile = value; OnProp(nameof(CurrentFile)); }
    }

    private int _progressPercent;
    public int ProgressPercent
    {
     get => _progressPercent;
        private set
        {
       _progressPercent = value;
            OnProp(nameof(ProgressPercent));
            OnProp(nameof(ProgressLabel));
        }
    }

    private int _processedCount;
    public int ProcessedCount
    {
        get => _processedCount;
   private set { _processedCount = value; OnProp(nameof(ProcessedCount)); }
    }

    private int _totalCount;
    public int TotalCount
    {
     get => _totalCount;
    private set { _totalCount = value; OnProp(nameof(TotalCount)); }
    }

    private double _filesPerSecond;
    public double FilesPerSecond
    {
        get => _filesPerSecond;
  private set { _filesPerSecond = value; OnProp(nameof(FilesPerSecond)); OnProp(nameof(SpeedLabel)); }
    }

private string _elapsedDisplay = "00:00";
    public string ElapsedDisplay
    {
  get => _elapsedDisplay;
        private set { _elapsedDisplay = value; OnProp(nameof(ElapsedDisplay)); }
    }

    private string _etaDisplay = "--:--";
    public string EtaDisplay
    {
        get => _etaDisplay;
        private set { _etaDisplay = value; OnProp(nameof(EtaDisplay)); }
    }

    private string _processingPhase = string.Empty;
    public string ProcessingPhase
{
        get => _processingPhase;
     private set { _processingPhase = value; OnProp(nameof(ProcessingPhase)); }
    }

    public string ProgressLabel =>
        TotalCount > 0 ? $"{ProcessedCount} / {TotalCount}  ({ProgressPercent}%)" : string.Empty;

    public string SpeedLabel =>
 FilesPerSecond > 0 ? $"{FilesPerSecond:F1} files/sec" : string.Empty;

    // ?? Status / header ???????????????????????????????????????????????????????
    private string _statusText = "Ready";
    public string StatusText
    {
 get => _statusText;
        private set { _statusText = value; OnProp(nameof(StatusText)); }
}

    private int _scanProgress;
    public int ScanProgress
  {
        get => _scanProgress;
        private set { _scanProgress = value; OnProp(nameof(ScanProgress)); }
    }

    private string _headerStatus = "Ready";
    public string HeaderStatus
    {
        get => _headerStatus;
        private set { _headerStatus = value; OnProp(nameof(HeaderStatus)); }
    }

// ?? Dashboard counters ????????????????????????????????????????????????????
    private int _totalScanned;
    public int TotalScanned { get => _totalScanned; private set { _totalScanned = value; OnProp(nameof(TotalScanned)); } }

    private int _unblockedCount;
    public int UnblockedCount
    {
        get => _unblockedCount;
 private set { _unblockedCount = value; OnProp(nameof(UnblockedCount)); OnProp(nameof(SuccessRate)); }
  }

    private int _failedCount;
    public int FailedCount { get => _failedCount; private set { _failedCount = value; OnProp(nameof(FailedCount)); } }

    // ?? Per-run trends ????????????????????????????????????????????????????????
    private int _prevTotal, _prevUnblocked, _prevFailed;

    private string Trend(int current, int prev)
    {
        int delta = current - prev;
    return delta > 0 ? $"+{delta} this run" : delta < 0 ? $"{delta} this run" : string.Empty;
  }

    public string TrendTotal     => Trend(TotalScanned,   _prevTotal);
    public string TrendUnblocked => Trend(UnblockedCount, _prevUnblocked);
    public string TrendFailed    => Trend(FailedCount,    _prevFailed);

    public ICommand FilterByCardCommand { get; }

    public string SuccessRate => TotalScanned > 0
        ? $"{Math.Round(UnblockedCount * 100.0 / TotalScanned, 1)}% success rate"
    : "0% success rate";

    private DateTime? _lastOperationTime;
    public string LastOperationTime =>
     _lastOperationTime.HasValue ? _lastOperationTime.Value.ToString("HH:mm:ss") : "\u2014";

    private string _lastOperationDetail = "No operations yet";
    public string LastOperationDetail
    {
   get => _lastOperationDetail;
        private set { _lastOperationDetail = value; OnProp(nameof(LastOperationDetail)); }
    }

    // ?? Cancellation ??????????????????????????????????????????????????????????
    /// <summary>
 /// Always replaced (and the old one disposed) at the start of every operation.
    /// Never shared between concurrent operations — state machine prevents that.
    /// </summary>
    private CancellationTokenSource? _cts;

    // ?? Commands ??????????????????????????????????????????????????????????????
    public ICommand UnblockCommand        { get; }
    public ICommand BrowseFolderCommand   { get; }
    public ICommand BrowseFilesCommand    { get; }
    public ICommand ClearCommand          { get; }
    public ICommand CancelCommand         { get; }
    public ICommand CopyReportCommand     { get; }
    public ICommand ExportReportCommand   { get; }
    public ICommand RetryItemCommand      { get; }
    public ICommand OpenFolderItemCommand { get; }
    public ICommand CopyPathItemCommand   { get; }
    public ICommand ToggleSortCommand     { get; }

    private readonly List<string> _pendingPaths = [];

    // ?? Constructor ???????????????????????????????????????????????????????????
    public MainViewModel()
    {
 ItemsView = CollectionViewSource.GetDefaultView(Items);
   ItemsView.Filter = FilterItem;
  ApplySort();

        Items.CollectionChanged += (_, _) => OnProp(nameof(IsEmpty));

        UnblockCommand      = new AsyncRelayCommand(RunUnblockAsync,    () => IsIdle && _pendingPaths.Count > 0);
        BrowseFolderCommand   = new RelayCommand(BrowseFolder,  _ => IsIdle);
        BrowseFilesCommand= new RelayCommand(BrowseFiles, _ => IsIdle);
        ClearCommand    = new RelayCommand(Clear);
        CancelCommand         = new RelayCommand(_ => RequestCancel(), _ => IsBusy);
   CopyReportCommand     = new RelayCommand(_ => SafeSetClipboard(BuildReport()), _ => IsIdle);
      ExportReportCommand   = new RelayCommand(ExportReport,  _ => IsIdle);
        RetryItemCommand      = new AsyncRelayCommand<FileItem>(RetryItemAsync);
        OpenFolderItemCommand = new RelayCommand<FileItem>(OpenFolderItem);
    CopyPathItemCommand   = new RelayCommand<FileItem>(CopyPathItem);
        FilterByCardCommand   = new RelayCommand<string>(filter => SelectedFilter = filter ?? "All");
    ToggleSortCommand     = new RelayCommand<string>(sortKey =>
        {
          if (sortKey == null) return;
       if (_selectedSort == sortKey)
         SortDescending = !SortDescending;
   else
            {
     _selectedSort = sortKey;
                OnProp(nameof(SelectedSort));
 _sortDescending = false;
   OnProp(nameof(SortDescending));
    ApplySort();
     }
        });
    }

    // ?? State machine helpers ?????????????????????????????????????????????????

    /// <summary>
    /// Transitions to the requested state and re-evaluates all command guards.
    /// Centralised so every transition is logged and auditable.
  /// </summary>
    private void Transition(AppState next)
    {
        System.Diagnostics.Debug.WriteLine($"[AppState] {_appState} ? {next}");
        AppState = next;
    }

    /// <summary>
    /// Resets the application to <see cref="AppState.Idle"/> regardless of
    /// the current state.  Safe to call from finally blocks.
    /// </summary>
    private void ReturnToIdle(string statusText = "Ready", string headerStatus = "Ready")
    {
  IsIndeterminate = false;
        CurrentFile     = string.Empty;
        StatusText      = statusText;
        HeaderStatus    = headerStatus;
        Transition(AppState.Idle);
    }

    /// <summary>
    /// Transitions to <see cref="AppState.Error"/>, logs the message, then
    /// immediately recovers to Idle so the app remains usable.
    /// </summary>
    private void HandleOperationError(Exception ex, string context)
    {
        System.Diagnostics.Debug.WriteLine($"[Error:{context}] {ex}");
        Transition(AppState.Error);
    ReturnToIdle(
            statusText:   $"Error during {context} — app recovered.",
 headerStatus: "Error (recovered)");
    }

    private void RaiseAllCommandsCanExecuteChanged()
    {
        ((AsyncRelayCommand)UnblockCommand).RaiseCanExecuteChanged();
        ((RelayCommand)BrowseFolderCommand).RaiseCanExecuteChanged();
        ((RelayCommand)BrowseFilesCommand).RaiseCanExecuteChanged();
        ((RelayCommand)CancelCommand).RaiseCanExecuteChanged();
        ((RelayCommand)CopyReportCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ExportReportCommand).RaiseCanExecuteChanged();
    }

    // ?? Drag state management (called from MainWindow code-behind) ?????????????
    /// <summary>
    /// Called by the view when a valid drag enters the drop zone.
    /// Transitions to Dragging only from Idle — ignores drags that arrive
    /// during an active operation.
    /// </summary>
    public void NotifyDragEnter()
    {
     if (_appState == AppState.Idle)
    Transition(AppState.Dragging);
    }

 /// <summary>
    /// Called by the view when the drag leaves the drop zone or is cancelled.
    /// </summary>
    public void NotifyDragLeave()
    {
        if (_appState == AppState.Dragging)
            Transition(AppState.Idle);
    }

    /// <summary>
  /// Called by the view when files are dropped.
    /// Validates, queues, and returns to Idle.
    /// </summary>
    public void NotifyDrop(IEnumerable<string>? paths)
    {
        // Only accept drops when idle or in drag-hover state.
 if (_appState is not (AppState.Idle or AppState.Dragging)) return;

        try
        {
        Transition(AppState.DropProcessing);
 AddPaths(paths);
        }
        catch (Exception ex)
        {
  System.Diagnostics.Debug.WriteLine($"[Drop] Validation error: {ex.Message}");
        }
        finally
        {
     // Always return to Idle — DropProcessing is a transient state.
  ReturnToIdle(
   statusText:   _pendingPaths.Count > 0 ? $"{_pendingPaths.Count} path(s) queued" : "Ready",
        headerStatus: _pendingPaths.Count > 0 ? $"{_pendingPaths.Count} file(s) queued" : "Ready");
 }
    }

    // ?? Cancellation ??????????????????????????????????????????????????????????
    private void RequestCancel()
{
        try { _cts?.Cancel(); }
      catch (ObjectDisposedException) { /* already cancelled and disposed — safe to ignore */ }
    }

 /// <summary>
    /// Creates a fresh <see cref="CancellationTokenSource"/>, disposing the previous one.
    /// Must be called at the start of every new operation.
    /// </summary>
    private CancellationToken BeginOperation()
    {
        // Dispose the previous CTS to avoid handle leaks.
        try { _cts?.Dispose(); } catch { /* best-effort */ }
  _cts = new CancellationTokenSource();
        return _cts.Token;
    }

    // ?? Progress helper ???????????????????????????????????????????????????????
    private IProgress<ProcessingProgress> MakeProgressReporter() =>
        new Progress<ProcessingProgress>(p =>
        {
            ProcessedCount  = p.Processed;
            TotalCount      = p.Total;
        CurrentFile     = p.CurrentFile;
     FilesPerSecond  = Math.Round(p.FilesPerSec, 1);
          ProgressPercent = p.Total > 0 ? (int)(p.Processed * 100.0 / p.Total) : 0;
            ElapsedDisplay  = $"{(int)p.Elapsed.TotalMinutes:D2}:{p.Elapsed.Seconds:D2}";
     EtaDisplay      = p.Eta.HasValue
     ? $"{(int)p.Eta.Value.TotalMinutes:D2}:{p.Eta.Value.Seconds:D2}"
       : "--:--";
         ProcessingPhase = p.Phase;
        ScanProgress    = ProgressPercent;
       HeaderStatus    = $"{p.Phase} {p.Processed}/{p.Total} \u2014 {FilesPerSecond:F1} files/sec";
   StatusText      = $"{p.Phase}: {p.CurrentFile}";
     });

    private void ResetFeedback()
    {
        CurrentFile   = string.Empty;
        ProgressPercent = 0;
        ProcessedCount  = 0;
    TotalCount      = 0;
        FilesPerSecond  = 0;
   ElapsedDisplay  = "00:00";
      EtaDisplay      = "--:--";
        ProcessingPhase = string.Empty;
     ScanProgress    = 0;
    }

 // ?? Filter ????????????????????????????????????????????????????????????????
  private bool FilterItem(object obj)
    {
        if (obj is not FileItem item) return false;
        return SelectedFilter switch
      {
          "Blocked"       => item.IsBlocked && item.Status != UnblockStatus.Unblocked,
            "Unblocked"     => item.Status == UnblockStatus.Unblocked,
        "Already Clear" => item.Status == UnblockStatus.AlreadyClear,
            "Failed"        => item.Status == UnblockStatus.Failed
    || item.Status == UnblockStatus.ArchiveFailed,
   "Archives"      => item.IsArchive,
  _        => true
        };
    }

 // ?? Sort ??????????????????????????????????????????????????????????????????
    private void ApplySort()
    {
  ItemsView.SortDescriptions.Clear();
    var dir = _sortDescending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        string prop = SelectedSort switch
        {
         "Size"     => nameof(FileItem.SizeBytes),
     "Status"   => nameof(FileItem.StatusLabel),
            "Modified" => nameof(FileItem.LastModified),
          _          => nameof(FileItem.FileName)
        };
        ItemsView.SortDescriptions.Add(new SortDescription(prop, dir));
 }

    // ?? Per-row actions ???????????????????????????????????????????????????????
  private async Task RetryItemAsync(FileItem? item)
  {
    if (item is null) return;
        try
        {
      item.Status = UnblockStatus.AlreadyClear;
     var result = await Task.Run(() => _service.UnblockFile(item.FullPath));
   item.Status       = result.Status;
            item.ErrorMessage = result.ErrorMessage;
        if (result.Status == UnblockStatus.Unblocked)  UnblockedCount++;
  else if (result.Status == UnblockStatus.Failed) FailedCount++;
        }
        catch (Exception ex)
        {
        // Retry failure is non-fatal — mark the row as failed and recover.
       item.Status= UnblockStatus.Failed;
    item.ErrorMessage = ex.Message;
        System.Diagnostics.Debug.WriteLine($"[RetryItem] {ex.Message}");
    }
    }

    private static void OpenFolderItem(FileItem? item)
    {
        if (item is null) return;
        try
        {
       var dir = Path.GetDirectoryName(item.FullPath);
            if (dir is not null && Directory.Exists(dir))
      System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{item.FullPath}\"");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OpenFolder] {ex.Message}");
  }
    }

    private static void CopyPathItem(FileItem? item)
 {
        if (item?.FullPath is not null)
     SafeSetClipboard(item.FullPath);
    }

    private static void SafeSetClipboard(string text)
    {
        try { System.Windows.Clipboard.SetText(text); }
    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Clipboard] {ex.Message}"); }
    }

    // ?? Browse helpers ????????????????????????????????????????????????????????
    private void BrowseFolder(object? _)
    {
        if (!IsIdle) return;   // belt-and-suspenders — CanExecute already gates this
      try
        {
            var dlg = new OpenFolderDialog { Title = "Select folder" };
 if (dlg.ShowDialog() == true) AddPaths([dlg.FolderName]);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[BrowseFolder] {ex.Message}"); }
    }

    private void BrowseFiles(object? _)
    {
        if (!IsIdle) return;
  try
        {
         var dlg = new OpenFileDialog
 {
             Title      = "Select files",
      Multiselect = true,
            Filter     = "All files (*.*)|*.*"
            };
            if (dlg.ShowDialog() == true) AddPaths(dlg.FileNames);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[BrowseFiles] {ex.Message}"); }
    }

    // ?? AddPaths (public — called by drag-drop, browse, and IPC) ?????????????
    public void AddPaths(IEnumerable<string>? paths)
    {
        if (paths is null) return;

        foreach (var raw in paths)
        {
    var p = raw?.Trim();
         if (string.IsNullOrEmpty(p)) continue;
          if (!File.Exists(p) && !Directory.Exists(p)) continue;
  if (!_pendingPaths.Contains(p, StringComparer.OrdinalIgnoreCase))
 _pendingPaths.Add(p);
     }

      if (_pendingPaths.Count == 0) return;

        StatusText   = $"{_pendingPaths.Count} path(s) queued";
        HeaderStatus = $"{_pendingPaths.Count} file(s) queued";
 ((AsyncRelayCommand)UnblockCommand).RaiseCanExecuteChanged();
    }

    // ?? Unblocking ????????????????????????????????????????????????????????????
    private async Task RunUnblockAsync()
    {
  if (_pendingPaths.Count == 0) return;

    var ct = BeginOperation();
        Transition(AppState.Unblocking);
 ResetFeedback();
  IsIndeterminate = false;
        ProcessingPhase = "Unblocking";

        var runSw = System.Diagnostics.Stopwatch.StartNew();
        int unblocked = 0, alreadyClear = 0, failed = 0;
 var paths = _pendingPaths.ToArray();
    _pendingPaths.Clear();

        var archivePaths = paths.Where(FileUnblockerService.IsArchive).ToArray();
        var regularPaths = paths.Where(p => !FileUnblockerService.IsArchive(p)).ToArray();
        List<string> regularFiles = [];

        try
{
      // ?? Phase 1: collect regular files ????????????????????????????
            if (regularPaths.Length > 0)
            {
 try
     {
                    await Task.Run(() =>
         {
      foreach (var p in regularPaths)
            {
              ct.ThrowIfCancellationRequested();
  regularFiles.AddRange(_service.CollectFiles(p, Recursive, expandArchives: false));
   }
       regularFiles = [.. regularFiles.Distinct()];
               }, ct);
        }
                catch (OperationCanceledException) { goto Cancelled; }
     catch (Exception ex) when (ex is not OperationCanceledException)
                {
          HandleOperationError(ex, "file collection");
       return;
 }

  var progress = MakeProgressReporter();
          var sw       = System.Diagnostics.Stopwatch.StartNew();
     int total    = regularFiles.Count;
             TotalCount   = total;
     StatusText   = $"Unblocking {total} file(s)…";

  for (int i = 0; i < total; i++)
            {
             if (ct.IsCancellationRequested) goto Cancelled;

     var file = regularFiles[i];
         FileUnblockResult result;
        try
    {
                result = await Task.Run(() => _service.UnblockFile(file), ct);
                 }
   catch (OperationCanceledException) { goto Cancelled; }
          catch (Exception ex)
    {
      // One file failing must not abort the whole batch.
      System.Diagnostics.Debug.WriteLine($"[Unblock:{file}] {ex.Message}");
    result = new FileUnblockResult(file, UnblockStatus.Failed, ex.Message);
        failed++;
        }

          if (ShouldReportProgress(i + 1))
      ReportProgress(progress, i + 1, total, Path.GetFileName(file), sw, "Unblocking");

        try { UpsertRow(file, result); }
             catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[UpsertRow] {ex.Message}"); }

             if      (result.Status == UnblockStatus.Unblocked)   unblocked++;
          else if (result.Status == UnblockStatus.AlreadyClear) alreadyClear++;
            else if (result.Status != UnblockStatus.Failed) { /* already counted */ }

        UnblockedCount = unblocked;
    FailedCount    = failed;
  }
            }

            // ?? Phase 2: archives ?????????????????????????????????????????
  int archiveTotal = archivePaths.Length;
            for (int ai = 0; ai < archiveTotal; ai++)
      {
                if (ct.IsCancellationRequested) goto Cancelled;

     var archivePath = archivePaths[ai];
 int archNum     = ai + 1;
                ProcessingPhase = $"Archive {archNum}/{archiveTotal}";
    ProgressPercent = regularFiles.Count > 0
               ? 100
        : (int)(ai * 100.0 / archiveTotal);

            var archiveRow = Items.FirstOrDefault(r => r.FullPath == archivePath);
                var archiveProgress = new Progress<ArchiveProgress>(p =>
           {
        if (!ShouldReportProgress(p.Processed)) return;
        StatusText   = $"{p.Phase}: {p.CurrentFile}  ({p.Processed}/{p.Total})";
          HeaderStatus = $"{p.Phase} archive {archNum}/{archiveTotal}…";
    CurrentFile  = p.CurrentFile;
  FilesPerSecond = 0;
          double archiveSlice = 100.0 / archiveTotal;
   double archiveBase  = ai * archiveSlice;
          double innerPct = p.Total > 0 ? (double)p.Processed / p.Total : 0;
       ScanProgress    = (int)(archiveBase + innerPct * archiveSlice);
           ProgressPercent = ScanProgress;
          });

                ArchiveProcessResult result;
 try
     {
       result = await Task.Run(() =>
     _service.ProcessArchive(
   archivePath,
       archiveProgress,
ct,
         // expandContents drives whether ZIP contents are extracted.
      // The container (the .zip) is ALWAYS unblocked
      // regardless of this flag — that happens inside ProcessArchive
      // Phase 0 unconditionally.
  expandContents: ExpandArchives), ct);
    }
                catch (OperationCanceledException) { goto Cancelled; }
          catch (Exception ex)
              {
          System.Diagnostics.Debug.WriteLine($"[Archive:{archivePath}] {ex.Message}");
           if (archiveRow is not null)
                {
             archiveRow.Status       = UnblockStatus.ArchiveFailed;
                archiveRow.ErrorMessage = ex.Message;
        }
    failed++;
            FailedCount = failed;
             continue;
  }

     try
  {
  // ?? Account for the ZIP container itself ??????????????
     // Phase 0 of ProcessArchive always unblocks the container.
        // Count it in the run totals and update its row in the table.
      if (result.ContainerUnblockResult is { } cr)
   {
       switch (cr.Status)
      {
        case UnblockStatus.Unblocked:
  unblocked++;
      break;
      case UnblockStatus.AlreadyClear:
 alreadyClear++;
          break;
     default:
     failed++;
       break;
              }

     if (archiveRow is not null)
       {
        // If extraction succeeded, show ArchiveProcessed;
   // if ZIP-only or extraction failed, reflect the container status.
     archiveRow.Status = result.Success
        ? UnblockStatus.ArchiveProcessed
     : cr.Status == UnblockStatus.Unblocked || cr.Status == UnblockStatus.AlreadyClear
  ? UnblockStatus.ArchiveProcessed   // container ok, contents skipped
    : UnblockStatus.ArchiveFailed;
           archiveRow.ArchiveNote        = result.Note;
     archiveRow.ExtractedFileCount = result.TotalExtracted;
   archiveRow.ErrorMessage       = result.Success ? null : result.ErrorMessage;
  }
            }
      else if (archiveRow is not null)
         {
          // ContainerUnblockResult is null only for very early failures
   // (file not found / unsupported format) — mark as failed.
    archiveRow.Status    = UnblockStatus.ArchiveFailed;
       archiveRow.ArchiveNote  = result.Note;
     archiveRow.ErrorMessage = result.ErrorMessage;
      failed++;
 }

      // ?? Account for extracted file results ????????????????
  if (result.Success && result.TotalExtracted > 0)
            {
          foreach (var fr in result.FileResults)
      {
        var existing = Items.FirstOrDefault(r => r.FullPath == fr.FilePath);
         if (existing is null)
Items.Add(new FileItem(fr.FilePath,
        isBlocked: fr.Status == UnblockStatus.Unblocked,
                  isArchive: false)
  { Status = fr.Status, ErrorMessage = fr.ErrorMessage });
            else
    {
           existing.Status    = fr.Status;
       existing.ErrorMessage = fr.ErrorMessage;
         }
           }
 unblocked    += result.TotalUnblocked;
       alreadyClear += result.TotalAlreadyClear;
       failed     += result.TotalFailed;
     }
             }
 catch (Exception ex)
       {
         System.Diagnostics.Debug.WriteLine($"[Archive:PostProcess:{archivePath}] {ex.Message}");
        failed++;
       }

           UnblockedCount = unblocked;
      FailedCount    = failed;
  }

         goto Done;

    // ?? Cancellation path ?????????????????????????????????????????
Cancelled:
     ReturnToIdle("Operation cancelled.", "Cancelled");
     ProgressPercent = 0;
            ScanProgress    = 0;
     return;

        // ?? Completion path ???????????????????????????????????????????
Done:
          _lastOperationTime = DateTime.Now;
        OnProp(nameof(LastOperationTime));
            LastOperationDetail = $"{unblocked} unblocked · {alreadyClear} clear · {failed} failed";

    _prevTotal   = TotalScanned;
  _prevUnblocked = UnblockedCount;
    _prevFailed    = FailedCount;
    OnProp(nameof(TrendTotal)); OnProp(nameof(TrendUnblocked));
    OnProp(nameof(TrendFailed));

            // Transition to Finalizing BEFORE showing the dialog so no new
     // operation can start until the dialog is dismissed.
  Transition(AppState.Finalizing);

       StatusText      = $"Finished. {unblocked} file(s) unblocked.";
            HeaderStatus= failed > 0 ? $"Done — {failed} error(s)" : "All done ?";
       ProgressPercent = 100;
        ScanProgress    = 100;
     EtaDisplay      = "Done";

    try
        {
  ProcessingCompleted?.Invoke(new ResultSummaryViewModel
         {
 Unblocked       = unblocked,
          AlreadyClear      = alreadyClear,
ArchivesProcessed = archivePaths.Length > 0
 ? archivePaths.Count(p => Items.Any(r =>
        r.FullPath == p && r.Status == UnblockStatus.ArchiveProcessed))
     : 0,
       Failed  = failed,
          Elapsed = runSw.Elapsed
      });
       }
        catch (Exception ex)
 {
          System.Diagnostics.Debug.WriteLine($"[ProcessingCompleted] {ex.Message}");
   }
      finally
       {
   ReturnToIdle(StatusText, HeaderStatus);
        }
 }
        catch (Exception ex)
 {
            HandleOperationError(ex, "Unblocking");
    }
}

    // ?? Row upsert helper ?????????????????????????????????????????????????????
    private void UpsertRow(string file, FileUnblockResult result)
    {
var row = Items.FirstOrDefault(r => r.FullPath == file);
 if (row is not null)
   {
         row.Status       = result.Status;
 row.ErrorMessage = result.ErrorMessage;
        }
        else
{
            Items.Add(new FileItem(file,
        isBlocked: result.Status == UnblockStatus.Unblocked,
                isArchive: FileUnblockerService.IsArchive(file))
    { Status = result.Status, ErrorMessage = result.ErrorMessage });
        }
    }

    // ?? Progress shorthand ????????????????????????????????????????????????????
    private static void ReportProgress(
        IProgress<ProcessingProgress> progress,
        int processed, int total, string currentFile,
        System.Diagnostics.Stopwatch sw, string phase)
    {
        var elapsed = sw.Elapsed;
        var fps       = elapsed.TotalSeconds > 0 ? processed / elapsed.TotalSeconds : 0;
 var remaining = fps > 0 ? TimeSpan.FromSeconds((total - processed) / fps) : (TimeSpan?)null;
   progress.Report(new ProcessingProgress(processed, total, currentFile, fps, elapsed, remaining, phase));
    }

    // ?? Indeterminate mode ????????????????????????????????????????????????????
 private bool _isIndeterminate;
    public bool IsIndeterminate
    {
        get => _isIndeterminate;
     private set { _isIndeterminate = value; OnProp(nameof(IsIndeterminate)); }
    }

    // ?? Progress throttle ?????????????????????????????????????????????????????
    private static readonly TimeSpan ProgressThrottle = TimeSpan.FromMilliseconds(33);
 private DateTime _lastProgressTick = DateTime.MinValue;

    private bool ShouldReportProgress(int processed)
    {
      var now = DateTime.UtcNow;
        if (processed == 1 || (now - _lastProgressTick) >= ProgressThrottle)
        {
            _lastProgressTick = now;
  return true;
        }
        return false;
  }

    // ?? Clear ?????????????????????????????????????????????????????????????????
    private void Clear(object? _)
    {
  RequestCancel();
        Items.Clear();
        _pendingPaths.Clear();
        ResetCounters();
        ResetFeedback();
        ReturnToIdle();
}

    private void ResetCounters()
    {
        TotalScanned = UnblockedCount = FailedCount = 0;
     _lastOperationTime = null;
        OnProp(nameof(LastOperationTime));
        LastOperationDetail = "No operations yet";
        _prevTotal = _prevUnblocked = _prevFailed = 0;
        OnProp(nameof(TrendTotal)); OnProp(nameof(TrendUnblocked));
     OnProp(nameof(TrendFailed));
    }

    // ?? Report ????????????????????????????????????????????????????????????????
    private string BuildReport()
    {
  var rows = Items.Where(r => r.Status == UnblockStatus.Unblocked).ToList();
        var sb   = new System.Text.StringBuilder();
        sb.AppendLine("=======================================================");
        sb.AppendLine("  FILE UNBLOCKER - UNBLOCKED FILES REPORT");
        sb.AppendLine($"  Generated : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"  Total     : {rows.Count} file(s) unblocked");
        sb.AppendLine("=======================================================\n");
        if (rows.Count == 0) { sb.AppendLine("No files were unblocked."); return sb.ToString(); }
        foreach (var grp in rows.GroupBy(r => r.IsArchive ? "Archives" : "Standalone"))
{
         sb.AppendLine($"  {(grp.Key == "Archives" ? "From archives:" : "Standalone files:")}");
  sb.AppendLine(new string('-', 53));
      foreach (var r in grp)
   {
   sb.AppendLine($"  [OK] {r.FileName}");
     sb.AppendLine($"       Path     : {r.FullPath}");
       sb.AppendLine($"       Size     : {r.SizeDisplay}");
                sb.AppendLine($"  Modified : {r.ModifiedDisplay}\n");
    }
        }
        sb.AppendLine("=======================================================");
        return sb.ToString();
    }

    private void ExportReport(object? _)
    {
        if (!IsIdle) return;
        try
        {
            var dlg = new SaveFileDialog
       {
     Title      = "Save unblock report",
   Filter     = "Text file (*.txt)|*.txt",
        FileName   = $"UnblockReport_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
  DefaultExt = ".txt"
          };
            if (dlg.ShowDialog() != true) return;
     File.WriteAllText(dlg.FileName, BuildReport());
  }
        catch (Exception ex)
    {
            System.Diagnostics.Debug.WriteLine($"[ExportReport] {ex.Message}");
        }
    }

    // ?? Processing-complete notification ??????????????????????????????????????
    public event Action<ResultSummaryViewModel>? ProcessingCompleted;

    // ?? INotifyPropertyChanged ????????????????????????????????????????????????
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnProp(String name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
