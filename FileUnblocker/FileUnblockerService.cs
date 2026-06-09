using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace FileUnblocker;

public enum UnblockStatus { Unblocked, AlreadyClear, Failed, ArchiveProcessed, ArchiveFailed }

public record FileUnblockResult(
    string FilePath,
  UnblockStatus Status,
    string? ErrorMessage  = null,
    bool IsArchiveEntry   = false,
 string? ArchiveSource = null);

/// <summary>Detailed result returned by <see cref="FileUnblockerService.ProcessArchive"/>.</summary>
public sealed class ArchiveProcessResult
{
    public string  ArchivePath      { get; init; } = string.Empty;
    public string  ExtractDirectory      { get; init; } = string.Empty;
public bool Success               { get; init; }
    public bool    WasPasswordProtected  { get; init; }
    public string? ErrorMessage          { get; init; }

    /// <summary>
    /// Unblock result for the archive container file itself (the .zip / .cab).
    /// Always populated once the file has been validated.
    /// Null only for early-exit failures (file not found, unsupported format)
    /// that occur before the container can be processed.
    /// </summary>
    public FileUnblockResult? ContainerUnblockResult { get; init; }

    /// <summary>All files extracted from the archive.</summary>
    public IReadOnlyList<string>         ExtractedFiles { get; init; } = [];

    /// <summary>Per-file unblock results for every extracted file.</summary>
    public IReadOnlyList<FileUnblockResult> FileResults { get; init; } = [];

    // ?? Aggregate counters ????????????????????????????????????????????????????
    public int TotalExtracted    => ExtractedFiles.Count;
    public int TotalUnblocked => FileResults.Count(r => r.Status == UnblockStatus.Unblocked);
    public int TotalAlreadyClear => FileResults.Count(r => r.Status == UnblockStatus.AlreadyClear);
    public int TotalFailed       => FileResults.Count(r => r.Status == UnblockStatus.Failed);

    /// <summary>Human-readable one-liner for the FileItem note column.</summary>
    public string Note
    {
        get
  {
            if (WasPasswordProtected)
                return "Skipped — password protected";

         // Describe what happened to the container file itself.
      var containerPart = ContainerUnblockResult?.Status switch
            {
   UnblockStatus.Unblocked    => "ZIP unblocked",
         UnblockStatus.AlreadyClear => "ZIP already clean",
     UnblockStatus.Failed       => $"ZIP unblock failed: {ContainerUnblockResult.ErrorMessage}",
     _        => string.Empty
            };

            // ZIP-only mode: no contents were extracted.
            if (TotalExtracted == 0)
   return string.IsNullOrEmpty(containerPart)
       ? $"Error: {ErrorMessage}"
   : containerPart;

 // ZIP + contents mode: describe both.
            return $"{containerPart} · {TotalExtracted} extracted · {TotalUnblocked} cleared";
        }
  }
}

/// <summary>Progress snapshot reported during archive processing.</summary>
public record ArchiveProgress(
    string Phase,
    int    Processed,
    int    Total,
    string CurrentFile);

public class FileUnblockerService
{
    private const string ZoneIdentifier = ":Zone.Identifier";

