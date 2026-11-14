using System.Runtime.CompilerServices;

namespace DupliXon.Core;

/// <summary>
/// Splits a raw H.264 Annex-B byte stream into access units (one per video frame),
/// using Access Unit Delimiter NAL units (type 9) as boundaries.
/// </summary>
public sealed class AnnexBAccessUnitReader(Stream source, int chunkSize = 256 * 1024)
{
    private const int AudNalType = 9;
    private byte[] _buffer = new byte[2 * 1024 * 1024];
    private int _length;

    public async IAsyncEnumerable<byte[]> ReadAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        var chunk = new byte[chunkSize];
        var scan = 0;
        while (true)
        {
            var read = await source.ReadAsync(chunk, ct).ConfigureAwait(false);
            if (read == 0) break;
            Append(chunk.AsSpan(0, read));

            while (scan + 3 < _length)
            {
                if (_buffer[scan] == 0 && _buffer[scan + 1] == 0 && _buffer[scan + 2] == 1 &&
                    (_buffer[scan + 3] & 0x1F) == AudNalType)
                {
                    var start = scan > 0 && _buffer[scan - 1] == 0 ? scan - 1 : scan;
                    if (start > 0)
                    {
                        yield return _buffer[..start];
                        Buffer.BlockCopy(_buffer, start, _buffer, 0, _length - start);
                        _length -= start;
                    }
                    scan = 4;
                    continue;
                }
                scan++;
            }
        }

        if (_length > 0) yield return _buffer[.._length];
    }

    private void Append(ReadOnlySpan<byte> data)
    {
        if (_length + data.Length > _buffer.Length)
            Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, _length + data.Length));
        data.CopyTo(_buffer.AsSpan(_length));
        _length += data.Length;
    }
}
