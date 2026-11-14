using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.Subgen;

/// <summary>
/// Adds the Subgen button script to Jellyfin's web page while it is sent.
/// Nothing is written to disk, so it needs no file rights and survives Jellyfin updates.
/// </summary>
public class WebInjectorStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.UseMiddleware<WebInjectorMiddleware>();
            next(app);
        };
    }
}

public class WebInjectorMiddleware
{
    private const string Tag = "<!-- subgen-plugin --><script defer src=\"../Subgen/client.js\"></script>";
    private readonly RequestDelegate _next;

    public WebInjectorMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var isIndex = HttpMethods.IsGet(context.Request.Method)
            && (path.EndsWith("/web/", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("/web/index.html", StringComparison.OrdinalIgnoreCase));

        if (!isIndex)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        // Ask for a plain, full answer so the page can be changed.
        context.Request.Headers.Remove("Accept-Encoding");
        context.Request.Headers.Remove("If-None-Match");
        context.Request.Headers.Remove("If-Modified-Since");

        var original = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await _next(context).ConfigureAwait(false);

            buffer.Position = 0;
            var isHtml = context.Response.StatusCode == StatusCodes.Status200OK
                && (context.Response.ContentType ?? string.Empty).Contains("text/html", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrEmpty(context.Response.Headers.ContentEncoding);

            if (isHtml)
            {
                var html = await new StreamReader(buffer, Encoding.UTF8).ReadToEndAsync().ConfigureAwait(false);
                if (!html.Contains("subgen-plugin", StringComparison.Ordinal))
                {
                    var at = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
                    html = at >= 0 ? html.Insert(at, Tag) : html + Tag;
                }

                var bytes = Encoding.UTF8.GetBytes(html);
                context.Response.ContentLength = bytes.Length;
                context.Response.Headers.Remove("ETag");
                context.Response.Headers.CacheControl = "no-cache";
                context.Response.Body = original;
                await original.WriteAsync(bytes).ConfigureAwait(false);
            }
            else
            {
                context.Response.Body = original;
                await buffer.CopyToAsync(original).ConfigureAwait(false);
            }
        }
        finally
        {
            context.Response.Body = original;
        }
    }
}
