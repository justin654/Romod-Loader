using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace Romestead.ModValueEditor;

internal static class XnbPreviewExtractor
{
    private const string XnbCliReleaseUrl =
        "https://github.com/LeonBlade/xnbcli/releases/download/v1.0.7/xnbcli-windows-x64.zip";

    private static string LocalAppData =>
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    private static string AppDataDir =>
        Path.Combine(LocalAppData, "Romestead.ModValueEditor");

    private static string XnbCliDir =>
        Path.Combine(AppDataDir, "tools", "xnbcli");

    /// <summary>
    /// Locations an xnbcli install may already exist. We don't *depend* on any
    /// other Romestead tool, but if one already downloaded the 38 MB xnbcli we
    /// reuse it rather than fetching a second copy.
    /// </summary>
    private static IEnumerable<string> KnownXnbCliExes()
    {
        yield return Path.Combine(XnbCliDir, "xnbcli.exe");
        yield return Path.Combine(LocalAppData, "Romestead.MapWorkshop", "tools", "xnbcli", "xnbcli.exe");
    }

    private static string XnbCliExe =>
        KnownXnbCliExes().FirstOrDefault(File.Exists) ?? Path.Combine(XnbCliDir, "xnbcli.exe");

    private static string CacheDir =>
        Path.Combine(AppDataDir, "xnb-preview-cache");

    public static string ResolvePreviewImage(string path)
    {
        if (!path.EndsWith(".xnb", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        EnsureXnbCliInstalled();
        Directory.CreateDirectory(CacheDir);

        var cacheKey = CacheKey(path);
        var outDir = Path.Combine(CacheDir, cacheKey);
        var baseName = Path.GetFileNameWithoutExtension(path);
        var expectedPng = Path.Combine(outDir, baseName + ".png");
        if (File.Exists(expectedPng))
        {
            return expectedPng;
        }

        Directory.CreateDirectory(outDir);
        var rc = RunProcess(
            XnbCliExe,
            $"unpack {Quote(path)} {Quote(outDir)}",
            Path.GetDirectoryName(XnbCliExe));

        if (rc != 0)
        {
            throw new InvalidOperationException($"xnbcli unpack failed with exit code {rc}.");
        }

        var sidecar = Path.Combine(outDir, baseName + ".json");
        if (File.Exists(sidecar))
        {
            TryDelete(sidecar);
        }

        if (File.Exists(expectedPng))
        {
            return expectedPng;
        }

        var firstPng = Directory.EnumerateFiles(outDir, "*.png", SearchOption.AllDirectories).FirstOrDefault();
        if (firstPng is not null)
        {
            return firstPng;
        }

        throw new FileNotFoundException("xnbcli completed but did not emit a PNG preview.", expectedPng);
    }

    private static void EnsureXnbCliInstalled()
    {
        if (File.Exists(XnbCliExe))
        {
            return;
        }

        Directory.CreateDirectory(XnbCliDir);
        var zipPath = Path.Combine(Path.GetTempPath(), $"xnbcli-{Guid.NewGuid():N}.zip");
        var stagingDir = Path.Combine(Path.GetTempPath(), $"xnbcli-extract-{Guid.NewGuid():N}");
        try
        {
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
            using (var response = http.GetAsync(XnbCliReleaseUrl).GetAwaiter().GetResult())
            {
                response.EnsureSuccessStatusCode();
                using var zip = File.Create(zipPath);
                response.Content.CopyToAsync(zip).GetAwaiter().GetResult();
            }

            ZipFile.ExtractToDirectory(zipPath, stagingDir);
            var exe = Directory.EnumerateFiles(stagingDir, "xnbcli.exe", SearchOption.AllDirectories).FirstOrDefault()
                ?? throw new InvalidOperationException("xnbcli.exe not found inside the release zip.");

            var srcRoot = Path.GetDirectoryName(exe)!;
            foreach (var file in Directory.EnumerateFiles(srcRoot))
            {
                File.Copy(file, Path.Combine(XnbCliDir, Path.GetFileName(file)), overwrite: true);
            }
        }
        finally
        {
            TryDeleteDirectory(stagingDir);
            TryDelete(zipPath);
        }
    }

    private static int RunProcess(string fileName, string arguments, string? workingDirectory)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        process.WaitForExit();
        return process.ExitCode;
    }

    private static string CacheKey(string path)
    {
        var info = new FileInfo(path);
        var raw = $"{info.FullName.ToLowerInvariant()}|{info.LastWriteTimeUtc.Ticks}|{info.Length}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes)[..24].ToLowerInvariant();
    }

    private static string Quote(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        if (value.IndexOfAny([' ', '\t', '"', '(', ')']) < 0)
        {
            return value;
        }

        return "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
    }
}
