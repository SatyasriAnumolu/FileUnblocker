using System.ComponentModel;
using System.IO;

namespace FileUnblocker.ViewModels;

/// <summary>
/// Observable model for a single scanned / unblocked file row.
/// Populated by SmartScan; Status updated after unblocking.
/// </summary>
public sealed class FileItem : INotifyPropertyChanged
{
    // ?? Identity ??????????????????????????????????????????????????????????

    public string FullPath      { get; }
    public string FileName      { get; }
  public string Directory     { get; }
    public long SizeBytes    { get; }
    public DateTime? LastModified { get; }

    // ?? Classification ????????????????????????????????????????????????????

    public bool IsBlocked { get; }
    public bool IsArchive { get; }

    // ?? Archive processing details ????????????????????????????????????????

    private string _archiveNote = string.Empty;
    /// <summary>
    /// Set after ProcessArchive completes — e.g. "Extracted &amp; Unblocked · 12 files · 10 cleared".
    /// </summary>
  public string ArchiveNote
    {
        get => _archiveNote;
        set
        {
            if (_archiveNote == value) return;
_archiveNote = value;
       OnPropertyChanged(nameof(ArchiveNote));
 OnPropertyChanged(nameof(NoteDisplay));
        }
    }

 private int _extractedFileCount;
public int ExtractedFileCount
    {
        get => _extractedFileCount;
        set { _extractedFileCount = value; OnPropertyChanged(nameof(ExtractedFileCount)); }
    }

    // ?? Status (mutable, raises PropertyChanged) ??????????????????????????

    private UnblockStatus _status;
    public UnblockStatus Status
    {
        get => _status;
        set
        {
            if (_status == value) return;
     _status = value;
        OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(StatusLabel));
       OnPropertyChanged(nameof(StatusIcon));
        OnPropertyChanged(nameof(StatusColour));
     }
    }

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        set { _errorMessage = value; OnPropertyChanged(nameof(ErrorMessage)); OnPropertyChanged(nameof(NoteDisplay)); }
    }

    // ?? Display helpers ???????????????????????????????????????????????????

    /// <summary>File-type emoji derived from the file extension.</summary>
    public string FileTypeEmoji
    {
  get
        {
var ext = Path.GetExtension(FileName).ToLowerInvariant();
         return ext switch
    {
      ".exe" or ".msi" or ".dll"    => "EXE",
     ".zip" or ".cab" or ".7z" or ".rar"       => "ZIP",
     ".jpg" or ".jpeg" or ".png" or ".gif"
         or ".bmp" or ".webp" or ".svg"     => "IMG",
        ".mp4" or ".mkv" or ".avi" or ".mov"
        or ".wmv"       => "VID",
       ".mp3" or ".wav" or ".flac" or ".aac"       => "AUD",
      ".pdf"            => "PDF",
         ".doc" or ".docx" or ".odt"                 => "DOC",
                ".xls" or ".xlsx" or ".csv"             => "XLS",
     ".txt" or ".log" or ".md"     => "TXT",
       ".ps1" or ".bat" or ".cmd" or ".sh"         => "SCR",
    ".json" or ".xml" or ".yaml" or ".toml"     => "CFG",
        _ => "FILE"
   };
        }
    }

    public string TypeIcon => IsArchive ? "ZIP" : IsBlocked ? "BLK" : "OK";

    public string StatusIcon => Status switch
    {
        UnblockStatus.Unblocked        => "\u2714",   // ?
      UnblockStatus.AlreadyClear     => "\u2013",   // –
        UnblockStatus.ArchiveProcessed => "\u2714",   // ?
        UnblockStatus.ArchiveFailed    => "\u2716",   // ?
        _          => "\u2716"    // ?
    };

    public string StatusLabel => Status switch
    {
        UnblockStatus.Unblocked        => "Unblocked",
      UnblockStatus.AlreadyClear     => "Already clean",
   UnblockStatus.ArchiveProcessed => "Archive unblocked",
        UnblockStatus.ArchiveFailed    => "Archive error",
        _    => "Failed"
    };

    public string StatusColour => Status switch
    {
        UnblockStatus.Unblocked        => "#10B981",
        UnblockStatus.AlreadyClear     => "#9096B0",   // was #6B7280 — lifted to ~4.6:1 for WCAG AA
 UnblockStatus.ArchiveProcessed => "#A78BFA",
        UnblockStatus.ArchiveFailed    => "#F59E0B",
        _             => "#F87171"
    };

    public string SizeDisplay => SizeBytes switch
    {
      < 1_024       => $"{SizeBytes} B",
        < 1_048_576     => $"{SizeBytes / 1_024.0:F1} KB",
        < 1_073_741_824 => $"{SizeBytes / 1_048_576.0:F1} MB",
     _         => $"{SizeBytes / 1_073_741_824.0:F2} GB"
    };

    public string ModifiedDisplay =>
        LastModified.HasValue ? LastModified.Value.ToString("yyyy-MM-dd HH:mm:ss") : "\u2014";

    /// <summary>
    /// Note shown in the table Note column.
    /// For archives shows the archive summary; for regular files shows error text.
  /// </summary>
    public string NoteDisplay
    {
        get
        {
         if (!string.IsNullOrEmpty(_archiveNote)) return _archiveNote;
   return _errorMessage ?? string.Empty;
        }
    }
  // ?? Constructor ???????????????????????????????????????????????????????

    public FileItem(string fullPath, bool isBlocked, bool isArchive)
    {
        FullPath  = fullPath;
        FileName  = Path.GetFileName(fullPath);
Directory = Path.GetDirectoryName(fullPath) ?? fullPath;
        IsBlocked = isBlocked;
    IsArchive = isArchive;

        if (File.Exists(fullPath))
        {
var fi  = new FileInfo(fullPath);
  SizeBytes  = fi.Length;
      LastModified = fi.LastWriteTime;
  }

        // Default display status based on scan classification
      _status = isBlocked || isArchive
      ? UnblockStatus.AlreadyClear   // will be updated after unblock
: UnblockStatus.AlreadyClear;
    }

    // ?? INotifyPropertyChanged ????????????????????????????????????????????

    public event PropertyChangedEventHandler? PropertyChanged;
  private void OnPropertyChanged(string name) =>
   PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
