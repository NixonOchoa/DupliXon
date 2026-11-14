using DupliXon.Core;

public class StreamProfileTests
{
    [Fact]
    public void AutoMatchesNativeIPadPixelsInLandscape()
    {
        var ipad = new DeviceInfo("iPad", "iPad", 1032, 1376, 2);
        var p = StreamProfile.For(ipad, QualityPreset.Auto, 60);
        Assert.Equal((2752, 2064), (p.MaxWidth, p.MaxHeight));
    }

    [Fact]
    public void UltraHdTargets4k()
    {
        var phone = new DeviceInfo("iPhone", "iPhone", 430, 932, 3);
        var p = StreamProfile.For(phone, QualityPreset.UltraHd, 30);
        Assert.Equal((3840, 2160), (p.MaxWidth, p.MaxHeight));
        Assert.InRange(p.BitrateKbps, StreamProfile.MinBitrateKbps, StreamProfile.MaxBitrateKbps);
    }
}
