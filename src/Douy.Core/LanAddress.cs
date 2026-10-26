using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Douy;

public static class LanAddress
{
    public static IReadOnlyList<string> GetIPv4()
    {
        var addresses = new List<string>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || IsVirtual(nic))
            {
                continue;
            }

            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork)
                {
                    continue;
                }

                var text = unicast.Address.ToString();
                if (text.StartsWith("169.254.", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!addresses.Contains(text))
                {
                    addresses.Add(text);
                }
            }
        }

        return addresses;
    }

    private static bool IsVirtual(NetworkInterface nic)
    {
        var text = nic.Name + " " + nic.Description;
        var marks = new[]
        {
            "TAP", "VMware", "Virtual", "vEthernet", "Hyper-V", "Wintun",
            "cfw", "SSTAP", "Clash", "Bluetooth", "Loopback", "VPN"
        };
        foreach (var mark in marks)
        {
            if (text.Contains(mark, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsPrivateIPv4(string text)
    {
        if (!IPAddress.TryParse(text, out var address) || address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] == 10
            || (bytes[0] == 192 && bytes[1] == 168)
            || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31);
    }
}
