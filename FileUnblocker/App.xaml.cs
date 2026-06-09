using System.Windows;

namespace FileUnblocker;

public partial class App : Application
{
    internal static readonly SingleInstanceCoordinator Ipc = new();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ── Global unhandled-exception handlers ───────────────────────────
        // These are last-resort safety nets.  They log the exception and keep
        // the application alive instead of letting Windows terminate the process.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            System.Diagnostics.Debug.WriteLine($"[UnhandledException] {ex}");
            // Do NOT call Shutdown() here — let the app continue if possible.
        };

        DispatcherUnhandledException += (_, args) =>
        {
            System.Diagnostics.Debug.WriteLine($"[DispatcherUnhandledException] {args.Exception}");
            // Mark handled = true to prevent WPF from terminating the process.
            args.Handled = true;
        };

        // ── App startup ───────────────────────────────────────────────────
        // Auto-heal shell registration if exe was moved/updated
        ShellIntegrationService.RefreshRegistrationIfNeeded();

        // Build the list of real file/folder paths from the command line.
        //
        // IMPORTANT: strip every argument that starts with '-' or '/' before
        // treating anything as a file path.  Without this filter, GUI flags
        // such as --gui would pass the whitespace check, enter argPaths, and
        // be forwarded to TrySendToHostAsync — which then sees a connected host,
        // calls Shutdown(), and the window never opens.
        //
        // Rules:
        //   • Drop anything that starts with '-' or '/' (option flags).
        //   • Drop anything that is null / whitespace.
        //   • Keep only values that resolve to an existing file or directory.
        var argPaths = e.Args
            .Select(a => a.Trim().Trim('"'))
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Where(a => !a.StartsWith('-') && !a.StartsWith('/'))   // ← strip CLI flags
            .Where(a => System.IO.File.Exists(a) || System.IO.Directory.Exists(a))
            .ToList();

        // ── Single-instance: forward paths to an already-running host ─────
        if (argPaths.Count > 0)
        {
            bool forwarded = await SingleInstanceCoordinator.TrySendToHostAsync(argPaths);
            if (forwarded)
            {
                Shutdown();
                return;
            }
        }

        // ── This is the host instance ─────────────────────────────────────
        Ipc.StartServer();

        var window = new MainWindow();
        window.Show();

        // Queue CLI paths and start processing after window is fully loaded
        if (argPaths.Count > 0)
        {
            // Discard the Task intentionally — we want fire-and-forget at
            // Loaded priority; assigning to _ suppresses CS4014.
            _ = window.Dispatcher.InvokeAsync(() =>
            {
                if (window.DataContext is ViewModels.MainViewModel vm)
                {
                    vm.AddPaths(argPaths);
                    if (vm.UnblockCommand.CanExecute(null))
                        vm.UnblockCommand.Execute(null);
                }
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // ── Wire IPC for subsequent shell invocations ─────────────────────
        Ipc.PathsReceived += paths =>
        {
            // Guard: the window could be closing when a second instance forwards paths.
            if (window is null || !window.IsLoaded) return;

            window.Dispatcher.Invoke(() =>
            {
                // Secondary null-check on the UI thread — the window reference is
                // closed over and could have been GC'd between the outer check and
                // this invocation in extreme teardown scenarios.
                if (window.DataContext is not ViewModels.MainViewModel vm) return;

                try
                {
                    vm.AddPaths(paths);
                    if (vm.UnblockCommand.CanExecute(null))
                        vm.UnblockCommand.Execute(null);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[IPC PathsReceived] {ex.Message}");
                }

                window.Activate();
                window.WindowState = WindowState.Normal;
            });
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Ipc.Dispose();
        base.OnExit(e);
    }
}

