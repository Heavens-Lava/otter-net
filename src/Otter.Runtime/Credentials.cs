using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Otter.Runtime;

public sealed class CredentialProblem(string message, string? suggestion = null) : Exception(message)
{
    public string? Suggestion { get; } = suggestion;
}

// Stored credentials (D81): a port of otterPS's Set-OtterCredential,
// Get-OtterCredential and Remove-OtterCredential. Each credential is a file,
// %LOCALAPPDATA%\Otter\Credentials\<name>.cred, holding the secret encrypted
// with DPAPI for the current user (ProtectedData, CurrentUser scope), in
// base64 - the same files otterPS reads and writes.
[SupportedOSPlatform("windows")]
public static partial class OtterCredentials
{
    static readonly Regex SafeName = new("^[A-Za-z0-9_.\\- ]+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static void AssertName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !SafeName.IsMatch(name))
        {
            throw new CredentialProblem($"\"{name}\" is not a valid credential name.",
                "Credential names may only use letters, numbers, spaces, dots, dashes, and underscores.");
        }
    }

    // Get-OtterCredentialStorePath: made on first use.
    static string StoreFolder()
    {
        string folder = Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "", "Otter", "Credentials");
        if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
        return folder;
    }

    public static void Set(string name, string secret)
    {
        AssertName(name);
        string path = Path.Combine(StoreFolder(), name + ".cred");
        string step = "Protect";
        try
        {
            byte[] protectedBytes = Dpapi.Protect(Encoding.UTF8.GetBytes(secret));
            step = "WriteAllText";
            File.WriteAllText(path, Convert.ToBase64String(protectedBytes));
        }
        catch (Exception e) when (e is CryptographicException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            int arguments = step == "Protect" ? 3 : 2;
            throw new CredentialProblem($"I could not save the credential \"{name}\". Exception calling \"{step}\" with \"{arguments}\" argument(s): \"{e.Message}\"");
        }
    }

    // gone when there is no credential by that name, or it cannot be read
    // (another account, another machine, a damaged file).
    public static string? Get(string name)
    {
        AssertName(name);
        string path = Path.Combine(StoreFolder(), name + ".cred");
        if (!File.Exists(path)) return null;
        try
        {
            byte[] protectedBytes = Convert.FromBase64String(File.ReadAllText(path));
            return Encoding.UTF8.GetString(Dpapi.Unprotect(protectedBytes));
        }
        catch (Exception e) when (e is CryptographicException or IOException or UnauthorizedAccessException or FormatException or ArgumentException)
        {
            return null;
        }
    }

    // Deleting a credential that is already gone is success.
    public static void Delete(string name)
    {
        AssertName(name);
        string path = Path.Combine(StoreFolder(), name + ".cred");
        if (!File.Exists(path)) return;
        try { File.Delete(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    // DPAPI (CryptProtectData), as ProtectedData does it: no entropy, no
    // prompt, current user.
    static partial class Dpapi
    {
        [StructLayout(LayoutKind.Sequential)]
        struct DataBlob
        {
            public int Size;
            public IntPtr Data;
        }

        const int UiForbidden = 0x1;

        [LibraryImport("crypt32.dll", EntryPoint = "CryptProtectData", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool CryptProtectData(ref DataBlob dataIn, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref DataBlob dataOut);

        [LibraryImport("crypt32.dll", EntryPoint = "CryptUnprotectData", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref DataBlob dataOut);

        [LibraryImport("kernel32.dll", EntryPoint = "LocalFree")]
        private static partial IntPtr LocalFree(IntPtr memory);

        public static byte[] Protect(byte[] plain) => Transform(plain, protect: true);
        public static byte[] Unprotect(byte[] encrypted) => Transform(encrypted, protect: false);

        static byte[] Transform(byte[] input, bool protect)
        {
            IntPtr buffer = Marshal.AllocHGlobal(Math.Max(input.Length, 1));
            var dataOut = new DataBlob();
            try
            {
                Marshal.Copy(input, 0, buffer, input.Length);
                var dataIn = new DataBlob { Size = input.Length, Data = buffer };
                bool done = protect
                    ? CryptProtectData(ref dataIn, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref dataOut)
                    : CryptUnprotectData(ref dataIn, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref dataOut);
                if (!done) throw new CryptographicException(Marshal.GetLastPInvokeError());
                var result = new byte[dataOut.Size];
                Marshal.Copy(dataOut.Data, result, 0, dataOut.Size);
                return result;
            }
            finally
            {
                for (int i = 0; i < input.Length; i++) Marshal.WriteByte(buffer, i, 0);
                Marshal.FreeHGlobal(buffer);
                if (dataOut.Data != IntPtr.Zero)
                {
                    for (int i = 0; i < dataOut.Size; i++) Marshal.WriteByte(dataOut.Data, i, 0);
                    LocalFree(dataOut.Data);
                }
            }
        }
    }
}

// The credential vault (D111): a port of otterPS's OtterCredentialApi, the
// Windows Credential Manager through CredWriteW / CredReadW / CredDeleteW.
// Each secret is a generic credential named "otter:<application>:<name>";
// its comment says whether the secret is text or bytes.
[SupportedOSPlatform("windows")]
public static unsafe partial class OtterVault
{
    public const int MaxBlob = 2560;   // CRED_MAX_CREDENTIAL_BLOB_SIZE

    [StructLayout(LayoutKind.Sequential)]
    struct Credential
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public uint LastWrittenLow;
        public uint LastWrittenHigh;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    const uint GenericType = 1;          // CRED_TYPE_GENERIC
    const uint PersistLocalMachine = 2;  // CRED_PERSIST_LOCAL_MACHINE
    const int ErrorNotFound = 1168;

    [LibraryImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredWrite(ref Credential credential, uint flags);

    [LibraryImport("advapi32.dll", EntryPoint = "CredReadW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [LibraryImport("advapi32.dll", EntryPoint = "CredDeleteW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredDelete(string target, uint type, uint flags);

    [LibraryImport("advapi32.dll", EntryPoint = "CredFree")]
    private static partial void CredFree(IntPtr buffer);

    public static void Write(string target, string kind, byte[] blob)
    {
        IntPtr pinned = Marshal.AllocHGlobal(Math.Max(blob.Length, 1));
        IntPtr targetName = Marshal.StringToHGlobalUni(target);
        IntPtr comment = Marshal.StringToHGlobalUni(kind);
        IntPtr userName = Marshal.StringToHGlobalUni("otter");
        try
        {
            Marshal.Copy(blob, 0, pinned, blob.Length);
            var credential = new Credential
            {
                Type = GenericType,
                TargetName = targetName,
                Comment = comment,
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = pinned,
                Persist = PersistLocalMachine,
                UserName = userName,
            };
            if (!CredWrite(ref credential, 0)) throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        finally
        {
            // Do not leave secret bytes lying in unmanaged memory.
            for (int i = 0; i < blob.Length; i++) Marshal.WriteByte(pinned, i, 0);
            Marshal.FreeHGlobal(pinned);
            Marshal.FreeHGlobal(targetName);
            Marshal.FreeHGlobal(comment);
            Marshal.FreeHGlobal(userName);
        }
    }

    // false when there is no such secret.
    public static bool Read(string target, out string? kind, out byte[]? blob)
    {
        kind = null;
        blob = null;
        if (!CredRead(target, GenericType, 0, out IntPtr pointer))
        {
            int error = Marshal.GetLastPInvokeError();
            if (error == ErrorNotFound) return false;
            throw new Win32Exception(error);
        }
        try
        {
            Credential credential = *(Credential*)pointer;
            kind = credential.Comment == IntPtr.Zero ? null : Marshal.PtrToStringUni(credential.Comment);
            blob = new byte[credential.CredentialBlobSize];
            if (credential.CredentialBlobSize > 0) Marshal.Copy(credential.CredentialBlob, blob, 0, (int)credential.CredentialBlobSize);
            return true;
        }
        finally
        {
            CredFree(pointer);
        }
    }

    // false when there is no such secret.
    public static bool Delete(string target)
    {
        if (CredDelete(target, GenericType, 0)) return true;
        int error = Marshal.GetLastPInvokeError();
        if (error == ErrorNotFound) return false;
        throw new Win32Exception(error);
    }
}
