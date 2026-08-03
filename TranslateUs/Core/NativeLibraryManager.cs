using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using TranslateUs.Core.Manager;
using TranslateUs.Core.Utils;
using TranslateUs.Native;
using UnityEngine;

namespace TranslateUs.Core;

public static class NativeLibraryManager
{
    private static string BaseUrl
    {
        get
        {
            string configured = Main.NativeLibUrl?.Value ?? "";
            return string.IsNullOrWhiteSpace(configured)
                ? "https://github.com/ggerganov/llama.cpp/releases/download"
                : configured.TrimEnd('/');
        }
    }

    private const string ReleaseTag = "b10142";

    private static readonly string[] WindowsDlls =
        { "llama.dll", "ggml.dll", "ggml-base.dll", "ggml-cpu.dll" };

    private static readonly string[] WindowsExe =
        { "llama-server.exe" };

    private static readonly string[] WindowsAllFiles =
        WindowsExe.Concat(WindowsDlls).ToArray();

    private static string? _serverExePath;
    private static bool _ensured;

    public static string? ServerExePath => _serverExePath;

    public static async Task EnsureAsync(CancellationToken ct = default)
    {
        if (_ensured) return;

        if (OperatingSystem.IsAndroid())
            await EnsureAndroidAsync(ct);
        else
            await EnsureWindowsAsync(ct);

        _ensured = true;
    }

    #region Windows

    private const long MinValidZipSize = 15_000_000;

    private static async Task EnsureWindowsAsync(CancellationToken ct)
    {
        string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
        string libDir = Path.Combine(pluginDir, "llama-libs");

        string archiveName = $"llama-{ReleaseTag}-bin-win-cpu-x64.zip";
        string archivePath = Path.Combine(libDir, archiveName);

        if (IoUtils.AllFilesExist(libDir, WindowsAllFiles))
        {
            _serverExePath = Path.Combine(libDir, "llama-server.exe");
            return;
        }

        // Kill leftover servers
        ServerProcessManager.KillAll();
        Directory.CreateDirectory(libDir);

        bool needDownload = true;

        // Try existing zip first
        if (File.Exists(archivePath))
        {
            long size = new FileInfo(archivePath).Length;
            if (size >= MinValidZipSize)
            {
                try
                {
                    Main.Logger.LogInfo($"[NativeLib] Archive exists ({size / 1024} KB), extracting...");
                    ZipFile.ExtractToDirectory(archivePath, libDir, overwriteFiles: true);
                    FlattenExtractedFiles(libDir);
                    if (IoUtils.AllFilesExist(libDir, WindowsAllFiles))
                        needDownload = false;
                }
                catch (Exception ex)
                {
                    Main.Logger.LogWarning($"[NativeLib] Extraction failed, will re-download: {ex.Message}");
                }
            }
            else
            {
                Main.Logger.LogWarning(
                    $"[NativeLib] Archive too small ({size / 1024} KB), re-downloading.");
            }

            if (needDownload) { try { File.Delete(archivePath); } catch { } }
        }

        if (needDownload)
        {
            string url = $"{BaseUrl}/{ReleaseTag}/{archiveName}";
            Main.Logger.LogInfo($"[NativeLib] Downloading {archiveName} (~18 MB)...");
            DownloadProgress.Label = "AI Runtime";
            await DownloadUtils.DownloadWithRetryAsync(url, archivePath, "NativeLib", ct);

            Main.Logger.LogInfo("[NativeLib] Extracting...");
            ZipFile.ExtractToDirectory(archivePath, libDir, overwriteFiles: true);
            FlattenExtractedFiles(libDir);
        }

        _serverExePath = Path.Combine(libDir, "llama-server.exe");
        if (!File.Exists(_serverExePath))
            throw new FileNotFoundException(
                $"llama-server.exe not found after extraction. Expected: {_serverExePath}");

        Main.Logger.LogInfo($"[NativeLib] llama-server ready: {_serverExePath}");
    }

