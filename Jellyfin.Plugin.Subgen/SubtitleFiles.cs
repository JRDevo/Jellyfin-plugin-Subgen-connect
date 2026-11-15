namespace Jellyfin.Plugin.Subgen;

internal static class SubtitleFiles
{
    /// <summary>True if an English .srt file sits next to the video (name.en.srt, name.eng.srt, name.en.*.srt).</summary>
    public static bool HasEnglishSubtitle(string videoPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(videoPath);
            if (dir is null || !Directory.Exists(dir))
            {
                return false;
            }

            var stem = Path.GetFileNameWithoutExtension(videoPath);
            return Directory.EnumerateFiles(dir, stem + ".*.srt")
                .Select(f => Path.GetFileName(f)[(stem.Length + 1)..].ToLowerInvariant())
                .Any(rest => rest.StartsWith("en.", StringComparison.Ordinal)
                    || rest.StartsWith("eng.", StringComparison.Ordinal)
                    || rest.StartsWith("english.", StringComparison.Ordinal));
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
