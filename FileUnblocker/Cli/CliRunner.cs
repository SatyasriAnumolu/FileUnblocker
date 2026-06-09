using System.IO;
using System.Runtime.InteropServices;

namespace FileUnblocker.Cli;

// ?? Execution mode ????????????????????????????????????????????????????????????
/// <summary>
/// Describes what the CLI is actually doing this run.
/// Every console message is gated on this value so output never implies
/// an operation that did not occur.
/// </summary>
internal enum ExecutionMode
{
    /// <summary>Unblock regular files only. No archives in the input.</summary>
    Normal,

    /// <summary>
    /// Unblock ZIP/CAB containers only.
    /// No extraction, no temp folders, archive structure is untouched.
    /// Triggered by --zip-only or when archives are found but --zips is absent.
    /// </summary>
  ZipOnly,

    /// <summary>
    /// Unblock ZIP/CAB containers AND extract + unblock every entry.
    /// Triggered by --zips without --zip-only.
    /// </summary>
    ZipWithContents,

    /// <summary>
    /// Mixed input: regular files + archives.
    /// Regular files are unblocked; archives follow ZipOnly or ZipWithContents rules.
    /// </summary>
  Mixed,

    /// <summary>
    /// Enumerate and report input; do not modify any file.
    /// Triggered by --scan-only (kept for completeness; no scan messages are emitted).
    /// </summary>
    ReportOnly,
}

/// <summary>
/// Headless CLI pipeline — no WPF types referenced.
///
/// ZIP unblocking design
/// ?????????????????????
/// Every archive always goes through ProcessArchive() which enforces:
///   Phase 0 (mandatory)   : UnblockFile(zipPath)   — removes Zone.Identifier from the ZIP itself.
///   Phase 1 (conditional) : extract contents — only when expandContents = true.
///   Phase 2 (conditional) : UnblockFile each entry  — only when Phase 1 ran.
///
/// --zip-only overrides --zips: the container is unblocked, nothing is extracted.
///
/// Exit codes
/// ??????????
///   0  All files processed (some may already be clean).
///   1  One or more files failed.
///   2  Bad arguments / no valid paths / help requested.
///   3  Unhandled exception prevented processing.
/// </summary>
internal sealed class CliRunner
{
    // ?? Win32 console attachment ??????????????????????????????????????????????
    [DllImport("kernel32.dll")] private static extern bool AttachConsole(int dwProcessId);
    [DllImport("kernel32.dll")] private static extern bool AllocConsole();

    private const int ATTACH_PARENT_PROCESS = -1;

    // ?? Parsed flags ??????????????????????????????????????????????????????????
    private readonly bool     _recursive;
    private readonly bool   _expandArchives;  // --zips
    private readonly bool     _zipOnly;      // --zip-only  (overrides --zips)
    private readonly bool     _quiet;
    private readonly bool  _scanOnly;
    private readonly string[] _rawPaths;

    // ?? Core service (pure C# / Win32, no WPF) ???????????????????????????????
    private readonly FileUnblockerService _service = new();

    // ?? Constructor ???????????????????????????????????????????????????????????
    private CliRunner(string[] rawPaths, bool recursive, bool expandArchives,
             bool zipOnly, bool quiet, bool scanOnly)
    {
        _rawPaths       = rawPaths;
        _recursive      = recursive;
        // zip-only takes precedence: unblock container, never extract.
        _zipOnly        = zipOnly;
  _expandArchives = zipOnly ? false : expandArchives;
        _quiet       = quiet;
        _scanOnly       = scanOnly;
    }

