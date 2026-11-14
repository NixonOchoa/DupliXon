using DupliXon.Core;

public class AnnexBAccessUnitReaderTests
{
    private static readonly byte[] Aud = [0, 0, 0, 1, 0x09, 0xF0];
    private static byte[] Slice(byte marker) => [0, 0, 1, 0x65, marker, marker, marker];

    [Fact]
    public async Task SplitsFramesOnAccessUnitDelimiters()
    {
        var stream = new MemoryStream([.. Aud, .. Slice(1), .. Aud, .. Slice(2), .. Aud, .. Slice(3)]);
        var frames = new List<byte[]>();
        await foreach (var f in new AnnexBAccessUnitReader(stream, chunkSize: 3).ReadAsync()) frames.Add(f);

        Assert.Equal(3, frames.Count);
        Assert.All(frames, f => Assert.Equal<byte>(Aud, f[..Aud.Length]));
        Assert.Equal(2, frames[1][^1]);
    }
}
