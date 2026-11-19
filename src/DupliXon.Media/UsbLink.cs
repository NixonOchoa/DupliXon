using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace DupliXon.Media;

/// <summary>What is plugged in over USB right now.</summary>
/// <param name="DeviceName">"iPhone", "iPad"… when an Apple device is connected by cable.</param>
/// <param name="Address">This PC's IPv4 on the USB network link (iPhone/iPad Personal Hotspot over USB), if active.</param>
/// <param name="DriverMissing">An Apple device is plugged in but Windows lacks Apple's USB driver.</param>
public sealed record UsbState(string? DeviceName, string? Address, bool DriverMissing)
{
    public static readonly UsbState None = new(null, null, false);
    public bool Plugged => DeviceName is not null || Address is not null;
    public bool Ready => Address is not null;
}

/// <summary>
/// Detects iPhone/iPad connected by cable. Safari can only reach the PC over USB through the device's
/// Personal Hotspot ("Compartir Internet"), which shows up in Windows as an "Apple Mobile Device Ethernet"
/// network adapter: a wired link with far lower and steadier latency than Wi-Fi.
/// </summary>
public static class UsbLink
{
    private const string AppleVendor = "VID_05AC";

    public static UsbState Detect()
    {
        var address = UsbNetworkAddress(out var nicName);
        var (device, driverMissing) = PluggedAppleDevice();
        return new UsbState(device ?? nicName, address, driverMissing && address is null);
    }

    /// <summary>True when the address belongs to a USB tethering link (used to label sessions).</summary>
    public static bool IsUsbNetwork(NetworkInterface nic) =>
        nic.Description.Contains("Apple Mobile Device Ethernet", StringComparison.OrdinalIgnoreCase) ||
        nic.Description.Contains("Remote NDIS", StringComparison.OrdinalIgnoreCase);

    private static string? UsbNetworkAddress(out string? deviceName)
    {
        deviceName = null;
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || !IsUsbNetwork(nic)) continue;
            var ip = nic.GetIPProperties().UnicastAddresses
                .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address;
            if (ip is null) continue;
            deviceName = nic.Description.Contains("Apple", StringComparison.OrdinalIgnoreCase) ? "iPhone / iPad" : "Dispositivo USB";
            return ip.ToString();
        }
        return null;
    }

    private static (string? Name, bool DriverMissing) PluggedAppleDevice()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, PNPDeviceID, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE PNPDeviceID LIKE 'USB\\\\" + AppleVendor + "%'");
            string? name = null;
            var driverOk = false;
            foreach (var o in searcher.Get())
            {
                var n = o["Name"] as string ?? "";
                var error = Convert.ToInt32(o["ConfigManagerErrorCode"] ?? 0);
                if (n.Contains("iPad", StringComparison.OrdinalIgnoreCase)) name = "iPad";
                else if (n.Contains("iPhone", StringComparison.OrdinalIgnoreCase)) name ??= "iPhone";
                else name ??= "iPhone / iPad";
                if (error == 0 && n.Contains("Apple Mobile Device", StringComparison.OrdinalIgnoreCase)) driverOk = true;
            }
            return (name, name is not null && !driverOk);
        }
        catch
        {
            return (null, false);
        }
    }
}
