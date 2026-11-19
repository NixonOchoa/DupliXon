using DupliXon.Core;

public class FlvH264ReaderTests
{
    private static byte[] Tag(byte[] data)
    {
        var size = data.Length;
        byte[] header = [9, (byte)(size >> 16), (byte)(size >> 8), (byte)size, 0, 0, 0, 0, 0, 0, 0];
        var total = 11 + size;
        return [.. header, .. data, (byte)(total >> 24), (byte)(total >> 16), (byte)(total >> 8), (byte)total];
    }

    [Fact]
    public async Task ConvertsAvccToAnnexBAndPrependsParameterSetsOnKeyframes()
    {
        byte[] sps = [0x67, 0x64, 0x00, 0x1F];
        byte[] pps = [0x68, 0xEE];
        byte[] config = [0x17, 0, 0, 0, 0, 1, 0x64, 0x00, 0x1F, 0xFF, 0xE1, 0, 4, .. sps, 1, 0, 2, .. pps];
        byte[] idr = [0x17, 1, 0, 0, 0, 0, 0, 0, 3, 0x65, 0xAA, 0xBB];
        byte[] inter = [0x27, 1, 0, 0, 0, 0, 0, 0, 2, 0x41, 0xCC];
        byte[] flv = [(byte)'F', (byte)'L', (byte)'V', 1, 1, 0, 0, 0, 9, 0, 0, 0, 0, .. Tag(config), .. Tag(idr), .. Tag(inter)];

        var frames = new List<byte[]>();
        await foreach (var f in new FlvH264Reader(new MemoryStream(flv)).ReadAsync()) frames.Add(f);

        Assert.Equal(2, frames.Count);
        Assert.Equal<byte>([0, 0, 0, 1, .. sps, 0, 0, 0, 1, .. pps, 0, 0, 0, 1, 0x65, 0xAA, 0xBB], frames[0]);
        Assert.Equal<byte>([0, 0, 0, 1, 0x41, 0xCC], frames[1]);
    }
}