    // ?? Public entry point ????????????????????????????????????????????????????
    public static int Run(string[] args)
    {
 // Attach to the parent shell's console so output appears in-place.
        // If there is no parent (launched from Explorer / Task Scheduler),
    // AllocConsole creates a new window so output is never silently lost.
        if (!AttachConsole(ATTACH_PARENT_PROCESS))
     AllocConsole();

        try
        {
    // No args ? show help so the user learns about the tool.
       if (args.Length == 0)
            {
       PrintHelp();
     return 2;
  }

        var (paths, recursive, expandArchives, zipOnly, quiet, scanOnly, helpRequested) =
       ParseArgs(args);

            if (helpRequested)
            {
          PrintHelp();
        return 2;
        }

  // Validate paths
            var validPaths = paths
 .Select(p => p.Trim().Trim('"'))
         .Where(p => !string.IsNullOrWhiteSpace(p))
        .Where(p => File.Exists(p) || Directory.Exists(p))
    .Distinct(StringComparer.OrdinalIgnoreCase)
          .ToArray();

            if (validPaths.Length == 0)
            {
 Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine();
    Console.Error.WriteLine("  Error: no valid file or folder paths were supplied.");
       Console.Error.WriteLine("         Run with --help for usage information.");
        Console.Error.WriteLine();
      Console.Error.WriteLine("  Example:  FileUnblocker.exe C:\\Users\\You\\Downloads");
           Console.ResetColor();
    return 2;
        }

            var runner = new CliRunner(validPaths, recursive, expandArchives, zipOnly, quiet, scanOnly);
   return runner.Execute();
        }
 catch (Exception ex)
        {
         Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine();
       Console.Error.WriteLine($"  Fatal error: {ex.Message}");
            Console.ResetColor();
            return 3;
   }
        finally
        {
            Console.Out.Flush();
 Console.Error.Flush();
        }
    }

