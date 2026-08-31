using System.Net;
using System.Net.Http.Headers;
using BitMiracle.LibTiff.Classic;

namespace SpatialViewer.Formats.Gis.RemoteCog;

internal sealed class HttpRangeTiffStream : TiffStream
{
    private readonly object _gate = new();
    private readonly HttpClient _httpClient;
    private readonly Uri _uri;
    private readonly CancellationToken _cancellationToken;
    private readonly TimeSpan _requestTimeout;
    private readonly int _blockSize;
    private readonly int _maximumCachedBlocks;
    private readonly Dictionary<long, LinkedListNode<CacheEntry>> _cache = new();
    private readonly LinkedList<CacheEntry> _lru = new();
    private long _length;
    private long _position;
    private bool _closed;

    public HttpRangeTiffStream(
        HttpClient httpClient,
        Uri uri,
        CancellationToken cancellationToken,
        TimeSpan requestTimeout,
        int blockSize,
        int maximumCachedBlocks)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(uri);
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Remote COG URI must use HTTP or HTTPS.", nameof(uri));
        }

        if (requestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCachedBlocks);

        _httpClient = httpClient;
        _uri = uri;
        _cancellationToken = cancellationToken;
        _requestTimeout = requestTimeout;
        _blockSize = blockSize;
        _maximumCachedBlocks = maximumCachedBlocks;

        var firstBlock = FetchRange(0, blockSize - 1L, discoverLength: true);
        AddBlockToCache(0, firstBlock);
    }

    public override int Read(object clientData, byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset > buffer.Length - count)
        {
            throw new ArgumentException("Read buffer offset/count exceed the destination array.");
        }

        lock (_gate)
        {
            ThrowIfClosed();
            _cancellationToken.ThrowIfCancellationRequested();
            if (_position >= _length || count == 0)
            {
                return 0;
            }

            var remaining = (int)Math.Min(count, _length - _position);
            var totalRead = 0;
            while (remaining > 0)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var blockIndex = _position / _blockSize;
                var block = GetBlock(blockIndex);
                var blockStart = blockIndex * _blockSize;
                var blockOffset = checked((int)(_position - blockStart));
                var available = block.Length - blockOffset;
                if (available <= 0)
                {
                    throw new InvalidDataException($"Remote TIFF range cache returned an invalid block at index {blockIndex}.");
                }

                var copyLength = Math.Min(remaining, available);
                Buffer.BlockCopy(block, blockOffset, buffer, offset + totalRead, copyLength);
                _position += copyLength;
                totalRead += copyLength;
                remaining -= copyLength;
            }

            return totalRead;
        }
    }

    public override void Write(object clientData, byte[] buffer, int offset, int count) =>
        throw new NotSupportedException("Remote COG stream is read-only.");

    public override long Seek(object clientData, long offset, SeekOrigin origin)
    {
        lock (_gate)
        {
            ThrowIfClosed();
            var target = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => checked(_position + offset),
                SeekOrigin.End => checked(_length + offset),
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            };

            if (target < 0 || target > _length)
            {
                return -1;
            }

            _position = target;
            return _position;
        }
    }

    public override void Close(object clientData)
    {
        lock (_gate)
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            _cache.Clear();
            _lru.Clear();
        }
    }

    public override long Size(object clientData) => _length;

    private byte[] GetBlock(long blockIndex)
    {
        if (_cache.TryGetValue(blockIndex, out var existing))
        {
            _lru.Remove(existing);
            _lru.AddFirst(existing);
            return existing.Value.Content;
        }

        var start = checked(blockIndex * _blockSize);
        if (start >= _length)
        {
            return Array.Empty<byte>();
        }

        var end = Math.Min(_length - 1, checked(start + _blockSize - 1L));
        var content = FetchRange(start, end, discoverLength: false);
        AddBlockToCache(blockIndex, content);
        return content;
    }

    private void AddBlockToCache(long blockIndex, byte[] content)
    {
        if (_cache.Remove(blockIndex, out var existing))
        {
            _lru.Remove(existing);
        }

        var entry = new CacheEntry(blockIndex, content);
        var node = _lru.AddFirst(entry);
        _cache.Add(blockIndex, node);
        while (_cache.Count > _maximumCachedBlocks && _lru.Last is not null)
        {
            var last = _lru.Last;
            _lru.RemoveLast();
            _cache.Remove(last.Value.BlockIndex);
        }
    }

    private byte[] FetchRange(long start, long end, bool discoverLength)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(_cancellationToken);
        timeoutSource.CancelAfter(_requestTimeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _uri);
            request.Headers.Range = new RangeHeaderValue(start, end);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("SpatialViewer.GisCore", "0.4"));
            using var response = _httpClient.Send(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutSource.Token);

            if (response.StatusCode != HttpStatusCode.PartialContent)
            {
                throw new NotSupportedException(
                    $"Remote TIFF server must support HTTP Range requests. Expected 206 Partial Content, received {(int)response.StatusCode} {response.StatusCode}.");
            }

            var contentRange = response.Content.Headers.ContentRange;
            if (contentRange?.From != start || contentRange.To is null || contentRange.Length is null)
            {
                throw new InvalidDataException("Remote TIFF response has an invalid or incomplete Content-Range header.");
            }

            if (discoverLength)
            {
                if (contentRange.Length.Value <= 0)
                {
                    throw new InvalidDataException("Remote TIFF Content-Range reported an invalid source length.");
                }

                _length = contentRange.Length.Value;
            }
            else if (contentRange.Length.Value != _length)
            {
                throw new InvalidDataException(
                    $"Remote TIFF length changed during reading: expected {_length}, received {contentRange.Length.Value}.");
            }

            var actualEnd = contentRange.To.Value;
            if (actualEnd < start || actualEnd > end || actualEnd >= _length)
            {
                throw new InvalidDataException("Remote TIFF response returned an invalid byte range.");
            }

            var content = response.Content.ReadAsByteArrayAsync(timeoutSource.Token).GetAwaiter().GetResult();
            var expectedLength = checked((int)(actualEnd - start + 1));
            if (content.Length != expectedLength)
            {
                throw new InvalidDataException(
                    $"Remote TIFF range {start}-{actualEnd} returned {content.Length} bytes; expected {expectedLength}.");
            }

            return content;
        }
        catch (OperationCanceledException) when (!_cancellationToken.IsCancellationRequested && timeoutSource.IsCancellationRequested)
        {
            throw new TimeoutException($"Remote TIFF range request for '{_uri}' exceeded {_requestTimeout}.");
        }
    }

    private void ThrowIfClosed() => ObjectDisposedException.ThrowIf(_closed, this);

    private sealed record CacheEntry(long BlockIndex, byte[] Content);
}
