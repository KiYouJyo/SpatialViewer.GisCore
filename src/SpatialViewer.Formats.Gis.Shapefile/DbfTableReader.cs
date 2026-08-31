using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace SpatialViewer.Formats.Gis.Shapefile;

internal sealed class DbfTableReader : IAsyncDisposable
{
    private readonly FileStream _stream;
    private readonly Encoding _encoding;
    private readonly List<DbfField> _fields;
    private readonly int _headerLength;
    private readonly int _recordLength;

    private DbfTableReader(
        FileStream stream,
        Encoding encoding,
        List<DbfField> fields,
        int headerLength,
        int recordLength,
        int recordCount)
    {
        _stream = stream;
        _encoding = encoding;
        _fields = fields;
        _headerLength = headerLength;
        _recordLength = recordLength;
        RecordCount = recordCount;
    }

    public int RecordCount { get; }

    public static async ValueTask<DbfTableReader> OpenAsync(
        string dbfPath,
        string cpgPath,
        CancellationToken cancellationToken)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var stream = new FileStream(
            dbfPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.RandomAccess);

        try
        {
            var header = new byte[32];
            await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);

            var recordCount = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4, 4));
            var headerLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(8, 2));
            var recordLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(10, 2));
            var languageDriver = header[29];

            if (recordCount < 0 || headerLength < 33 || recordLength < 1)
            {
                throw new InvalidDataException("DBF header contains invalid record or header lengths.");
            }

            if ((headerLength - 33) % 32 != 0)
            {
                throw new InvalidDataException("DBF field descriptor section is not aligned to 32-byte entries.");
            }

            var fieldCount = (headerLength - 33) / 32;
            var fields = new List<DbfField>(fieldCount);
            var descriptor = new byte[32];

            for (var index = 0; index < fieldCount; index++)
            {
                await stream.ReadExactlyAsync(descriptor, cancellationToken).ConfigureAwait(false);
                fields.Add(ParseField(descriptor, index));
            }

            var terminator = stream.ReadByte();
            if (terminator != 0x0D)
            {
                throw new InvalidDataException("DBF field descriptor terminator is missing.");
            }

            var fieldBytes = fields.Sum(field => field.Length);
            if (fieldBytes + 1 > recordLength)
            {
                throw new InvalidDataException("DBF field lengths exceed the declared record length.");
            }

            var encoding = await ResolveEncodingAsync(cpgPath, languageDriver, cancellationToken)
                .ConfigureAwait(false);
            return new DbfTableReader(
                stream,
                encoding,
                fields,
                headerLength,
                recordLength,
                recordCount);
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask<DbfRecord> ReadRecordAsync(
        int zeroBasedIndex,
        CancellationToken cancellationToken)
    {
        if ((uint)zeroBasedIndex >= (uint)RecordCount)
        {
            throw new ArgumentOutOfRangeException(nameof(zeroBasedIndex));
        }

        var buffer = new byte[_recordLength];
        _stream.Position = checked((long)_headerLength + ((long)zeroBasedIndex * _recordLength));
        await _stream.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);

        if (buffer[0] == 0x2A)
        {
            return new DbfRecord(true, new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase));
        }

        if (buffer[0] != 0x20)
        {
            throw new InvalidDataException($"DBF record {zeroBasedIndex + 1} has invalid deletion flag 0x{buffer[0]:X2}.");
        }

        var attributes = new Dictionary<string, object?>(_fields.Count, StringComparer.OrdinalIgnoreCase);
        var offset = 1;

        foreach (var field in _fields)
        {
            var raw = buffer.AsSpan(offset, field.Length);
            attributes.Add(field.Name, ParseFieldValue(field, raw));
            offset += field.Length;
        }

        return new DbfRecord(false, attributes);
    }

    public ValueTask DisposeAsync() => _stream.DisposeAsync();

    private object? ParseFieldValue(DbfField field, ReadOnlySpan<byte> raw)
    {
        return field.Type switch
        {
            'C' => DecodeText(raw),
            'N' or 'F' => ParseNumber(field, raw),
            'L' => ParseLogical(raw),
            'D' => ParseDate(raw),
            'I' => ParseInt32(raw, field),
            'Y' => ParseCurrency(raw, field),
            _ => throw new NotSupportedException(
                $"DBF field '{field.Name}' uses unsupported field type '{field.Type}'. Memo/binary sidecars are not silently decoded."),
        };
    }

    private string DecodeText(ReadOnlySpan<byte> raw) =>
        _encoding.GetString(raw).TrimEnd('\0', ' ');

    private object? ParseNumber(DbfField field, ReadOnlySpan<byte> raw)
    {
        var text = Encoding.ASCII.GetString(raw).Trim();
        if (text.Length == 0)
        {
            return null;
        }

        if (field.DecimalCount == 0 &&
            long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
        {
            return integer;
        }

        if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var decimalValue))
        {
            return decimalValue;
        }

        throw new InvalidDataException($"DBF numeric field '{field.Name}' contains invalid value '{text}'.");
    }

    private static bool? ParseLogical(ReadOnlySpan<byte> raw)
    {
        if (raw.IsEmpty)
        {
            return null;
        }

        return char.ToUpperInvariant((char)raw[0]) switch
        {
            'Y' or 'T' => true,
            'N' or 'F' => false,
            '?' or ' ' or '\0' => null,
            var value => throw new InvalidDataException($"DBF logical field contains invalid value '{value}'."),
        };
    }

    private static DateOnly? ParseDate(ReadOnlySpan<byte> raw)
    {
        var text = Encoding.ASCII.GetString(raw).Trim();
        if (text.Length == 0 || text == "00000000")
        {
            return null;
        }

        if (DateOnly.TryParseExact(
            text,
            "yyyyMMdd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date))
        {
            return date;
        }

        throw new InvalidDataException($"DBF date field contains invalid value '{text}'.");
    }

    private static int ParseInt32(ReadOnlySpan<byte> raw, DbfField field)
    {
        if (raw.Length != 4)
        {
            throw new InvalidDataException($"DBF integer field '{field.Name}' must be four bytes.");
        }

        return BinaryPrimitives.ReadInt32LittleEndian(raw);
    }

    private static decimal ParseCurrency(ReadOnlySpan<byte> raw, DbfField field)
    {
        if (raw.Length != 8)
        {
            throw new InvalidDataException($"DBF currency field '{field.Name}' must be eight bytes.");
        }

        return BinaryPrimitives.ReadInt64LittleEndian(raw) / 10000m;
    }

    private static DbfField ParseField(ReadOnlySpan<byte> descriptor, int index)
    {
        var nameLength = descriptor[..11].IndexOf((byte)0);
        if (nameLength < 0)
        {
            nameLength = 11;
        }

        var name = Encoding.ASCII.GetString(descriptor[..nameLength]).Trim();
        if (name.Length == 0)
        {
            name = $"FIELD_{index + 1}";
        }

        var type = char.ToUpperInvariant((char)descriptor[11]);
        var length = descriptor[16];
        var decimalCount = descriptor[17];

        if (length == 0)
        {
            throw new InvalidDataException($"DBF field '{name}' has zero length.");
        }

        return new DbfField(name, type, length, decimalCount);
    }

    private static async ValueTask<Encoding> ResolveEncodingAsync(
        string cpgPath,
        byte languageDriver,
        CancellationToken cancellationToken)
    {
        if (File.Exists(cpgPath))
        {
            var value = (await File.ReadAllTextAsync(cpgPath, cancellationToken).ConfigureAwait(false)).Trim();
            if (value.Length == 0)
            {
                throw new InvalidDataException("The CPG sidecar exists but does not declare an encoding.");
            }

            try
            {
                return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var codePage)
                    ? Encoding.GetEncoding(codePage)
                    : Encoding.GetEncoding(value);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException($"Unsupported CPG encoding '{value}'.", exception);
            }
        }

        var fallbackCodePage = languageDriver switch
        {
            0x03 or 0x57 => 1252,
            0x7A => 936,
            0x7B => 932,
            0x78 => 950,
            _ => 28591,
        };

        return Encoding.GetEncoding(fallbackCodePage);
    }

    private sealed record DbfField(string Name, char Type, int Length, int DecimalCount);
}

internal sealed record DbfRecord(bool IsDeleted, Dictionary<string, object?> Attributes);