  // ?? Pipeline ??????????????????????????????????????????????????????????????
    private int Execute()
    {
        var sw    = System.Diagnostics.Stopwatch.StartNew();
 int unblocked = 0, alreadyClear = 0, failed = 0;
      var ct        = CancellationToken.None;

 // ?? Step 1: enumerate input ???????????????????????????????????????????
        // Collect all paths into regular-files and archives buckets.
        // Message text is intentionally neutral — we have not yet determined
        // the final ExecutionMode (that depends on what we find).
     var regularFiles = new List<string>();
        var archives     = new List<string>();
  int total        = 0;

        foreach (var path in _rawPaths)
        {
            try
            {
       var allFiles = _service.CollectFiles(path, _recursive, expandArchives: false);
  foreach (var file in allFiles)
    {
           total++;
  if (!_quiet && total % 50 == 0)
  Console.Write($"\r  Processing input… {total} item(s) ");

              if (FileUnblockerService.IsArchive(file))
            archives.Add(file);
    else
        regularFiles.Add(file);
      }
  }
     catch (Exception ex)
       {
     PrintError($"  Could not read \"{path}\": {ex.Message}");
  failed++;
     }
        }

        if (!_quiet) Console.WriteLine();

      // ?? Determine execution mode ??????????????????????????????????????????
        // Mode is resolved AFTER enumeration so output is always truthful.
        ExecutionMode mode;
        if (_scanOnly)
       mode = ExecutionMode.ReportOnly;
        else if (regularFiles.Count == 0 && archives.Count > 0)
       mode = _zipOnly || !_expandArchives ? ExecutionMode.ZipOnly : ExecutionMode.ZipWithContents;
     else if (regularFiles.Count > 0 && archives.Count == 0)
            mode = ExecutionMode.Normal;
        else
mode = ExecutionMode.Mixed;

        // ?? Print banner (mode is now known) ?????????????????????????????????
  PrintBanner(mode);

        // ?? Print input summary (mode-aware, no scan terminology) ????????????
        Print("", ConsoleColor.Gray);
   PrintInputSummary(mode, regularFiles.Count, archives.Count, sw.Elapsed);

        // ?? ReportOnly: list what was found, exit without modifying anything ??
        if (mode == ExecutionMode.ReportOnly)
      {
 Print("");
      Print("  --report-only: no files were modified.", ConsoleColor.Cyan);
            PrintOperationSummary(sw, mode, unblocked: 0, alreadyClear, failed);
return failed > 0 ? 1 : 0;
        }

        if (regularFiles.Count == 0 && archives.Count == 0)
        {
        Print("");
          Print("  No items found to process.", ConsoleColor.Green);
            PrintOperationSummary(sw, mode, unblocked: 0, alreadyClear, failed);
      return 0;
}

     // ?? Step 2: unblock regular files ?????????????????????????????????????
        // Only shown in Normal and Mixed modes — never in ZipOnly / ZipWithContents.
        if (regularFiles.Count > 0)
     {
         Print("", ConsoleColor.Gray);
          Print($"  Unblocking {regularFiles.Count} file(s)…", ConsoleColor.Cyan);

       int i = 0;
   foreach (var file in regularFiles)
      {
       i++;
    try
   {
          var r = _service.UnblockFile(file);
          switch (r.Status)
          {
            case UnblockStatus.Unblocked:
   unblocked++;
       if (!_quiet)
  Print($"    [OK]  {Ellipsize(file, 72)}", ConsoleColor.Green);
    break;
        case UnblockStatus.AlreadyClear:
      alreadyClear++;
          if (!_quiet)
      Print($"    [--]  {Ellipsize(file, 72)}", ConsoleColor.Gray);
       break;
        default:
           failed++;
                PrintError($"    [ERR] {Ellipsize(file, 72)} — {r.ErrorMessage}");
  break;
        }
       }
   catch (Exception ex)
 {
  failed++;
        PrintError($"    [ERR] {Ellipsize(file, 72)} — {ex.Message}");
        }

                if (_quiet && i % 100 == 0)
        Console.Write($"\r  {i}/{regularFiles.Count} processed… ");
            }

    if (_quiet) Console.WriteLine();
        }

        // ?? Step 3: archives ??????????????????????????????????????????????????
        // Phase 0 (container unblock) is unconditional inside ProcessArchive.
   // expandContents is false for ZipOnly, true for ZipWithContents.
        if (archives.Count > 0)
    {
            Print("", ConsoleColor.Gray);
      PrintArchiveHeader(mode, archives.Count);

      bool expandContents = mode == ExecutionMode.ZipWithContents
     || (mode == ExecutionMode.Mixed && _expandArchives && !_zipOnly);

     int ai = 0;
            foreach (var archivePath in archives)
       {
         ai++;
     Print($"  [{ai}/{archives.Count}] {Path.GetFileName(archivePath)}", ConsoleColor.Cyan);

                try
          {
       IProgress<ArchiveProgress>? progress = _quiet ? null
            : new Progress<ArchiveProgress>(p =>
       Console.Write(
 $"\r    {p.Phase}: {Ellipsize(p.CurrentFile, 50)} ({p.Processed}/{p.Total})   "));

               var result = _service.ProcessArchive(archivePath, progress, ct,
  expandContents: expandContents);

   if (!_quiet) Console.WriteLine();

   // ?? Container result (always present after validation) ????
    if (result.ContainerUnblockResult is { } cr)
         {
    switch (cr.Status)
              {
   case UnblockStatus.Unblocked:
         unblocked++;
       if (!_quiet)
       Print($"    [OK]  Container unblocked: {Ellipsize(archivePath, 60)}",
    ConsoleColor.Green);
 break;
      case UnblockStatus.AlreadyClear:
    alreadyClear++;
  if (!_quiet)
             Print($"    [--]  Container already clean: {Ellipsize(archivePath, 55)}",
      ConsoleColor.Gray);
           break;
    default:
           failed++;
             PrintError($"    [ERR] Container unblock failed: {cr.ErrorMessage}");
      break;
    }
  }

        // ?? ZIP-only / default: confirm contents were not touched ?
            if (!expandContents && !_quiet)
      Print("         Contents untouched — extraction was not requested.",
ConsoleColor.DarkGray);

  // ?? Extracted content results (ZipWithContents / Mixed) ???
       if (result.TotalExtracted > 0)
       {
      unblocked    += result.TotalUnblocked;
   alreadyClear += result.TotalAlreadyClear;
     failed += result.TotalFailed;

     Print($"         {result.TotalExtracted} entries extracted — " +
       $"{result.TotalUnblocked} unblocked, " +
              $"{result.TotalAlreadyClear} already clean, " +
 $"{result.TotalFailed} failed",
    result.TotalFailed > 0 ? ConsoleColor.Yellow : ConsoleColor.Green);
    }

             if (result.WasPasswordProtected)
 Print("     Contents skipped — archive is password-protected.",
     ConsoleColor.Yellow);

         if (!result.Success && result.ContainerUnblockResult is null)
     PrintError($"    Failed: {result.ErrorMessage}");
  }
           catch (Exception ex)
 {
   if (!_quiet) Console.WriteLine();
     failed++;
                    PrintError($"    [ERR] Exception processing archive: {ex.Message}");
             }
   }
        }

        PrintOperationSummary(sw, mode, unblocked, alreadyClear, failed);
      return failed > 0 ? 1 : 0;
    }

