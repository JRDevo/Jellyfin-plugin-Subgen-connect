using System.Net.Mime;
using System.Reflection;
using Jellyfin.Data.Enums;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
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

        var paths = new List<(Guid Id, string Path)>();
        if (item is Video && !string.IsNullOrEmpty(item.Path))
        {
            paths.Add((item.Id, item.Path));
        }
        else
        {
            var children = _libraryManager.GetItemList(new InternalItemsQuery
            {
                AncestorIds = new[] { item.Id },
                IncludeItemTypes = new[] { BaseItemKind.Episode, BaseItemKind.Movie },
                Recursive = true,
                IsVirtualItem = false
            });
            paths.AddRange(children
                .Where(c => !string.IsNullOrEmpty(c.Path))
                .OrderBy(c => c.Path, StringComparer.Ordinal)
                .Select(c => (c.Id, c.Path)));
        }

        var baseUrl = Plugin.Instance!.Configuration.SubgenUrl.TrimEnd('/');
        var sent = 0;
        var skipped = 0;
        var watch = new List<(Guid ItemId, string Path)>();
        foreach (var (id, path) in paths)
        {
            if (SubtitleFiles.HasEnglishSubtitle(path))
            {
                skipped++;
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

        _logger.LogInformation("Subgen: sent {Sent} of {Total} files for {Name} ({Skipped} already had a subtitle)", sent, paths.Count, item.Name, skipped);
        return Ok(new { sent, skipped, total = paths.Count, name = item.Name });
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