    private static void FlattenExtractedFiles(string libDir)
    {
        if (File.Exists(Path.Combine(libDir, "llama-server.exe")))
            return;

        foreach (string subDir in Directory.GetDirectories(libDir))
        {
            string candidate = Path.Combine(subDir, "llama-server.exe");
            if (!File.Exists(candidate)) continue;

            Main.Logger.LogInfo($"[NativeLib] Flattening: {Path.GetFileName(subDir)}");
            foreach (string file in Directory.GetFiles(subDir))
            {
                string dest = Path.Combine(libDir, Path.GetFileName(file));
                if (File.Exists(dest)) File.Delete(dest);
                File.Move(file, dest);
            }
            try { Directory.Delete(subDir); }
            catch { }
            break;
        }
    }

    #endregion

    // FusionCore taihuaile
    #region Android

    // CPU variant preference: try newest ARM ISA first, fall back to older
    private static readonly string[] CpuVariantPreference =
        { "armv9.2_2", "armv9.2_1", "armv9.0_1", "armv8.6_1", "armv8.2_2", "armv8.2_1", "armv8.0_1" };

    private static async Task EnsureAndroidAsync(CancellationToken ct)
    {
        string pluginDir = Path.GetDirectoryName(
            System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".";
        string libDir = pluginDir;
        Directory.CreateDirectory(libDir);
        Main.Logger.LogInfo($"[NativeLib] libDir={libDir}");

        string archiveName = $"llama-{ReleaseTag}-bin-android-arm64.tar.gz";
        string archivePath = Path.Combine(libDir, archiveName);

        if (!AllAndroidSosPresent(libDir))
        {
            string url = $"{BaseUrl}/{ReleaseTag}/{archiveName}";
            Main.Logger.LogInfo($"[NativeLib] Downloading {archiveName}...");
            await DownloadUtils.DownloadFileAsync(url, archivePath, ct);

            Main.Logger.LogInfo("[NativeLib] Extracting...");
            IoUtils.ExtractTarGz(archivePath, libDir);

            ListExtractedFiles(libDir);
            
            ResolveCpuVariant(libDir);
            
            FlattenAndroidSos(libDir);
        }
        
        string nativeDir = GetAndroidNativeLibDir();
        CopyAndroidSosToNativeDir(libDir, nativeDir);

        string[] loadOrder = { "libggml-base.so", "libggml.so", "libllama.so" };
        foreach (string name in loadOrder)
        {
            string path = Path.Combine(nativeDir, name);
            if (!File.Exists(path))
                throw new FileNotFoundException($"{name} not found in {nativeDir}");

            Main.Logger.LogInfo($"[NativeLib] Loading {name} from {path}...");
            NativeLibrary.Load(path);
        }
        
        LlamaCppApi.SetLogCallback();

        // LoadCpuBackend picks the fastest ISA variant the device actually supports (a variant
        if (!LoadCpuBackend(nativeDir))
            throw new InvalidOperationException(
                "[NativeLib] No compatible GGML CPU backend could be loaded for this device");
    }

    private static bool LoadCpuBackend(string nativeDir)
    {
        foreach (string pref in CpuVariantPreference)
        {
            string so = Path.Combine(nativeDir, $"libggml-cpu-android_{pref}.so");
            if (!File.Exists(so)) continue;

            Main.Logger.LogInfo($"[NativeLib] Trying CPU backend {pref}...");
            if (LlamaCppApi.ggml_backend_load(so))
            {
                Main.Logger.LogInfo($"[NativeLib] CPU backend {pref} loaded.");
                return true;
            }
            Main.Logger.LogWarning($"[NativeLib] CPU backend {pref} rejected (unsupported ISA).");
        }

        //a generic libggml-cpu.so (older installs resolved a single variant into it).
        string generic = Path.Combine(nativeDir, "libggml-cpu.so");
        if (File.Exists(generic) && LlamaCppApi.ggml_backend_load(generic))
        {
            Main.Logger.LogInfo("[NativeLib] Generic CPU backend loaded.");
            return true;
        }

        return false;
    }
    
    private static string GetAndroidNativeLibDir()
    {
        string? appData = Environment.GetEnvironmentVariable("FUSION_APP_DATA_DIR");
        if (string.IsNullOrWhiteSpace(appData))
        {
            Main.Logger.LogWarning(
                "[NativeLib] FUSION_APP_DATA_DIR not set — falling back to persistentDataPath (noexec, may fail)");
            appData = Application.persistentDataPath;
        }

        string dir = Path.Combine(appData, "llama-libs");
        Directory.CreateDirectory(dir);
        Main.Logger.LogInfo($"[NativeLib] Native lib dir: {dir}");
        return dir;
    }

    private static void CopyAndroidSosToNativeDir(string libDir, string nativeDir)
    {
        foreach (string so in Directory.GetFiles(libDir, "*.so", SearchOption.TopDirectoryOnly))
        {
            string name = Path.GetFileName(so);
            string dest = Path.Combine(nativeDir, name);
            try
            {
                if (File.Exists(dest) && new FileInfo(dest).Length == new FileInfo(so).Length)
                    continue;
                File.Copy(so, dest, overwrite: true);
            }
            catch (Exception ex)
            {
                Main.Logger.LogWarning($"[NativeLib] Failed to copy {name} to native dir: {ex.Message}");
            }
        }
    }

    private static void ResolveCpuVariant(string libDir)
    {
        var allSos = new List<string>();
        try
        {
            foreach (string f in Directory.GetFiles(libDir, "libggml-cpu-android_*.so", SearchOption.AllDirectories))
                allSos.Add(f);
        }
        catch { return; }

        foreach (string pref in CpuVariantPreference)
        {
            string? variant = allSos.FirstOrDefault(p =>
                Path.GetFileName(p).Contains(pref, StringComparison.OrdinalIgnoreCase));
            if (variant != null)
            {
                string dest = Path.Combine(Path.GetDirectoryName(variant)!, "libggml-cpu.so");
                Main.Logger.LogInfo($"[NativeLib] CPU variant: {Path.GetFileName(variant)} → libggml-cpu.so");
                File.Copy(variant, dest, overwrite: true);
                return;
            }
        }
        Main.Logger.LogWarning("[NativeLib] No CPU variant found for libggml-cpu!");
    }
    
    private static void FlattenAndroidSos(string libDir)
    {
        string[] needed = { "libllama.so", "libggml.so", "libggml-base.so", "libggml-cpu.so" };
        foreach (string f in Directory.GetFiles(libDir, "*.so", SearchOption.AllDirectories))
        {
            string name = Path.GetFileName(f);
            if (needed.Contains(name, StringComparer.OrdinalIgnoreCase) || name.StartsWith("libggml-cpu", StringComparison.OrdinalIgnoreCase))
            {
                string dest = Path.Combine(libDir, name);
                if (!string.Equals(f, dest, StringComparison.OrdinalIgnoreCase))
                {
                    Main.Logger.LogInfo($"[NativeLib] Flatten: {name} → plugins root");
                    File.Copy(f, dest, overwrite: true);
                }
            }
        }
    }

    private static bool AllAndroidSosPresent(string libDir)
    {
        string[] needed = { "libllama.so", "libggml.so", "libggml-base.so", "libggml-cpu.so" };
        return needed.All(n => File.Exists(Path.Combine(libDir, n)));
    }

    private static void ListExtractedFiles(string libDir)
    {
        try
        {
            if (!Directory.Exists(libDir)) return;
            var files = Directory.GetFiles(libDir, "*", SearchOption.AllDirectories)
                .Select(f => f.Replace(libDir, "").TrimStart(Path.DirectorySeparatorChar, '/'))
                .OrderBy(f => f)
                .ToList();
            Main.Logger.LogInfo($"[NativeLib] Extracted {files.Count} files:");
            foreach (string f in files)
                Main.Logger.LogInfo($"[NativeLib]   {f}");
        }
        catch (Exception ex)
        {
            Main.Logger.LogWarning($"[NativeLib] Could not list files: {ex.Message}");
        }
    }

    #endregion
}
