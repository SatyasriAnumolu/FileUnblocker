using System.IO;
using System.IO.Pipes;
using System.Text;

namespace FileUnblocker;

/// <summary>
/// Lightweight named-pipe single-instance coordinator.
///
/// When the app is launched for the first time it becomes the <em>host</em>:
/// it starts a background pipe server that listens for incoming path lists.
///
/// If a second instance is launched (e.g. from the shell verb) it becomes a
/// <em>client</em>: it serialises all command-line arguments as a
/// newline-delimited string, sends them over the pipe, then exits.
///
/// The host raises <see cref="PathsReceived"/> on the UI thread each time a
/// message arrives so the main window can add the paths and start processing.
/// </summary>
public sealed class SingleInstanceCoordinator : IDisposable
{
  // ?? Pipe name (unique per user session) ???????????????????????????????
    private const string PipeName = "FileUnblocker_IPC_v1";

 // ?? State ?????????????????????????????????????????????????????????????
 private readonly CancellationTokenSource _cts = new();
    private Task? _serverTask;

    // ?? Events ????????????????????????????????????????????????????????????
    /// <summary>
    /// Raised on the thread-pool when a client sends a path list.
    /// Subscribe from the UI dispatcher so you can safely update the ViewModel.
    /// </summary>
    public event Action<IReadOnlyList<string>>? PathsReceived;

    // ?? Host API ??????????????????????????????????????????????????????????

 /// <summary>Start the named-pipe server (call from the first/host instance).</summary>
    public void StartServer()
    {
     _serverTask = Task.Run(ServerLoopAsync);
    }

    private async Task ServerLoopAsync()
    {
    while (!_cts.IsCancellationRequested)
        {
            try
            {
          await using var server = new NamedPipeServerStream(
           PipeName,
    PipeDirection.In,
   maxNumberOfServerInstances: NamedPipeServerStream.MaxAllowedServerInstances,
 PipeTransmissionMode.Byte,
          PipeOptions.Asynchronous);

         await server.WaitForConnectionAsync(_cts.Token);

 using var reader = new StreamReader(server, Encoding.UTF8);
           var payload = await reader.ReadToEndAsync(_cts.Token);

     var paths = payload
    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
           .Select(p => p.Trim())
   .Where(p => !string.IsNullOrEmpty(p))
                 .ToList();

     if (paths.Count > 0)
      PathsReceived?.Invoke(paths);
        }
            catch (OperationCanceledException) { break; }
            catch { /* swallow connection errors — restart loop */ }
  }
    }

    // ?? Client API ????????????????????????????????????????????????????????

    /// <summary>
    /// Try to send <paramref name="paths"/> to an already-running host instance.
    /// Returns <see langword="true"/> if the message was delivered, meaning
    /// the caller (second instance) should exit without showing a window.
    /// </summary>
  public static async Task<bool> TrySendToHostAsync(IEnumerable<string> paths,
        TimeSpan timeout = default)
    {
        if (timeout == default) timeout = TimeSpan.FromSeconds(2);
     try
        {
            using var client = new NamedPipeClientStream(
     ".", PipeName, PipeDirection.Out, PipeOptions.Asynchronous);

using var cts = new CancellationTokenSource(timeout);
            await client.ConnectAsync(cts.Token);

            await using var writer = new StreamWriter(client, Encoding.UTF8, leaveOpen: true);
         foreach (var p in paths)
            await writer.WriteLineAsync(p);

            await writer.FlushAsync();
   return true;
        }
     catch { return false; }
    }

  // ?? IDisposable ???????????????????????????????????????????????????????
    public void Dispose()
    {
      _cts.Cancel();
        _cts.Dispose();
  }
}