    // ?? Mode-aware output helpers ?????????????????????????????????????????????

    /// <summary>
    /// Prints a banner and the resolved execution mode.
    /// Called after mode is determined so the label is always truthful.
    /// </summary>
    private static void PrintBanner(ExecutionMode mode)
    {
  Console.ForegroundColor = ConsoleColor.DarkMagenta;
        Console.WriteLine();
        Console.WriteLine("  ????????????????????????????????????????");
        Console.WriteLine("  ?       File Unblocker  — CLI mode     ?");
    Console.WriteLine("  ????????????????????????????????????????");
        Console.ResetColor();

   var (label, color) = mode switch
   {
            ExecutionMode.ZipOnly    => ("ZIP-only          — containers unblocked, contents untouched", ConsoleColor.Yellow),
            ExecutionMode.ZipWithContents => ("ZIP + contents     — containers and entries unblocked",      ConsoleColor.Cyan),
  ExecutionMode.Normal    => ("Normal             — regular files unblocked",     ConsoleColor.Green),
ExecutionMode.Mixed           => ("Mixed              — regular files and archives unblocked",    ConsoleColor.Cyan),
   ExecutionMode.ReportOnly      => ("Report-only        — no files will be modified",           ConsoleColor.Magenta),
  _     => ("",         ConsoleColor.Gray),
   };

        if (!string.IsNullOrEmpty(label))
            Print($"  Mode: {label}", color);
    }

    /// <summary>
    /// Prints what was found in the input — wording driven entirely by mode.
    /// Never uses "scan" terminology.
    /// </summary>
    private static void PrintInputSummary(ExecutionMode mode, int regularCount,
       int archiveCount, TimeSpan elapsed)
    {
        int totalFound = regularCount + archiveCount;

        switch (mode)
        {
    case ExecutionMode.ZipOnly:
     case ExecutionMode.ZipWithContents:
   // ZIP-centric modes: talk only about archives
     Print($"  Found {archiveCount} ZIP container(s) in {elapsed.TotalSeconds:F1}s",
           ConsoleColor.Gray);
      if (regularCount > 0)
   Print($"    {regularCount} regular file(s) present but not processed in this mode.",
     ConsoleColor.DarkGray);
                break;

  case ExecutionMode.Normal:
  Print($"  Found {regularCount} file(s) in {elapsed.TotalSeconds:F1}s",
      ConsoleColor.Gray);
                break;

   case ExecutionMode.Mixed:
     Print($"  Found {totalFound} item(s) in {elapsed.TotalSeconds:F1}s",
            ConsoleColor.Gray);
  Print($"    Regular : {regularCount}", ConsoleColor.Yellow);
         Print($"    Archives: {archiveCount}", ConsoleColor.Cyan);
           break;

            case ExecutionMode.ReportOnly:
  Print($"  Found {totalFound} item(s) in {elapsed.TotalSeconds:F1}s",
          ConsoleColor.Gray);
              Print($"    Regular : {regularCount}", ConsoleColor.Yellow);
              Print($"    Archives: {archiveCount}", ConsoleColor.Cyan);
   break;
        }
}

