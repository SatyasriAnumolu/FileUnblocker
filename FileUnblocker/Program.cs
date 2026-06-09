using System.Runtime.InteropServices;

namespace FileUnblocker;

/// <summary>
/// Custom application entry point — the single place that decides between
/// headless CLI mode and WPF GUI mode."""

/// Self-unblock:
///   The very first thing Main() does is remove its own Zone.Identifier ADS.
///   When the exe is copied from another machine or downloaded, Windows marks
///   it as blocked.  A blocked exe may fail to load native dependencies or
///   trigger SmartScreen warnings.  Unblocking ourselves is harmless if already
///   clean (DeleteFile returns false with ERROR_FILE_NOT_FOUND = 2).
///
/// Decision rules (evaluated in order):
///   1. --gui flag present       ? GUI mode  (explicit override)
///   2. No arguments at all? GUI mode  (double-click / shortcut / F5)
///   3. Any flag (starts with - or /)
///      that is NOT --gui       ? CLI mode
///   4. Any arg that is a valid path on disk ? CLI mode
///   5. Unrecognisable args only ? GUI mode  (App.OnStartup handles them)
///
/// To see CLI help without opening a window:
///   FileUnblocker.exe --help
///
/// CLI mode:
///   • No WPF App, no Dispatcher, no resource dictionaries.
///   • CliRunner.Run() performs all work and returns an exit code.
///   • Process exits immediately after Run() returns.
///
/// GUI mode:
///   • Normal WPF startup (App.InitializeComponent + App.Run).
///   • Command-line paths forwarded to the VM after the window loads.
///   • CLI flags (--gui, --help, etc.) are stripped before forwarding so
///     they are never mistaken for file paths inside App.OnStartup.
/// </summary>
internal static class Program
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool DeleteFile(string lpFileName);

    [STAThread]   // required for both WPF and COM drag-drop interop
    static int Main(string[] args)
  {
        // ?? Self-unblock ??????????????????????????????????????????????????????
  // Remove Zone.Identifier from our own exe (and the .pdb if present).
        // This is the very first operation so that native WPF DLLs extracted
    // from the single-file bundle are not prevented from loading.
  // If the exe is already clean, DeleteFile simply returns false with
      // GetLastError() == 2 (file not found) — completely harmless.
        try
        {
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath))
            {
       DeleteFile(exePath + ":Zone.Identifier");

   // Also unblock any companion files in the same directory
       // (e.g. .pdb, or native DLLs if the publish wasn't fully bundled).
  var dir = System.IO.Path.GetDirectoryName(exePath);
  if (dir != null)
                {
   foreach (var file in System.IO.Directory.EnumerateFiles(dir))
       {
              DeleteFile(file + ":Zone.Identifier");
  }
    }
            }
        }
     catch
{
            // Best-effort — if we can't unblock ourselves, continue anyway.
            // The user may have explicitly unblocked via Properties dialog.
        }

        // ?? Normalise args ????????????????????????????????????????????????????
        var cleanArgs = args
            .Select(a => a.Trim().Trim('"'))
     .Where(a => !string.IsNullOrWhiteSpace(a))
      .ToArray();

        // ?? Rule 1: --gui ? explicit GUI launch ???????????????????????????????
        if (cleanArgs.Any(a => a.Equals("--gui", StringComparison.OrdinalIgnoreCase)))
            return RunWpf();

        // ?? Rule 2: no args ? open the GUI window ?????????????????????????????
if (cleanArgs.Length == 0)
     return RunWpf();

        // ?? Rules 3 & 4: any flag or valid path ? CLI ????????????????????????
   if (IsCli(cleanArgs))
         return Cli.CliRunner.Run(cleanArgs);

        // ?? Rule 5: unrecognisable args ? fall through to GUI ????????????????
        return RunWpf();
    }

    // ?????????????????????????????????????????????????????????????????????????
    private static bool IsCli(string[] args)
    {
        foreach (var arg in args)
      {
    if (arg.Equals("--gui", StringComparison.OrdinalIgnoreCase)) continue;
            if (arg.StartsWith('-') || arg.StartsWith('/')) return true;
        if (System.IO.File.Exists(arg) || System.IO.Directory.Exists(arg)) return true;
     }
        return false;
    }

    // ?????????????????????????????????????????????????????????????????????????
    private static int RunWpf()
    {
   var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