    private static readonly HashSet<string> ArchiveExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".zip", ".cab" };

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool DeleteFile(string lpFileName);

    // ?? Archive processing (extract + unblock) ?????????????????????????

    /// <summary>
    /// Processes an archive file in two distinct, ordered phases:
    ///
    /// <b>Phase 0 — Container unblock (mandatory, always runs first)</b>
    /// Removes the Zone.Identifier ADS from the .zip / .cab file itself.
    /// This step is completely independent of extraction and always executes
    /// exactly once, even if extraction is skipped or fails.
    ///
    /// <b>Phase 1 — Extraction (optional, controlled by <paramref name="expandContents"/>)</b>
    /// Extracts all entries to a unique collision-safe folder.
    ///
    /// <b>Phase 2 — Content unblock (optional, only when Phase 1 ran)</b>
    /// Removes Zone.Identifier from every extracted file.
    ///
    /// Password-protected ZIPs are detected after the container unblock so the
    /// container is always unblocked even if the contents cannot be read.
    /// </summary>
    public ArchiveProcessResult ProcessArchive(
        string archivePath,
        IProgress<ArchiveProgress>? progress = null,
      CancellationToken cancellationToken = default,
    bool expandContents = true)
    {
  if (!File.Exists(archivePath))
      return Fail(archivePath, string.Empty, $"File not found: {archivePath}");

        var ext = Path.GetExtension(archivePath).ToLowerInvariant();
        if (!ArchiveExtensions.Contains(ext))
   return Fail(archivePath, string.Empty, $"Unsupported archive format: {ext}");

        // ?? Phase 0: Unblock the archive container itself ?????????????????????
        // This MUST run before any extraction.  It is unconditional — the ZIP
  // file's own Zone.Identifier is independent of its contents.
        var containerResult = UnblockFile(archivePath);

        System.Diagnostics.Debug.WriteLine(
    $"[ProcessArchive] Container unblock: {archivePath} ? {containerResult.Status}");

 // ?? ZIP-only mode: stop here, no extraction ???????????????????????????
     if (!expandContents)
        {
     return new ArchiveProcessResult
        {
   ArchivePath           = archivePath,
     ExtractDirectory    = string.Empty,
 Success          = true,
   ContainerUnblockResult = containerResult,
   ExtractedFiles    = [],
  FileResults        = []
         };
      }

  // ?? Phase 1: Create extraction directory ??????????????????????????????
      var extractDir = BuildUniqueExtractDir(archivePath);
        try
  {
     Directory.CreateDirectory(extractDir);
        }
  catch (Exception ex)
  {
    // Extraction setup failed, but the container was already unblocked —
    // return partial success rather than discarding the container result.
    return new ArchiveProcessResult
    {
       ArchivePath   = archivePath,
     ExtractDirectory      = extractDir,
         Success        = false,
      ErrorMessage        = $"Cannot create extract folder: {ex.Message}",
  ContainerUnblockResult = containerResult,
  ExtractedFiles        = [],
  FileResults           = []
       };
 }

        // ?? Phase 1: Extract ??????????????????????????????????????????????????
List<string> extractedFiles;
     try
        {
         extractedFiles = ext == ".zip"
    ? ExtractZipStreaming(archivePath, extractDir, progress, cancellationToken)
    : ExtractCabProcess(archivePath, extractDir, progress, cancellationToken);
        }
        catch (OperationCanceledException)
  {
            // Cancelled after container was already unblocked — report that.
     return new ArchiveProcessResult
  {
         ArchivePath           = archivePath,
  ExtractDirectory   = extractDir,
    Success = false,
    ErrorMessage    = "Operation was cancelled.",
       ContainerUnblockResult = containerResult,
       ExtractedFiles        = [],
       FileResults     = []
      };
   }
    catch (InvalidDataException ex) when (IsPasswordError(ex))
  {
  // Password-protected: container was unblocked, contents cannot be read.
     return new ArchiveProcessResult
    {
          ArchivePath        = archivePath,
    ExtractDirectory     = extractDir,
     Success   = false,
        WasPasswordProtected = true,
         ErrorMessage         = "Archive is password-protected — contents skipped.",
   ContainerUnblockResult = containerResult,
   ExtractedFiles     = [],
      FileResults    = []
   };
        }
    catch (Exception ex)
     {
   return new ArchiveProcessResult
  {
 ArchivePath           = archivePath,
ExtractDirectory      = extractDir,
  Success       = false,
   ErrorMessage          = ex.Message,
    ContainerUnblockResult = containerResult,
     ExtractedFiles        = [],
    FileResults           = []
  };
 }

  // ?? Phase 2: Unblock each extracted file ?????????????????????????????
  var fileResults = new List<FileUnblockResult>(extractedFiles.Count);
    int done = 0;

        foreach (var file in extractedFiles)
   {
       cancellationToken.ThrowIfCancellationRequested();

       progress?.Report(new ArchiveProgress(
     Phase:       "Unblocking",
    Processed:   ++done,
      Total:     extractedFiles.Count,
     CurrentFile: Path.GetFileName(file)));

     var result = UnblockFile(file, archiveSource: archivePath);
            fileResults.Add(result);
        }

     return new ArchiveProcessResult
        {
       ArchivePath         = archivePath,
    ExtractDirectory    = extractDir,
   Success        = true,
   ContainerUnblockResult = containerResult,
  ExtractedFiles   = extractedFiles,
     FileResults         = fileResults
   };
    }

    // ?? Streaming ZIP extraction (entry-by-entry, low memory) ?????????

    private static List<string> ExtractZipStreaming(
        string zipPath,
        string destDir,
      IProgress<ArchiveProgress>? progress,
      CancellationToken ct)
    {
      var extracted = new List<string>();

      using var archive = ZipFile.OpenRead(zipPath);

        // Count only file entries (not directory entries)
        var fileEntries = archive.Entries
            .Where(e => !string.IsNullOrEmpty(e.Name))
 .ToList();

        int done = 0;
        foreach (var entry in fileEntries)
  {
            ct.ThrowIfCancellationRequested();

     progress?.Report(new ArchiveProgress(
      Phase:       "Extracting",
            Processed: ++done,
        Total:       fileEntries.Count,
       CurrentFile: entry.Name));

          var destPath = BuildSafeDestPath(destDir, entry.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

         // Skip if already present from a previous partial run
     if (!File.Exists(destPath))
            {
           // Extract entry-by-entry: safe for large archives
         using var entryStream = entry.Open();
    using var destStream  = File.Create(destPath);
       entryStream.CopyTo(destStream);
       }

            extracted.Add(destPath);
  }

      return extracted;
    }

    // ?? CAB extraction via expand.exe ??????????????????????????????????

    private static List<string> ExtractCabProcess(
    string cabPath,
        string destDir,
        IProgress<ArchiveProgress>? progress,
  CancellationToken ct)
    {
  progress?.Report(new ArchiveProgress("Extracting", 0, 1, Path.GetFileName(cabPath)));

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName               = "expand.exe",
     Arguments              = $"\"{cabPath}\" -F:* \"{destDir}\"",
 UseShellExecute        = false,
            CreateNoWindow    = true,
 RedirectStandardOutput = true,
   RedirectStandardError  = true
};

   using var proc = System.Diagnostics.Process.Start(psi)
    ?? throw new InvalidOperationException("Failed to start expand.exe");

        // Honour cancellation while waiting
        while (!proc.WaitForExit(500))
      ct.ThrowIfCancellationRequested();

        if (proc.ExitCode != 0)
        {
            var err = proc.StandardError.ReadToEnd().Trim();
   throw new InvalidOperationException($"expand.exe exit {proc.ExitCode}: {err}");
        }

        return Directory
    .EnumerateFiles(destDir, "*.*", SearchOption.AllDirectories)
 .ToList();
    }

    // ?? Helpers ???????????????????????????????????????????????????????

    /// <summary>
    /// Builds a unique extraction directory that never overwrites an existing one.
    /// e.g. archive_extracted, archive_extracted_1, archive_extracted_2 …
    /// </summary>
    private static string BuildUniqueExtractDir(string archivePath)
    {
        var dir  = Path.GetDirectoryName(archivePath) ?? Environment.CurrentDirectory;
        var name = Path.GetFileNameWithoutExtension(archivePath);
     var candidate = Path.Combine(dir, name + "_extracted");

if (!Directory.Exists(candidate))
   return candidate;

  for (int i = 1; i < 1000; i++)
 {
            var next = Path.Combine(dir, $"{name}_extracted_{i}");
  if (!Directory.Exists(next))
        return next;
        }

// Ultimate fallback: GUID-suffixed folder
        return Path.Combine(dir, $"{name}_extracted_{Guid.NewGuid():N}");
    }

    /// <summary>
    /// Converts a ZIP entry's FullName into a safe absolute destination path,
    /// preventing path-traversal attacks (e.g. entries with "../").
    /// </summary>
    private static string BuildSafeDestPath(string destDir, string entryFullName)
 {
        // Normalise separators and strip any leading slashes
        var relative = entryFullName
         .Replace('/', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);

        var fullDest = Path.GetFullPath(Path.Combine(destDir, relative));

        // Guard against path traversal
        if (!fullDest.StartsWith(Path.GetFullPath(destDir), StringComparison.OrdinalIgnoreCase))
   throw new InvalidOperationException($"Blocked path-traversal entry: {entryFullName}");

     return fullDest;
    }

    private static bool IsPasswordError(InvalidDataException ex) =>
        ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("encrypted", StringComparison.OrdinalIgnoreCase);

    private static ArchiveProcessResult Fail(string archivePath, string extractDir, string message) =>
        new()
        {
    ArchivePath      = archivePath,
  ExtractDirectory = extractDir,
      Success          = false,
    ErrorMessage     = message,
            ExtractedFiles   = [],
            FileResults      = []
   };

    // ?? Legacy ??????????????????????????????????????????????????????

    public static bool IsArchive(string path) =>
        ArchiveExtensions.Contains(Path.GetExtension(path));

    public List<string> ExtractArchive(string archivePath, out string extractDir)
    {
    var dir  = Path.GetDirectoryName(archivePath) ?? Environment.CurrentDirectory;
  var name = Path.GetFileNameWithoutExtension(archivePath);
        extractDir = Path.Combine(dir, name + "_extracted");
        Directory.CreateDirectory(extractDir);

    return Path.GetExtension(archivePath).ToLowerInvariant() switch
        {
      ".zip" => ExtractZipLegacy(archivePath, extractDir),
          ".cab" => ExtractCabLegacy(archivePath, extractDir),
       _      => []
        };
    }

    private static List<string> ExtractZipLegacy(string zipPath, string destDir)
    {
    var extracted = new List<string>();
        try
        {
     using var archive = ZipFile.OpenRead(zipPath);
          foreach (var entry in archive.Entries)
            {
       if (string.IsNullOrEmpty(entry.Name)) continue;
      var destPath = Path.Combine(destDir,
             entry.FullName.Replace('/', Path.DirectorySeparatorChar));
  Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                if (!File.Exists(destPath)) entry.ExtractToFile(destPath);
  extracted.Add(destPath);
      }
        }
        catch (Exception ex)
        {
          extracted.Add($"[ZIP ERROR] {zipPath} — {ex.Message}");
        }
     return extracted;
    }

    private static List<string> ExtractCabLegacy(string cabPath, string destDir)
    {
     var extracted = new List<string>();
        try
        {
    var psi = new System.Diagnostics.ProcessStartInfo
    {
         FileName   = "expand.exe",
                Arguments    = $"\"{cabPath}\" -F:* \"{destDir}\"",
       UseShellExecute        = false,
                CreateNoWindow         = true,
         RedirectStandardOutput = true,
          RedirectStandardError  = true
      };
  using var proc = System.Diagnostics.Process.Start(psi)!;
            proc.WaitForExit(30_000);
  extracted.AddRange(Directory.EnumerateFiles(destDir, "*.*", SearchOption.AllDirectories));
}
        catch (Exception ex)
        {
          extracted.Add($"[CAB ERROR] {cabPath} — {ex.Message}");
        }
        return extracted;
  }

    // ?? File collection ????????????????????????????????????????????????????????

    public List<string> CollectFiles(string path, bool recursive, bool expandArchives = false)
    {
        if (File.Exists(path))
 {
          if (expandArchives && IsArchive(path))
            {
         var entries = ExtractArchive(path, out _);
   entries.Insert(0, path);
 return entries;
    }
     return [path];
        }

        if (Directory.Exists(path))
        {
        var files = EnumerateAll(path, recursive);
   if (!expandArchives) return files;

     var expanded = new List<string>();
            foreach (var f in files)
      {
           expanded.Add(f);
           if (IsArchive(f))
         expanded.AddRange(ExtractArchive(f, out _));
            }
            return expanded.Distinct().ToList();
        }

        return [];
    }

    private static List<string> EnumerateAll(string path, bool recursive) =>
        File.Exists(path) ? [path]
        : Directory.Exists(path)
            ? Directory.EnumerateFiles(
       path, "*.*",
        recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
 .ToList()
    : [];

    // ?? Unblocking ?????????????????????????????????????????????????????????????

    public FileUnblockResult UnblockFile(string filePath, string? archiveSource = null)
    {
        var adsPath = filePath + ZoneIdentifier;
        try
     {
      bool deleted = DeleteFile(adsPath);
            if (deleted)
    return new FileUnblockResult(filePath, UnblockStatus.Unblocked,
        ArchiveSource: archiveSource,
    IsArchiveEntry: archiveSource is not null);

            int err = Marshal.GetLastWin32Error();
    if (err == 2)
      return new FileUnblockResult(filePath, UnblockStatus.AlreadyClear,
           ArchiveSource: archiveSource,
    IsArchiveEntry: archiveSource is not null);

            return new FileUnblockResult(filePath, UnblockStatus.Failed,
     "Win32 error " + err,
        ArchiveSource: archiveSource,
IsArchiveEntry: archiveSource is not null);
        }
        catch (Exception ex)
    {
            return new FileUnblockResult(filePath, UnblockStatus.Failed, ex.Message,
       ArchiveSource: archiveSource,
        IsArchiveEntry: archiveSource is not null);
        }
    }
}