    /// <summary>
    /// Prints the archive-processing section header — wording driven by mode.
    /// </summary>
    private static void PrintArchiveHeader(ExecutionMode mode, int count)
    {
        switch (mode)
        {
          case ExecutionMode.ZipOnly:
                Print($"  Unblocking {count} ZIP container(s)  [contents will NOT be extracted]…",
              ConsoleColor.Cyan);
                break;
   case ExecutionMode.ZipWithContents:
     Print($"  Unblocking {count} ZIP container(s) and their contents…",
         ConsoleColor.Cyan);
    break;
   case ExecutionMode.Mixed when true:
        // label depends on whether expansion was requested
    Print($"  Processing {count} archive(s)…", ConsoleColor.Cyan);
  break;
            default:
      Print($"  Processing {count} archive(s)…", ConsoleColor.Cyan);
    break;
        }
    }

    /// <summary>
    /// Prints the final operation summary — no scan terminology, ever.
    /// </summary>
  private static void PrintOperationSummary(System.Diagnostics.Stopwatch sw,
        ExecutionMode mode,
    int unblocked, int alreadyClear, int failed)
    {
        Console.WriteLine();
   Print("  ?? Operation summary ?????????????????????????", ConsoleColor.White);
     Print($"  Mode       : {ModeLabel(mode)}", ConsoleColor.Gray);
        PrintColoured($"  Unblocked    : {unblocked}",  unblocked    > 0 ? ConsoleColor.Green : ConsoleColor.Gray);
    PrintColoured($"  Already clean: {alreadyClear}", ConsoleColor.Gray);
        PrintColoured($"  Failed       : {failed}",       failed       > 0 ? ConsoleColor.Red   : ConsoleColor.Gray);
      PrintColoured($"  Elapsed      : {sw.Elapsed.TotalSeconds:F2}s", ConsoleColor.DarkGray);
        Print("  ??????????????????????????????????????????????", ConsoleColor.White);
   Console.WriteLine();
    }

    private static string ModeLabel(ExecutionMode mode) => mode switch
    {
        ExecutionMode.ZipOnly         => "ZIP-only (container unblocked, contents untouched)",
        ExecutionMode.ZipWithContents => "ZIP + contents (container and entries unblocked)",
    ExecutionMode.Normal          => "Normal (regular files)",
    ExecutionMode.Mixed => "Mixed (regular files + archives)",
   ExecutionMode.ReportOnly      => "Report-only (no files modified)",
        _   => mode.ToString(),
    };

