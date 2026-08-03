using TranslateUs.Core.Utils;
using UnityEngine;

namespace TranslateUs.Core;

/// <summary>
/// Manages the GGUF model file — checks local filesystem, downloads from URL
/// if missing, validates file size. Uses <see cref="HttpUtils"/> for networking.
/// </summary>
public static class ModelManager
{
    private const long MinModelSizeBytes = 100_000_000;

    public static async Task<string> AcquireModelAsync(
        string? configuredPath,
        string downloadUrl,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            Main.Logger.LogInfo($"[ModelManager] Using user-specified model: {configuredPath}");
            return configuredPath;
        }

        string modelsDir = Path.Combine(Application.persistentDataPath, "TranslateUs", "models");
        Directory.CreateDirectory(modelsDir);

        string fileName = DownloadUtils.GetFileNameFromUrl(downloadUrl);
        string localPath = Path.Combine(modelsDir, fileName);

        if (File.Exists(localPath))
        {
            var fi = new FileInfo(localPath);
            if (fi.Length >= MinModelSizeBytes)
            {
                Main.Logger.LogInfo(
                    $"[ModelManager] Model already downloaded: {localPath} " +
                    $"({fi.Length / (1024 * 1024)} MB)");
                return localPath;
            }

            Main.Logger.LogWarning(
                $"[ModelManager] Model file too small ({fi.Length} bytes), re-downloading...");
            try { File.Delete(localPath); } catch { }
        }

        string tmpPath = localPath + ".tmp";
        try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }

        DownloadProgress.Label = "AI Model";
        await DownloadModelWithRetryAsync(downloadUrl, localPath, tmpPath, ct);
        return localPath;
    }

    private static async Task DownloadModelWithRetryAsync(
        string url, string dest, string tmpPath, CancellationToken ct, int maxRetries = 3)
    {
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                await DownloadModelOnceAsync(url, tmpPath, ct);
                try { if (File.Exists(dest)) File.Delete(dest); } catch { }
                File.Move(tmpPath, dest);
                return;
            }
            catch (Exception ex) when (DownloadUtils.IsTransientError(ex) && attempt < maxRetries - 1)
            {
                try { if (File.Exists(tmpPath)) File.Delete(tmpPath); }
                catch { }

                int delayMs = (int)Math.Pow(2, attempt + 1) * 1000;
                Main.Logger.LogWarning(
                    $"[ModelManager] Download failed (attempt {attempt + 1}/{maxRetries}): " +
                    $"{ex.Message}. Retrying in {delayMs / 1000}s...");
                await Task.Delay(delayMs, ct);
            }
        }
    }

    private static async Task DownloadModelOnceAsync(
        string url, string tmpPath, CancellationToken ct)
    {
        Main.Logger.LogInfo($"[ModelManager] Downloading model from: {url}");
        Main.Logger.LogInfo("[ModelManager] This is a one-time download (~1 GB). Please wait...");

        using var client = DownloadUtils.CreateClient(TimeSpan.FromMinutes(30));
        using var response = await client.GetAsync(
            url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        long totalBytes = response.Content.Headers.ContentLength ?? -1;
        if (totalBytes > 0)
            Main.Logger.LogInfo($"[ModelManager] Total size: {totalBytes / (1024 * 1024)} MB");

        DownloadProgress.BytesTotal = totalBytes;
        DownloadProgress.BytesDownloaded = 0;
        DownloadProgress.Active = true;

        await using var src = await response.Content.ReadAsStreamAsync(ct);
        await using var dst = new FileStream(
            tmpPath, FileMode.Create, FileAccess.Write, FileShare.Read,
            bufferSize: 8192, useAsync: true);

        byte[] buffer = new byte[8192];
        long downloaded = 0;
        int n;
        DateTime lastLog = DateTime.MinValue;
        var logInterval = TimeSpan.FromSeconds(15);

        while ((n = await src.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
        {
            await dst.WriteAsync(buffer, 0, n, ct);
            downloaded += n;
            DownloadProgress.BytesDownloaded = downloaded;

            if (totalBytes > 0 && (DateTime.UtcNow - lastLog) >= logInterval)
            {
                double pct = (double)downloaded / totalBytes * 100;
                Main.Logger.LogInfo(
                    $"[ModelManager] Downloading... {pct:F0}% " +
                    $"({downloaded / (1024 * 1024)} / {totalBytes / (1024 * 1024)} MB)");
                lastLog = DateTime.UtcNow;
            }
        }

        DownloadProgress.Active = false;

        long actualSize = dst.Length;
        if (actualSize < MinModelSizeBytes)
        {
            throw new IOException(
                $"Downloaded model is too small: {actualSize} bytes (min {MinModelSizeBytes}).");
        }

        if (totalBytes > 0)
        {
            long diff = Math.Abs(actualSize - totalBytes);
            long tolerance = Math.Max(totalBytes / 100, 10_000_000); // 1% or 10 MB
            if (diff > tolerance)
            {
                throw new IOException(
                    $"Download size mismatch: got {actualSize / (1024 * 1024)} MB, " +
                    $"expected ~{totalBytes / (1024 * 1024)} MB " +
                    $"(diff {diff / (1024 * 1024)} MB > tolerance {tolerance / (1024 * 1024)} MB).");
            }

            if (diff > 0)
            {
                Main.Logger.LogInfo(
                    $"[ModelManager] Size differs by {diff} bytes " +
                    $"({(double)diff / totalBytes * 100:F1}%) — within tolerance, ok.");
            }
        }

        Main.Logger.LogInfo(
            $"[ModelManager] Download complete: {tmpPath} ({actualSize / (1024 * 1024)} MB) → renaming to final");
    }
}
