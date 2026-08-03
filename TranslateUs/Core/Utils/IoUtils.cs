using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace TranslateUs.Core.Utils;

/// <summary>
/// File-system I/O helpers used during native asset and model setup.
/// </summary>
public static class IoUtils
{
    /// <summary>
    /// Returns true when every file in <paramref name="files"/> exists in <paramref name="dir"/>.
    /// </summary>
    public static bool AllFilesExist(string dir, string[] files)
    {
        return files.All(f => File.Exists(Path.Combine(dir, f)));
    }

    /// <summary>
    /// Extracts a .tar.gz archive to <paramref name="outputDir"/>.
    /// Tries System.Formats.Tar first (.NET 7+), then falls back to a pure managed implementation.
    /// </summary>
    public static void ExtractTarGz(string archivePath, string outputDir)
    {
        // Try System.Formats.Tar if available (.NET 7+)
        try
        {
            var tarMethod = Type.GetType("System.Formats.Tar.TarFile, System.Formats.Tar");
            if (tarMethod != null)
            {
                var extractMethod = tarMethod.GetMethod("ExtractToDirectory",
                    new[] { typeof(string), typeof(string), typeof(bool) });
                if (extractMethod != null)
                {
                    extractMethod.Invoke(null, new object[] { archivePath, outputDir, true });
                    return;
                }
            }

            // Try via loaded assemblies
            var asm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "System.Formats.Tar");
            if (asm != null)
            {
                var tarFile = asm.GetType("System.Formats.Tar.TarFile")!;
                var method = tarFile.GetMethod("ExtractToDirectory",
                    new[] { typeof(string), typeof(string), typeof(bool) })!;
                method.Invoke(null, new object[] { archivePath, outputDir, true });
                return;
            }
        }
        catch (Exception ex)
        {
            Main.Logger.LogWarning($"[Io] System.Formats.Tar failed: {ex.Message}, using managed fallback");
        }

        // Managed fallback — works on all platforms including Android
        ExtractTarGzManaged(archivePath, outputDir);
    }

    /// <summary>
    /// Pure managed .tar.gz extraction using GZipStream + manual tar parsing.
    /// Works on all platforms including Android (no shell, no native deps).
    /// </summary>
    private static void ExtractTarGzManaged(string archivePath, string outputDir)
    {
        Directory.CreateDirectory(outputDir);

        using var fs = File.OpenRead(archivePath);
        using var gz = new GZipStream(fs, CompressionMode.Decompress);

        byte[] header = new byte[512];
        long totalFiles = 0;

        while (true)
        {
            // Read 512-byte tar header
            int headerRead = ReadExactly(gz, header, 0, 512);
            if (headerRead < 512)
                break;

            // Check for end-of-archive (two zero-filled blocks)
            bool allZero = true;
            for (int i = 0; i < 512; i++)
            {
                if (header[i] != 0) { allZero = false; break; }
            }
            if (allZero) break;

            // Parse file name (100 bytes at offset 0, null-terminated)
            string name = ReadNullTerminatedString(header, 0, 100);

            // Parse file size (12 octal bytes at offset 124)
            long fileSize = ParseOctal(header, 124, 12);

            // Parse type flag (offset 156): '0' = regular file, '5' = directory
            char typeFlag = (char)header[156];

            // Skip the remaining header + any extended attributes; read to data start
            // (some archives use ustar prefix at offset 345)

            if (typeFlag == '5' || name.EndsWith("/"))
            {
                // Directory — skip
            }
            else if (fileSize > 0 && !string.IsNullOrEmpty(name))
            {
                // Remove leading ./ or / if present
                name = name.TrimStart('/', '.').TrimStart('/');

                string destPath = Path.Combine(outputDir, name);
                string? destDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(destDir))
                    Directory.CreateDirectory(destDir);

                byte[] fileData = new byte[fileSize];
                int dataRead = ReadExactly(gz, fileData, 0, (int)fileSize);
                if (dataRead == fileSize)
                {
                    File.WriteAllBytes(destPath, fileData);
                    totalFiles++;
                }
                else
                {
                    Main.Logger.LogWarning(
                        $"[Io] Tar: short read for '{name}': got {dataRead}, expected {fileSize}");
                }

                // Skip padding to next 512-byte boundary
                long padding = (512 - (fileSize % 512)) % 512;
                if (padding > 0)
                {
                    byte[] padBuf = new byte[padding];
                    ReadExactly(gz, padBuf, 0, (int)padding);
                }
            }
            else
            {
                // Skip file data + padding for zero-size or special files
                if (fileSize > 0)
                {
                    long padding = (512 - (fileSize % 512)) % 512;
                    long totalSkip = fileSize + padding;

                    byte[] skipBuf = new byte[8192];
                    while (totalSkip > 0)
                    {
                        int chunk = (int)Math.Min(totalSkip, skipBuf.Length);
                        int r = gz.Read(skipBuf, 0, chunk);
                        if (r <= 0) break;
                        totalSkip -= r;
                    }
                }
            }
        }

        Main.Logger.LogInfo($"[Io] Tar extraction complete: {totalFiles} files to {outputDir}");
    }

    private static string ReadNullTerminatedString(byte[] buf, int offset, int maxLen)
    {
        int end = offset;
        while (end < offset + maxLen && end < buf.Length && buf[end] != 0)
            end++;
        return Encoding.UTF8.GetString(buf, offset, end - offset);
    }

    private static long ParseOctal(byte[] buf, int offset, int len)
    {
        // Tar uses NUL-terminated or space-padded octal strings
        long value = 0;
        for (int i = offset; i < offset + len && i < buf.Length; i++)
        {
            byte b = buf[i];
            if (b == 0 || b == (byte)' ') break;
            if (b >= (byte)'0' && b <= (byte)'7')
                value = (value << 3) | (uint)(b - (byte)'0');
        }
        return value;
    }

    private static int ReadExactly(Stream stream, byte[] buffer, int offset, int count)
    {
        int total = 0;
        while (total < count)
        {
            int n = stream.Read(buffer, offset + total, count - total);
            if (n <= 0) break;
            total += n;
        }
        return total;
    }
}
