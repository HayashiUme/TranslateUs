namespace TranslateUs.Core;

public static class DownloadProgress
{
    public static string Label = "";
    public static long BytesDownloaded;
    public static long BytesTotal;
    public static bool Active;

    public static string GetDisplayText()
    {
        if (!Active || string.IsNullOrEmpty(Label)) return Label;
        if (BytesTotal > 0)
        {
            double pct = (double)BytesDownloaded / BytesTotal * 100.0;
            double downloadedMB = BytesDownloaded / (1024.0 * 1024.0);
            double totalMB = BytesTotal / (1024.0 * 1024.0);
            return $"{Label}\n{downloadedMB:F0} / {totalMB:F0} MB ({pct:F0}%)";
        }
        double mb = BytesDownloaded / (1024.0 * 1024.0);
        return $"{Label}\n{mb:F1} MB downloaded...";
    }

    public static void Reset()
    {
        Label = "";
        BytesDownloaded = 0;
        BytesTotal = 0;
        Active = false;
    }
}
