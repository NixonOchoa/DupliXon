namespace DupliXon.Media;

/// <summary>Finds an ffmpeg.exe (version 6.1 or newer, needed for the ddagrab desktop-duplication source).</summary>
public static class FfmpegLocator
{
    public static string? Find()
    {
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "ffmpeg", "ffmpeg.exe"),
            Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe"),
        };

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            if (!string.IsNullOrWhiteSpace(dir)) candidates.Add(Path.Combine(dir.Trim(), "ffmpeg.exe"));

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        candidates.Add(Path.Combine(local, "Microsoft", "WinGet", "Links", "ffmpeg.exe"));

        var found = candidates.FirstOrDefault(File.Exists);
        if (found is not null) return found;

        var packages = Path.Combine(local, "Microsoft", "WinGet", "Packages");
        if (!Directory.Exists(packages)) return null;
        return Directory.EnumerateDirectories(packages, "*FFmpeg*")
            .SelectMany(d => Directory.EnumerateFiles(d, "ffmpeg.exe", SearchOption.AllDirectories))
            .FirstOrDefault();
    }
}
