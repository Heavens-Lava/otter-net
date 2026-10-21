using System.Runtime.InteropServices;
using Otter.Runtime;

// usage: Probe <host> <user> <password>
unsafe
{
    string host = args[0], user = args[1], password = args[2];
    foreach (string variant in new[] { "none", "set", "set-mustunderstand", "setex" })
    {
        var pool = Guid.NewGuid();
        byte[] creation = Psrp.Fragment(1, Psrp.Message(2, Psrp.SessionCapability, pool, Guid.Empty, Psrp.SessionCapabilityXml()))
            .Concat(Psrp.Fragment(2, Psrp.Message(2, Psrp.InitRunspacePool, pool, Guid.Empty, Psrp.InitRunspacePoolXml()))).ToArray();
        string xml = "<creationXml xmlns=\"http://schemas.microsoft.com/powershell\">" + Convert.ToBase64String(creation) + "</creationXml>";
        Native.WSManInitialize(1, out IntPtr api);
        IntPtr u = Marshal.StringToHGlobalUni(user), p = Marshal.StringToHGlobalUni(password);
        var creds = new Native.Creds { mechanism = 4, user = u, password = p };
        uint s = Native.WSManCreateSession(api, Marshal.StringToHGlobalUni($"http://{host}:5985/wsman"), 0, &creds, IntPtr.Zero, out IntPtr session);
        IntPtr sin = Marshal.StringToHGlobalUni("stdin pr"), sout = Marshal.StringToHGlobalUni("stdout");
        var inSet = new Native.StreamSet { count = 1, ids = (IntPtr)(&sin) };
        var outSet = new Native.StreamSet { count = 1, ids = (IntPtr)(&sout) };
        var startup = new Native.Startup { input = (IntPtr)(&inSet), output = (IntPtr)(&outSet) };
        var option = new Native.Option { name = Marshal.StringToHGlobalUni("protocolversion"), value = Marshal.StringToHGlobalUni("2.3"), mustComply = 1 };
        IntPtr type = Marshal.StringToHGlobalUni("");
        var setEx = new Native.OptionSetEx { count = 1, options = (IntPtr)(&option), mustUnderstand = variant == "set-mustunderstand" ? 1 : 0, types = variant == "setex" ? (IntPtr)(&type) : IntPtr.Zero };
        var data = new Native.Data { type = 1, length = (uint)xml.Length, pointer = Marshal.StringToHGlobalUni(xml) };
        var done = new ManualResetEventSlim();
        Native.Result = (0, null);
        Native.Done = done;
        var async = new Native.Async { context = IntPtr.Zero, callback = (IntPtr)(delegate* unmanaged<IntPtr, uint, Native.Error*, IntPtr, IntPtr, IntPtr, IntPtr, void>)&Native.Callback };
        IntPtr options = variant == "none" ? IntPtr.Zero : (IntPtr)(&setEx);
        Native.WSManCreateShellEx(session, 0, Marshal.StringToHGlobalUni("http://schemas.microsoft.com/powershell/Microsoft.PowerShell"),
            Marshal.StringToHGlobalUni(pool.ToString().ToUpperInvariant()), &startup, options, &data, &async, out IntPtr shell);
        done.Wait(TimeSpan.FromSeconds(60));
        Console.WriteLine($"{variant}: session {s}, create shell error {Native.Result.code}: {Native.Result.detail}");
        if (shell != IntPtr.Zero) { var d2 = new ManualResetEventSlim(); Native.Done = d2; Native.WSManCloseShell(shell, 0, &async); d2.Wait(TimeSpan.FromSeconds(30)); }
        Native.WSManCloseSession(session, 0);
        Native.WSManDeinitialize(api, 0);
    }
}

static unsafe partial class Native
{
    public static (uint code, string? detail) Result;
    public static ManualResetEventSlim? Done;
    [StructLayout(LayoutKind.Sequential)] public struct Creds { public uint mechanism; public IntPtr user; public IntPtr password; }
    [StructLayout(LayoutKind.Sequential)] public struct StreamSet { public uint count; public IntPtr ids; }
    [StructLayout(LayoutKind.Sequential)] public struct Startup { public IntPtr input; public IntPtr output; public uint idle; public IntPtr dir; public IntPtr vars; public IntPtr name; }
    [StructLayout(LayoutKind.Sequential)] public struct Option { public IntPtr name; public IntPtr value; public int mustComply; }
    // WSMAN_OPTION_SET is the first three fields; WSMAN_OPTION_SETEX adds optionTypes.
    [StructLayout(LayoutKind.Sequential)] public struct OptionSetEx { public uint count; public IntPtr options; public int mustUnderstand; public IntPtr types; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] public struct Data { [FieldOffset(0)] public int type; [FieldOffset(8)] public uint length; [FieldOffset(16)] public IntPtr pointer; }
    [StructLayout(LayoutKind.Sequential)] public struct Async { public IntPtr context; public IntPtr callback; }
    [StructLayout(LayoutKind.Sequential)] public struct Error { public uint code; public IntPtr detail; public IntPtr language; public IntPtr machine; public IntPtr plugin; }
    [UnmanagedCallersOnly] public static void Callback(IntPtr context, uint flags, Error* error, IntPtr shell, IntPtr command, IntPtr operation, IntPtr data)
    {
        Result = error != null ? (error->code, Marshal.PtrToStringUni(error->detail)) : (0u, null);
        Done?.Set();
    }
    [LibraryImport("WsmSvc.dll")] public static partial uint WSManInitialize(uint flags, out IntPtr api);
    [LibraryImport("WsmSvc.dll")] public static partial uint WSManDeinitialize(IntPtr api, uint flags);
    [LibraryImport("WsmSvc.dll")] public static partial uint WSManCreateSession(IntPtr api, IntPtr connection, uint flags, Creds* creds, IntPtr proxy, out IntPtr session);
    [LibraryImport("WsmSvc.dll")] public static partial uint WSManCloseSession(IntPtr session, uint flags);
    [LibraryImport("WsmSvc.dll")] public static partial void WSManCreateShellEx(IntPtr session, uint flags, IntPtr uri, IntPtr id, Startup* startup, IntPtr options, Data* xml, Async* async, out IntPtr shell);
    [LibraryImport("WsmSvc.dll")] public static partial void WSManCloseShell(IntPtr shell, uint flags, Async* async);
}