    // ?? Help ?????????????????????????????????????????????????????????????????
    private static void PrintHelp()
    {
        Console.WriteLine();
 Print("  ????????????????????????????????????????????", ConsoleColor.DarkMagenta);
    Print("  ?File Unblocker  — command-line help   ?", ConsoleColor.DarkMagenta);
        Print("  ????????????????????????????????????????????", ConsoleColor.DarkMagenta);
        Console.WriteLine();

        Print("  USAGE", ConsoleColor.White);
    Console.WriteLine("    FileUnblocker.exe [options] <path> [<path2> ...]");
        Console.WriteLine("    FileUnblocker.exe --gui      (open the graphical window)");
  Console.WriteLine();

Print("  OPTIONS", ConsoleColor.Cyan);
        Console.WriteLine("    -r,  --recursive     Recurse into sub-folders (default: on)");
        Console.WriteLine("         --no-recursive  Disable sub-folder recursion");
     Console.WriteLine("    -z,  --zips          Unblock ZIP container AND extract/unblock contents");
      Console.WriteLine("       --zip-only      Unblock ZIP container only — do NOT extract contents");
        Console.WriteLine("    -s,  --report-only   List items without modifying any of them");
      Console.WriteLine("    -q,  --quiet     Suppress per-file output; show summary only");
        Console.WriteLine("    -h,  --help, /?   Show this help");
   Console.WriteLine("         --gui Open the graphical window instead");
        Console.WriteLine();

        Print("  ZIP UNBLOCKING RULES", ConsoleColor.Cyan);
     Console.WriteLine("    The ZIP file itself (the container) is ALWAYS unblocked.");
        Console.WriteLine("    --zip-only  ? unblock ZIP, leave contents untouched (no extraction).");
   Console.WriteLine("    --zips      ? unblock ZIP, then extract and unblock every entry.");
   Console.WriteLine("    (neither)   ? same as --zip-only (safe default).");
        Console.WriteLine();

        Print("  EXIT CODES", ConsoleColor.Cyan);
        Console.WriteLine("    0  All files processed successfully");
        Console.WriteLine("    1  One or more files failed");
        Console.WriteLine("    2  Bad arguments / no valid paths / help shown");
   Console.WriteLine("    3  Unhandled exception");
     Console.WriteLine();

        Print("  EXAMPLES", ConsoleColor.DarkGray);
        Console.WriteLine("    FileUnblocker.exe C:\\Downloads");
        Console.WriteLine("    FileUnblocker.exe --zip-only  C:\\Downloads\\file.zip");
      Console.WriteLine("    FileUnblocker.exe -z          C:\\Downloads\\file.zip");
        Console.WriteLine("    FileUnblocker.exe -r -z   C:\\Downloads");
        Console.WriteLine("    FileUnblocker.exe --report-only C:\\Projects");
        Console.WriteLine("    FileUnblocker.exe --gui");
        Console.WriteLine();
    }

    // ?? Generic output helpers ????????????????????????????????????????????????
    private static void Print(string text, ConsoleColor color = ConsoleColor.Gray)
    {
    Console.ForegroundColor = color;
        Console.WriteLine(text);
     Console.ResetColor();
    }

    private static void PrintColoured(string text, ConsoleColor color)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(text);
     Console.ResetColor();
    }

    private static void PrintError(string text)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine(text);
        Console.ResetColor();
    }

    private static string Ellipsize(string s, int max) =>
    s.Length <= max ? s : "…" + s[^(max - 1)..];

    // ?? Argument parser ???????????????????????????????????????????????????????
    private static (List<string> paths,
          bool recursive,
     bool expandArchives,
   bool zipOnly,
         bool quiet,
       bool scanOnly,
 bool helpRequested)
        ParseArgs(string[] args)
    {
        var  paths          = new List<string>();
     bool recursive  = true;    // matches GUI default
        bool expandArchives = false;
        bool zipOnly        = false;
        bool quiet      = false;
  bool scanOnly    = false;
        bool helpRequested  = false;

        foreach (var arg in args)
        {
            switch (arg.ToLowerInvariant())
            {
   case "-h":
     case "--help":
          case "/?":           helpRequested  = true;  break;

          case "--gui":        /* handled in Program.cs — ignore here */ break;

     case "-r":
                case "--recursive":  recursive      = true;  break;
        case "--no-recursive": recursive    = false; break;

case "-z":
      case "--zips": expandArchives = true;  break;
                case "--zip-only":   zipOnly        = true;  break;

   case "-q":
       case "--quiet":      quiet          = true;  break;

         case "-s":
case "--scan-only":
          case "--report-only": scanOnly      = true;  break;

      default:
           // Unknown flags starting with - warn rather than silently ignore.
        if (arg.StartsWith('-'))
          {
          Console.ForegroundColor = ConsoleColor.Yellow;
   Console.Error.WriteLine($"  Warning: unknown option '{arg}' — ignored.");
            Console.ResetColor();
 }
 else
      {
          paths.Add(arg.Trim().Trim('"'));
         }
          break;
       }
   }

        return (paths, recursive, expandArchives, zipOnly, quiet, scanOnly, helpRequested);
    }
}
