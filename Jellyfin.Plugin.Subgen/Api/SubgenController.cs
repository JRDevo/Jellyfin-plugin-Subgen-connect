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
    private readonly ILogger<SubgenController> _logger;

    public SubgenController(ILibraryManager libraryManager, ILogger<SubgenController> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <summary>
    /// Sends every video under the item (episode, season, series or movie) to Subgen.
    /// Subgen skips files that already have its subtitle.
    /// </summary>
    [HttpPost("Generate/{itemId}")]
    [Authorize(Policy = Policies.SubtitleManagement)] // admins and users with "Allow subtitle management"
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<object>> Generate([FromRoute] Guid itemId, CancellationToken cancellationToken)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item is null)
        {
            return NotFound();
        }

        var paths = new List<string>();
        if (item is Video && !string.IsNullOrEmpty(item.Path))
        {
            paths.Add(item.Path);
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
                .Select(c => c.Path));
        }

        var baseUrl = Plugin.Instance!.Configuration.SubgenUrl.TrimEnd('/');
        var sent = 0;
        var skipped = 0;
        foreach (var path in paths)
        {
            if (HasEnglishSubtitle(path))
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

        _logger.LogInformation("Subgen: sent {Sent} of {Total} files for {Name} ({Skipped} already had a subtitle)", sent, paths.Count, item.Name, skipped);
        return Ok(new { sent, skipped, total = paths.Count, name = item.Name });
    }

    /// <summary>True if an English .srt file sits next to the video (name.en.srt, name.eng.srt, name.en.*.srt).</summary>
    private static bool HasEnglishSubtitle(string videoPath)
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
