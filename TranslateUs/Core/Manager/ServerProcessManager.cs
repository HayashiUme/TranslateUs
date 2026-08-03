using System.Diagnostics;

namespace TranslateUs.Core.Manager;


public class ServerProcessManager : IDisposable
{
    private Process? _process;
    private HttpClient? _client;
    private int _port;
    private bool _disposed;

    public int Port => _port;

    public HttpClient? Client => _client;

    public bool IsRunning =>
        _process is { HasExited: false } && _client != null;

    /// <summary>
    /// Kill any llama-server.exe processes left over from previous runs.
    /// Called before extraction to release file locks, and at startup for safety.
    /// </summary>
    public static void KillAll()
    {
        try
        {
            foreach (var proc in Process.GetProcessesByName("llama-server"))
            {
                Main.Logger.LogInfo(
                    $"[ServerProc] Killing leftover llama-server (PID {proc.Id})...");
                proc.Kill();
                proc.WaitForExit(3000);
            }
        }
        catch (Exception ex)
        {
            Main.Logger.LogWarning($"[ServerProc] Could not kill old servers: {ex.Message}");
        }
    }

    /// <summary>
    /// Start llama-server.exe with the given model.
    /// Blocks until the server responds to /health or times out.
    /// </summary>
    /// <param name="exePath">Absolute path to llama-server.exe.</param>
    /// <param name="modelPath">Absolute path to the .gguf model file.</param>
    /// <param name="threads">CPU thread count.</param>
    /// <param name="contextSize">KV-cache context size in tokens.</param>
    /// <param name="startPort">First port to try (will try up to 10 ports).</param>
    public async Task StartAsync(
        string exePath, string modelPath,
        int threads, int contextSize,
        int startPort = 8081)
    {
        if (_process != null)
            throw new InvalidOperationException("Server is already running.");

        KillAll();

        if (!File.Exists(exePath))
            throw new FileNotFoundException($"Server executable not found: {exePath}");

        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"Model not found: {modelPath}");

        _port = await FindAvailablePort(startPort);

        string args = $"-m \"{modelPath}\" " +
                      $"--host 127.0.0.1 " +
                      $"--port {_port} " +
                      $"-ngl 0 " +
                      $"-c {contextSize} " +
                      $"-t {threads} " +
                      $"-tb {threads}";

        Main.Logger.LogInfo($"[ServerProc] Starting on port {_port}...");
        Main.Logger.LogInfo($"[ServerProc] {exePath} {args}");

        var psi = new ProcessStartInfo(exePath, args)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? "."
        };

        _process = new Process { StartInfo = psi };

        _process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                Main.Logger.LogInfo($"[ServerProc] {e.Data}");
        };
        _process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                Main.Logger.LogWarning($"[ServerProc] {e.Data}");
        };

        _process.Start();
        JobObjectHelper.AssignChildProcess(_process);
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        Main.Logger.LogInfo("[ServerProc] Waiting for model load (~15-30s)...");
        bool ready = await WaitForReady(TimeSpan.FromSeconds(60));

        if (!ready)
        {
            if (_process.HasExited)
                throw new InvalidOperationException(
                    $"llama-server exited with code {_process.ExitCode}");

            throw new TimeoutException("llama-server did not become ready in time");
        }

        _client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
            BaseAddress = new Uri($"http://127.0.0.1:{_port}/")
        };

        Main.Logger.LogInfo($"[ServerProc] Ready on port {_port}");
    }

    private async Task<bool> WaitForReady(TimeSpan timeout)
    {
        using var pollClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var sw = Stopwatch.StartNew();

        while (sw.Elapsed < timeout)
        {
            if (_process is { HasExited: true })
                return false;

            try
            {
                var response = await pollClient.GetAsync(
                    $"http://127.0.0.1:{_port}/health");
                if (response.IsSuccessStatusCode)
                {
                    Main.Logger.LogInfo(
                        $"[ServerProc] Health OK after {sw.Elapsed.TotalSeconds:F0}s");
                    return true;
                }
            }
            catch
            {
                
            }

            await Task.Delay(500);
        }

        return false;
    }
    
    private static async Task<int> FindAvailablePort(int startPort)
    {
        for (int port = startPort; port < startPort + 10; port++)
        {
            try
            {
                var listener = new System.Net.Sockets.TcpListener(
                    System.Net.IPAddress.Loopback, port);
                listener.Start();
                listener.Stop();
                return port;
            }
            catch
            {
                
            }

            await Task.Delay(100);
        }

        return startPort;
    }

    #region IDisposable

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _client?.Dispose();
        _client = null;

        if (_process != null)
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.CloseMainWindow();
                    if (!_process.WaitForExit(3000))
                    {
                        _process.Kill();
                        _process.WaitForExit(2000);
                    }
                }
            }
            catch (Exception ex)
            {
                Main.Logger.LogWarning(
                    $"[ServerProc] Error shutting down: {ex.Message}");
            }

            _process.Dispose();
            _process = null;
        }
    }

    #endregion
}
