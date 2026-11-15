using System.Diagnostics;
using System.Text;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Subgen;

/// <summary>
/// Adds one result, "Generate with Subgen", to Jellyfin's subtitle search.
/// Picking it extracts the audio with Jellyfin's FFmpeg, sends it to Subgen's
/// /asr endpoint, and returns the SRT that Subgen makes.
/// </summary>
public class SubgenSubtitleProvider : ISubtitleProvider
{
    private readonly ILogger<SubgenSubtitleProvider> _logger;
    private readonly IMediaEncoder _mediaEncoder;

    public SubgenSubtitleProvider(ILogger<SubgenSubtitleProvider> logger, IMediaEncoder mediaEncoder)
    {
        _logger = logger;
        _mediaEncoder = mediaEncoder;
    }

    public string Name => "Subgen Connect";

    public IEnumerable<VideoContentType> SupportedMediaTypes => new[] { VideoContentType.Episode, VideoContentType.Movie };

    public Task<IEnumerable<RemoteSubtitleInfo>> Search(SubtitleSearchRequest request, CancellationToken cancellationToken)
    {
        var results = new List<RemoteSubtitleInfo>();
        if (string.IsNullOrEmpty(request.MediaPath) || !File.Exists(request.MediaPath))
        {
            return Task.FromResult<IEnumerable<RemoteSubtitleInfo>>(results);
        }

        var lang2 = string.IsNullOrEmpty(request.TwoLetterISOLanguageName) ? "en" : request.TwoLetterISOLanguageName;
        var id = Encode(lang2 + "|" + request.MediaPath);

        results.Add(new RemoteSubtitleInfo
        {
            Id = id,
            ProviderName = Name,
            Name = "Generate with Subgen (Whisper)",
            Format = "srt",
            ThreeLetterISOLanguageName = request.Language,
            Author = "Subgen",
            Comment = "Makes a new subtitle from the audio of this file.",
            IsHashMatch = true
        });

        return Task.FromResult<IEnumerable<RemoteSubtitleInfo>>(results);
    }

    public async Task<SubtitleResponse> GetSubtitles(string id, CancellationToken cancellationToken)
    {
        var parts = Decode(id).Split('|', 2);
        var lang2 = parts[0];
        var mediaPath = parts[1];
        var config = Plugin.Instance!.Configuration;

        var wav = Path.Combine(Path.GetTempPath(), "subgen-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            _logger.LogInformation("Subgen: extracting audio from {Path}", mediaPath);
            await ExtractAudio(mediaPath, wav, cancellationToken).ConfigureAwait(false);

            var url = config.SubgenUrl.TrimEnd('/')
                + "/asr?task=transcribe&output=srt&encode=true"
                + "&language=" + Uri.EscapeDataString(lang2)
                + "&video_file=" + Uri.EscapeDataString(mediaPath);

            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(Math.Max(1, config.TimeoutMinutes)) };
            using var form = new MultipartFormDataContent();
            await using var fs = File.OpenRead(wav);
            var file = new StreamContent(fs);
            file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
            form.Add(file, "audio_file", "audio.wav");

            _logger.LogInformation("Subgen: sending audio to {Url}", config.SubgenUrl);
            using var resp = await http.PostAsync(url, form, cancellationToken).ConfigureAwait(false);
            var srt = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode || !srt.Contains("-->", StringComparison.Ordinal))
            {
                var preview = srt.Length > 300 ? srt[..300] : srt;
                throw new InvalidOperationException($"Subgen returned no subtitle (HTTP {(int)resp.StatusCode}): {preview}");
            }

            _logger.LogInformation("Subgen: subtitle ready for {Path}", mediaPath);
            return new SubtitleResponse
            {
                Format = "srt",
                Language = lang2,
                IsForced = false,
                Stream = new MemoryStream(Encoding.UTF8.GetBytes(srt))
            };
        }
        finally
        {
            try { File.Delete(wav); } catch (IOException) { }
        }
    }

    private async Task ExtractAudio(string input, string output, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _mediaEncoder.EncoderPath,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in new[] { "-nostdin", "-loglevel", "error", "-y", "-i", input, "-vn", "-map", "0:a:0", "-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le", output })
        {
            psi.ArgumentList.Add(a);
        }

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Could not start FFmpeg");
        var errTask = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct).ConfigureAwait(false);
        var err = await errTask.ConfigureAwait(false);
        if (p.ExitCode != 0 || !File.Exists(output))
        {
            throw new InvalidOperationException("FFmpeg audio extraction failed: " + err);
        }
    }

    private static string Encode(string s) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Decode(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + ((4 - (s.Length % 4)) % 4), '=');
        return Encoding.UTF8.GetString(Convert.FromBase64String(s));
    }
}
