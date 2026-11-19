using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace DupliXon.Core;

/// <summary>
/// Reads H.264 frames from an FLV stream and returns each one as an Annex-B access unit.
/// FLV tags carry their own length, so a frame is handed over the instant it is written —
/// unlike raw Annex-B, where a frame's end is only known when the next one starts (one frame of lag).
/// </summary>
public sealed class FlvH264Reader(Stream source)
{
    private static readonly byte[] StartCode = [0, 0, 0, 1];
    private byte[] _sps = [];
    private byte[] _pps = [];
    private int _lengthSize = 4;

    public async IAsyncEnumerable<byte[]> ReadAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        var header = new byte[13]; // FLV header (9) + PreviousTagSize0 (4)
        if (!await FillAsync(header, ct).ConfigureAwait(false)) yield break;
        if (header[0] != 'F' || header[1] != 'L' || header[2] != 'V') throw new InvalidDataException("Not an FLV stream.");
        var extra = (int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(5)) - 9;
        if (extra > 0 && !await FillAsync(new byte[extra], ct).ConfigureAwait(false)) yield break;

        var tagHeader = new byte[11];
        while (await FillAsync(tagHeader, ct).ConfigureAwait(false))
        {
            var type = tagHeader[0] & 0x1F;
            var size = tagHeader[1] << 16 | tagHeader[2] << 8 | tagHeader[3];
            var body = new byte[size + 4]; // tag data + PreviousTagSize
            if (!await FillAsync(body, ct).ConfigureAwait(false)) yield break;
            if (type != 9 || size < 5) continue;

            var frame = ParseVideo(body.AsSpan(0, size));
            if (frame is not null) yield return frame;
        }
    }

    private byte[]? ParseVideo(ReadOnlySpan<byte> data)
    {
        var keyframe = (data[0] >> 4) == 1;
        var codec = data[0] & 0x0F;
        if (codec != 7) return null; // AVC only
        var packetType = data[1];
        var payload = data[5..];

        if (packetType == 0) { ParseConfig(payload); return null; }
        if (packetType != 1) return null;

        using var ms = new MemoryStream(payload.Length + 64);
        var hasParams = false;
        var nals = new List<(int Offset, int Length)>();
        var pos = 0;
        while (pos + _lengthSize <= payload.Length)
        {
            var len = 0;
            for (var i = 0; i < _lengthSize; i++) len = len << 8 | payload[pos + i];
            pos += _lengthSize;
            if (len <= 0 || pos + len > payload.Length) break;
            var nalType = payload[pos] & 0x1F;
            if (nalType is 7 or 8) hasParams = true;
            nals.Add((pos, len));
            pos += len;
        }

        // Decoders need SPS/PPS before every IDR so a receiver can join or recover at any keyframe.
        if (keyframe && !hasParams && _sps.Length > 0)
        {
            ms.Write(StartCode); ms.Write(_sps);
            ms.Write(StartCode); ms.Write(_pps);
        }
        foreach (var (offset, length) in nals)
        {
            ms.Write(StartCode);
            ms.Write(payload.Slice(offset, length));
        }
        return ms.Length > 0 ? ms.ToArray() : null;
    }

    private void ParseConfig(ReadOnlySpan<byte> c)
    {
        // AVCDecoderConfigurationRecord
        if (c.Length < 7) return;
        _lengthSize = (c[4] & 0x03) + 1;
        var pos = 5;
        var spsCount = c[pos++] & 0x1F;
        for (var i = 0; i < spsCount && pos + 2 <= c.Length; i++)
        {
            var len = c[pos] << 8 | c[pos + 1]; pos += 2;
            if (i == 0) _sps = c.Slice(pos, len).ToArray();
            pos += len;
        }
        if (pos >= c.Length) return;
        var ppsCount = c[pos++];
        for (var i = 0; i < ppsCount && pos + 2 <= c.Length; i++)
        {
            var len = c[pos] << 8 | c[pos + 1]; pos += 2;
            if (i == 0) _pps = c.Slice(pos, len).ToArray();
            pos += len;
        }
    }

    private async Task<bool> FillAsync(byte[] buffer, CancellationToken ct)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await source.ReadAsync(buffer.AsMemory(read), ct).ConfigureAwait(false);
            if (n == 0) return false;
            read += n;
        }
        return true;
    }
}
