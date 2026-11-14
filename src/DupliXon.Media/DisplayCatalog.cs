using Vortice.DXGI;

namespace DupliXon.Media;

/// <summary>A monitor as DXGI sees it; (AdapterIndex, OutputIndex) is exactly what ffmpeg's ddagrab needs.</summary>
public sealed record DisplayOutput(
    int AdapterIndex, int OutputIndex, string DeviceName, string AdapterName,
    int Left, int Top, int Width, int Height, bool IsPrimary, bool IsVirtual)
{
    public string Key => DeviceName;
}

/// <summary>Enumerates the monitors attached to the desktop, including virtual (IddCx) displays.</summary>
public static class DisplayCatalog
{
    private static readonly string[] VirtualHints = ["virtual", "iddcx", "parsec", "idd ", "dcv", "spacedesk", "indirect"];

    public static IReadOnlyList<DisplayOutput> GetOutputs()
    {
        var list = new List<DisplayOutput>();
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        for (uint a = 0; factory.EnumAdapters1(a, out var adapter).Success; a++)
        {
            using (adapter)
            {
                var adapterName = adapter.Description1.Description.Trim();
                var isVirtualAdapter = VirtualHints.Any(h => adapterName.Contains(h, StringComparison.OrdinalIgnoreCase));
                for (uint o = 0; adapter.EnumOutputs(o, out var output).Success; o++)
                {
                    using (output)
                    {
                        var d = output.Description;
                        if (!d.AttachedToDesktop || list.Any(x => x.DeviceName == d.DeviceName)) continue;
                        var r = d.DesktopCoordinates;
                        list.Add(new DisplayOutput((int)a, (int)o, d.DeviceName, adapterName,
                            r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top,
                            IsPrimary: r.Left == 0 && r.Top == 0,
                            IsVirtual: isVirtualAdapter || DisplayModes.IsVirtualMonitor(d.DeviceName)));
                    }
                }
            }
        }
        return list.OrderByDescending(x => x.IsPrimary).ThenBy(x => x.IsVirtual).ThenBy(x => x.Left).ToList();
    }
}
