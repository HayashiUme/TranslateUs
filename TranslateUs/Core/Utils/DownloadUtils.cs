namespace TranslateUs.Core.Utils;

/// <summary>
/// Shared HTTP helpers used across the project — download with progress,
/// retry on transient errors, and properly configured HttpClient instances.
/// </summary>
public static class DownloadUtils
{
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 TranslateUs/1.3";

    /// <summary>
    /// Creates an HttpClient with a sensible User-Agent header.
    /// Disposable — use in a using block for one-shot requests.
    /// </summary>
    public static HttpClient CreateClient(TimeSpan timeout)
    {
        var client = new HttpClient { Timeout = timeout };
        client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
        return client;
    }

    /// <summary>
    /// Downloads a file from <paramref name="url"/> to <paramref name="dest"/>,
    /// updating <see cref="DownloadProgress"/> along the way.
    /// </summary>
    public static async Task DownloadFileAsync(
        string url, string dest, CancellationToken ct = default)
    {
        using var client = CreateClient(TimeSpan.FromMinutes(10));
        using var response = await client.GetAsync(
            url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        long total = response.Content.Headers.ContentLength ?? -1;

        DownloadProgress.BytesDownloaded = 0;
        DownloadProgress.BytesTotal = total;
        DownloadProgress.Active = true;

        await using var src = await response.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(dest);

        byte[] buffer = new byte[8192];
        long read = 0;
        int n;
        while ((n = await src.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
        {
            await dst.WriteAsync(buffer, 0, n, ct);
            read += n;
            DownloadProgress.BytesDownloaded = read;
        }

        DownloadProgress.Active = false;

        var fi = new FileInfo(dest);
        Main.Logger.LogInfo($"[Http] Downloaded: {fi.Name} ({fi.Length / 1024} KB)");
    }

    /// <summary>
    /// Downloads with automatic retry on transient network errors.
    /// Cleans up partial files between attempts.
    /// </summary>
    /// <param name="label">Log prefix for progress messages (e.g. "NativeLib", "ModelManager").</param>
    public static async Task DownloadWithRetryAsync(
        string url, string dest, string label,
        CancellationToken ct = default, int maxRetries = 3)
    {
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                await DownloadFileAsync(url, dest, ct);
                return; // Success
            }
            catch (Exception ex) when (IsTransientError(ex) && attempt < maxRetries - 1)
            {
                // Clean up partial download
                try { if (File.Exists(dest)) File.Delete(dest); }
                catch { /* best-effort */ }

                int delayMs = (int)Math.Pow(2, attempt + 1) * 1000; // 2s, 4s, 8s
                Main.Logger.LogWarning(
                    $"[{label}] Download failed (attempt {attempt + 1}/{maxRetries}): {ex.Message}. " +
                    $"Retrying in {delayMs / 1000}s...");
                await Task.Delay(delayMs, ct);
            }
        }
    }

    /// <summary>
    /// Returns true for network/IO errors that are worth retrying.
    /// </summary>
    public static bool IsTransientError(Exception ex)
    {
        return ex is HttpRequestException
            || ex is IOException
            || ex is TaskCanceledException;
    }

    /// <summary>
    /// Extracts a filename from a URL, falling back to the last path segment.
    /// </summary>
    public static string GetFileNameFromUrl(string url)
    {
        try
        {
            return Path.GetFileName(new Uri(url).AbsolutePath);
        }
        catch
        {
            int lastSlash = url.LastIndexOf('/');
            return lastSlash >= 0 ? url[(lastSlash + 1)..] : "download.bin";
        }
    }
}
