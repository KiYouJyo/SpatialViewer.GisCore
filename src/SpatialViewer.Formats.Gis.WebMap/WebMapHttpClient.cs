using System.Net;
using System.Net.Http.Headers;
using SpatialViewer.Gis.Core;

namespace SpatialViewer.Formats.Gis.WebMap;

internal sealed class WebMapHttpClient
{
    private readonly HttpClient _httpClient;
    private readonly TimeSpan _requestTimeout;
    private readonly int _maximumRetries;
    private readonly TimeSpan _retryDelay;

    public WebMapHttpClient(
        HttpClient httpClient,
        TimeSpan requestTimeout,
        int maximumRetries,
        TimeSpan retryDelay)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        if (requestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(maximumRetries);
        if (retryDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retryDelay));
        }

        _httpClient = httpClient;
        _requestTimeout = requestTimeout;
        _maximumRetries = maximumRetries;
        _retryDelay = retryDelay;
    }

    public async ValueTask<WebMapHttpResult> GetAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);
        for (var attempt = 0; attempt <= _maximumRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(_requestTimeout);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.UserAgent.Add(new ProductInfoHeaderValue("SpatialViewer.GisCore", "0.4"));
                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeoutSource.Token).ConfigureAwait(false);

                if (IsTransientStatus(response.StatusCode) && attempt < _maximumRetries)
                {
                    await DelayBeforeRetryAsync(response, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsByteArrayAsync(timeoutSource.Token).ConfigureAwait(false);
                if (content.Length == 0)
                {
                    throw new InvalidDataException($"Web map request '{uri}' returned an empty payload.");
                }

                return new WebMapHttpResult(
                    content,
                    ParseMediaType(response.Content.Headers.ContentType?.MediaType),
                    response.Headers.ETag?.Tag,
                    response.Content.Headers.LastModified ?? response.Headers.Date);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutSource.IsCancellationRequested)
            {
                if (attempt < _maximumRetries)
                {
                    await DelayBeforeRetryAsync(null, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                throw new TimeoutException($"Web map request '{uri}' exceeded {_requestTimeout}.");
            }
            catch (HttpRequestException) when (attempt < _maximumRetries)
            {
                await DelayBeforeRetryAsync(null, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException("Web map retry loop terminated unexpectedly.");
    }

    private async ValueTask DelayBeforeRetryAsync(
        HttpResponseMessage? response,
        CancellationToken cancellationToken)
    {
        var delay = response?.Headers.RetryAfter?.Delta ?? _retryDelay;
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsTransientStatus(HttpStatusCode statusCode) => statusCode is
        HttpStatusCode.RequestTimeout or
        HttpStatusCode.TooManyRequests or
        HttpStatusCode.InternalServerError or
        HttpStatusCode.BadGateway or
        HttpStatusCode.ServiceUnavailable or
        HttpStatusCode.GatewayTimeout;

    private static TileContentType ParseMediaType(string? mediaType) => mediaType?.ToLowerInvariant() switch
    {
        "image/png" => TileContentType.Png,
        "image/jpeg" or "image/jpg" => TileContentType.Jpeg,
        "image/webp" => TileContentType.WebP,
        "application/vnd.mapbox-vector-tile" or "application/x-protobuf" or "application/protobuf" => TileContentType.VectorPbf,
        _ => TileContentType.Unknown,
    };
}

internal sealed record WebMapHttpResult(
    byte[] Content,
    TileContentType ContentType,
    string? EntityTag,
    DateTimeOffset? LastModified);

internal static class WebMapUriBuilder
{
    public static Uri Build(string source, IReadOnlyList<KeyValuePair<string, string>> parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(parameters);
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Web map source must be an absolute HTTP or HTTPS URI.", nameof(source));
        }

        var builder = new UriBuilder(uri);
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(builder.Query))
        {
            parts.Add(builder.Query.TrimStart('?'));
        }

        foreach (var parameter in parameters)
        {
            parts.Add($"{Uri.EscapeDataString(parameter.Key)}={Uri.EscapeDataString(parameter.Value)}");
        }

        builder.Query = string.Join('&', parts);
        return builder.Uri;
    }
}
