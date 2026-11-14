namespace DupliXon.Core;

/// <summary>Describes a receiver (iPad, iPhone, any browser) as reported by the web client.</summary>
public sealed record DeviceInfo(string Name, string Kind, int ScreenWidth, int ScreenHeight, double PixelRatio)
{
    /// <summary>Native panel size in physical pixels, landscape-oriented (long side first).</summary>
    public (int Width, int Height) NativePixels
    {
        get
        {
            var ratio = PixelRatio is > 0 and < 8 ? PixelRatio : 1;
            var w = (int)Math.Round(Math.Max(ScreenWidth, ScreenHeight) * ratio);
            var h = (int)Math.Round(Math.Min(ScreenWidth, ScreenHeight) * ratio);
            return (w, h);
        }
    }
}
