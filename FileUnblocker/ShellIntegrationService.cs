using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace FileUnblocker;

public static class ShellIntegrationService
{
    private const string VerbKey    = @"Software\Classes\*\shell\FileUnblocker";
    private const string CommandKey = @"Software\Classes\*\shell\FileUnblocker\command";
    private const string VerbText   = "Unblock with File Unblocker";

    // ?? Reliable exe path ?????????????????????????????????????????????????
    public static string ExePath
    {
get
        {
  // Environment.ProcessPath is the most reliable on .NET 6+
      var path = Environment.ProcessPath;
   if (!string.IsNullOrEmpty(path) && File.Exists(path))
     return path;

   // Fallback: current process module
            path = Process.GetCurrentProcess().MainModule?.FileName;
  if (!string.IsNullOrEmpty(path) && File.Exists(path))
       return path;

        // Last resort: AppContext.BaseDirectory (works in single-file publish)
          return Path.Combine(AppContext.BaseDirectory, "FileUnblocker.exe");
        }
    }

    // ?? Query ?????????????????????????????????????????????????????????????
public static bool IsRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(VerbKey);
        return key is not null;
    }

    /// <summary>
    /// Returns the exe path stored in the registry command, or null if not registered.
    /// Useful for diagnosing stale registrations pointing to old paths.
    /// </summary>
    public static string? GetRegisteredExePath()
    {
        using var key = Registry.CurrentUser.OpenSubKey(CommandKey);
     var cmd = key?.GetValue(string.Empty) as string;
        if (string.IsNullOrEmpty(cmd)) return null;
        // Command is: "C:\path\app.exe" "%1"  — extract just the exe portion
        cmd = cmd.Trim();
        if (cmd.StartsWith('"'))
     {
   var end = cmd.IndexOf('"', 1);
            if (end > 1) return cmd[1..end];
        }
      return cmd.Split(' ')[0];
    }

    // ?? Register ?????????????????????????????????????????????????????????
public static void Register()
    {
        string exe = ExePath;

    // Verb key
        using (var verbKey = Registry.CurrentUser.CreateSubKey(VerbKey, writable: true))
        {
         verbKey.SetValue(string.Empty, VerbText);
            verbKey.SetValue("Icon", $"\"{exe}\",0");
            // "Player" = Explorer calls the app ONCE per selected file as a separate process.
   // We use the IPC pipe to funnel subsequent calls into the running instance.
            verbKey.SetValue("MultiSelectModel", "Player");
        }

      // Command key — wrap exe and %1 in quotes to handle spaces in paths
using (var cmdKey = Registry.CurrentUser.CreateSubKey(CommandKey, writable: true))
        {
       cmdKey.SetValue(string.Empty, $"\"{exe}\" \"%1\"");
        }
    }

    // ?? Unregister ???????????????????????????????????????????????????????
    public static void Unregister()
    {
        Registry.CurrentUser.DeleteSubKeyTree(VerbKey, throwOnMissingSubKey: false);
    }

    // ?? Toggle ???????????????????????????????????????????????????????????
    public static bool Toggle()
    {
   if (IsRegistered()) { Unregister(); return false; }
        Register(); return true;
    }

    // ?? Self-heal: re-register if the exe path has changed ???????????????
    /// <summary>
    /// Call on startup. If the app is already registered but the stored exe path
    /// differs from the current exe path (e.g. after moving/updating), re-registers.
    /// </summary>
    public static void RefreshRegistrationIfNeeded()
    {
        if (!IsRegistered()) return;
  var stored  = GetRegisteredExePath();
        var current = ExePath;
        if (!string.Equals(stored, current, StringComparison.OrdinalIgnoreCase))
            Register();
    }
}
