using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Otter.Runtime;

// get system information "network" into list (D69) on Windows: the network
// interfaces as .NET Framework's NetworkInterface.GetAllNetworkInterfaces()
// lists them under Windows PowerShell 5.1, which otterPS uses.
//
// Both read GetAdaptersAddresses, but .NET Framework asks with
// GAA_FLAG_INCLUDE_WINS_INFO | GAA_FLAG_INCLUDE_GATEWAYS, while .NET 5 and
// later add GAA_FLAG_INCLUDE_ALL_INTERFACES, which also lists the hidden
// filter interfaces ("Ethernet-WFP Native MAC Layer LightWeight
// Filter-0000", "...-QoS Packet Scheduler-0000") and disconnected ones that
// report themselves up. Measured on a Windows CI runner (SPEC-QUESTIONS
// W5-1), so the adapters are read here with .NET Framework's flags, in the
// order Windows gives them: the friendly name, the operational status (1 is
// up), the interface type (24 is loopback) and the first IPv4 unicast
// address.
[SupportedOSPlatform("windows")]
public static unsafe partial class OtterNetworkAdapters
{
    const uint AfUnspecified = 0;
    const uint IncludeWinsInfo = 0x40;
    const uint IncludeGateways = 0x80;
    const int ErrorBufferOverflow = 111;
    const int ErrorNoData = 232;
    const uint IfTypeLoopback = 24;
    const uint IfOperStatusUp = 1;
    const ushort AfInet = 2;

    [LibraryImport("iphlpapi.dll")]
    private static partial uint GetAdaptersAddresses(uint family, uint flags, IntPtr reserved, IntPtr addresses, ref uint size);

    // (friendly name, first IPv4 address or null) for each interface that is
    // up and not loopback; null when Windows will not list them.
    public static List<(string Name, string? Address)>? List()
    {
        uint size = 16 * 1024;
        for (int attempt = 0; attempt < 4; attempt++)
        {
            IntPtr buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                uint result = GetAdaptersAddresses(AfUnspecified, IncludeWinsInfo | IncludeGateways, IntPtr.Zero, buffer, ref size);
                if (result == ErrorBufferOverflow) continue;
                if (result == ErrorNoData) return new();
                if (result != 0) return null;
                var list = new List<(string, string?)>();
                // IP_ADAPTER_ADDRESSES (x64 and Arm64 layout): Next at 8,
                // FirstUnicastAddress at 24, FriendlyName at 72, IfType at 100,
                // OperStatus at 104.
                for (byte* adapter = (byte*)buffer; adapter != null; adapter = *(byte**)(adapter + 8))
                {
                    uint type = *(uint*)(adapter + 100);
                    uint status = *(uint*)(adapter + 104);
                    if (status != IfOperStatusUp || type == IfTypeLoopback) continue;
                    string name = Marshal.PtrToStringUni(*(IntPtr*)(adapter + 72)) ?? "";
                    list.Add((name, FirstIPv4(*(byte**)(adapter + 24))));
                }
                return list;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        return null;
    }

    // IP_ADAPTER_UNICAST_ADDRESS: Next at 8, Address.lpSockaddr at 16.
    static string? FirstIPv4(byte* unicast)
    {
        for (; unicast != null; unicast = *(byte**)(unicast + 8))
        {
            byte* socketAddress = *(byte**)(unicast + 16);
            if (socketAddress == null || *(ushort*)socketAddress != AfInet) continue;
            return $"{socketAddress[4]}.{socketAddress[5]}.{socketAddress[6]}.{socketAddress[7]}";
        }
        return null;
    }
}
