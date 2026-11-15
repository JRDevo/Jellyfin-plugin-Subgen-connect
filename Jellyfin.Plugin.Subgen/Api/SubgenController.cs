using System.Net.Mime;
using System.Reflection;
using Jellyfin.Data.Enums;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Subgen.Api;

/// <summary>
/// API for the "Generate subtitles" button.
/// </summary>
[ApiController]
[Route("Subgen")]
public class SubgenController : ControllerBase
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly ILibraryManager _libraryManager;
    private readonly SubtitleWatcher _watcher;
    private readonly ILogger<SubgenController> _logger;

    public SubgenController(ILibraryManager libraryManager, SubtitleWatcher watcher, ILogger<SubgenController> logger)
    {
        _libraryManager = libraryManager;
        _watcher = watcher;
        _logger = logger;
    }

    /// <summary>
    /// Sends every video under the item (episode, season, series or movie) to Subgen.
    /// Subgen skips files that already have its subtitle.
    /// </summary>
    [HttpPost("Generate/{itemId}")]
    [Authorize(Policy = Policies.SubtitleManagement)] // admins and users with "Allow this user to edit subtitles"
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<object>> Generate([FromRoute] Guid itemId, CancellationToken cancellationToken)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item is null)
        {
            return NotFound();
        }

        var videos = new List<BaseItem>();
        if (item is Video && !string.IsNullOrEmpty(item.Path))
        {
            videos.Add(item);
        }
        else
        {
            // A season is not always the episodes' parent folder in Jellyfin's database
            // (episodes can hang under the series and only point to the season by SeasonId).
            // So for a season: take all episodes of its series and keep the ones of this season.
            var season = item as Season;
            var children = _libraryManager.GetItemList(new InternalItemsQuery
            {
                AncestorIds = new[] { season is null ? item.Id : season.SeriesId },
                IncludeItemTypes = new[] { BaseItemKind.Episode, BaseItemKind.Movie },
                Recursive = true,
                IsVirtualItem = false
            }).Where(c => season is null || (c is Episode e && e.SeasonId == season.Id));
            videos.AddRange(children
                .Where(c => !string.IsNullOrEmpty(c.Path))
                .OrderBy(c => c.Path, StringComparer.Ordinal));
        }

        var baseUrl = Plugin.Instance!.Configuration.SubgenUrl.TrimEnd('/');
        var sent = 0;
        var skipped = 0;
        var embedded = 0;
        var watch = new List<(Guid ItemId, string Path)>();
        foreach (var video in videos)
        {
            var id = video.Id;
            var path = video.Path;
            if (SubtitleFiles.HasEnglishSubtitle(path))
            {
                skipped++;
                continue;
            }

            // Embedded (inside the file) full English subtitles are usually better than generated ones.
            if (HasEmbeddedEnglishSubtitle(video))
            {
                embedded++;
                continue;
            }

            var url = baseUrl + "/batch?directory=" + Uri.EscapeDataString(path);
            try
            {
                using var resp = await Http.PostAsync(url, null, cancellationToken).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    sent++;
                    watch.Add((id, path));
                }
                else
                {
                    _logger.LogWarning("Subgen: {Status} for {Path}", (int)resp.StatusCode, path);
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Subgen: cannot reach {Url}", baseUrl);
                return StatusCode(StatusCodes.Status502BadGateway, new { error = "Cannot reach Subgen at " + baseUrl });
            }
        }

        // Subgen does not tell Jellyfin about /batch jobs; the watcher refreshes each item when its .srt appears.
        if (watch.Count > 0)
        {
            _watcher.Watch(watch);
        }

        _logger.LogInformation(
            "Subgen: sent {Sent} of {Total} files for {Name} ({Skipped} had a subtitle file, {Embedded} had embedded English subtitles)",
            sent,
            videos.Count,
            item.Name,
            skipped,
            embedded);
        return Ok(new { sent, skipped, embedded, total = videos.Count, name = item.Name });
    }

    /// <summary>
    /// True if the video file itself holds an English subtitle track that is not "forced".
    /// Forced tracks only cover foreign-language parts, so they do not count.
    /// Tracks with no language tag do not count either (they may be another language).
    /// </summary>
    private bool HasEmbeddedEnglishSubtitle(BaseItem video)
    {
        try
        {
            return video.GetMediaStreams().Any(s =>
                s.Type == MediaStreamType.Subtitle
                && !s.IsExternal
                && !s.IsForced
                && s.Language is not null
                && (s.Language.Equals("eng", StringComparison.OrdinalIgnoreCase)
                    || s.Language.Equals("en", StringComparison.OrdinalIgnoreCase)
                    || s.Language.Equals("english", StringComparison.OrdinalIgnoreCase)));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            _logger.LogDebug(ex, "Subgen: could not read streams for {Path}", video.Path);
            return false;
        }
    }

    /// <summary>The script that adds the button to the web page.</summary>
    [HttpGet("client.js")]
    [AllowAnonymous]
    [Produces("application/javascript")]
    public ActionResult GetClientScript()
    {
        var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Jellyfin.Plugin.Subgen.Web.client.js");
        if (stream is null)
        {
            return NotFound();
        }

        return File(stream, "application/javascript");
    }
}
