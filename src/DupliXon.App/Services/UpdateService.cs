using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using DupliXon.Core;

namespace DupliXon.App.Services;

public sealed record UpdateInfo(Version Version, string Tag, string DownloadUrl, long Size, string? Sha256, string Notes);

/// <summary>
/// Self-update from GitHub Releases: checks the latest release, downloads its installer, verifies it
/// and runs it silently. The installer closes DupliXon, replaces it and starts the new version.
/// </summary>
public sealed class UpdateService
{
    private const string LatestReleaseApi = "https://api.github.com/repos/NixonOchoa/DupliXon/releases/latest";
    private const string AssetName = "DupliXon-Setup.exe";

    private static readonly HttpClient Http = CreateClient();

    public static Version CurrentVersion
    {
        get
        {
            // Development only: pretend to be an older build to exercise the update flow.
            if (Version.TryParse(Environment.GetEnvironmentVariable("DUPLIXON_DEV_FAKE_VERSION"), out var fake)) return fake;
            var v = typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    /// <summary>Returns the newer release, or null when up to date or offline.</summary>
    public async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await Http.GetAsync(LatestReleaseApi, ct);
            if (!response.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;

            var tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest) || latest <= CurrentVersion) return null;

            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                if (asset.GetProperty("name").GetString() != AssetName) continue;
                var digest = asset.TryGetProperty("digest", out var d) ? d.GetString() : null;
                return new UpdateInfo(
                    latest, tag,
                    asset.GetProperty("browser_download_url").GetString()!,
                    asset.GetProperty("size").GetInt64(),
                    digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true ? digest[7..] : null,
                    root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
        {
            Log.Info($"update check skipped: {ex.Message}");
        }
        return null;
    }

    /// <summary>Downloads and verifies the installer; reports progress 0..1.</summary>
    public async Task<string> DownloadAsync(UpdateInfo update, IProgress<double> progress, CancellationToken ct = default)
    {
        var dir = Path.Combine(Path.GetTempPath(), "DupliXon-Update");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"DupliXon-Setup-{update.Version}.exe");

        using (var response = await Http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using var file = File.Create(path);
            var buffer = new byte[256 * 1024];
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), ct);
                total += read;
                if (update.Size > 0) progress.Report((double)total / update.Size);
            }
        }

        var info = new FileInfo(path);
        if (update.Size > 0 && info.Length != update.Size)
            throw new InvalidDataException("La descarga está incompleta.");
        if (update.Sha256 is not null)
        {
            await using var stream = File.OpenRead(path);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
            if (!hash.Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("El archivo descargado no coincide con la versión publicada.");
        }
        return path;
    }

    /// <summary>Starts the installer in silent mode; it closes this app and relaunches the new version.</summary>
    public static void RunInstaller(string path)
    {
        Log.Info($"running update installer {path}");
        Process.Start(new ProcessStartInfo(path, "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS")
        {
            UseShellExecute = true, // lets Windows show the administrator prompt
        });
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DupliXon", CurrentVersion.ToString()));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }
}
