using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Otter.Runtime;

public sealed class RegistryProblem(string message, string? suggestion = null) : Exception(message)
{
    public string? Suggestion { get; } = suggestion;
}

// The Windows registry (D78): a port of otterPS's Get-OtterRegistryValue,
// Set-OtterRegistryValue, Remove-OtterRegistryValue and
// Test-OtterRegistryKeyExists, which work through PowerShell's registry
// provider. A key path is a provider path: "HKCU:\Software\MyApp". Windows
// PowerShell has only the HKCU: and HKLM: drives, so the other three
// prefixes otterPS accepts (HKCR:, HKU:, HKCC:) name a drive that does not
// exist, exactly as they do there.
[SupportedOSPlatform("windows")]
public static class OtterRegistry
{
    static readonly Regex KeyPathPattern = new("^(HKCU|HKLM|HKCR|HKU|HKCC):", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static void AssertKeyPath(string keyPath)
    {
        if (!KeyPathPattern.IsMatch(keyPath))
        {
            throw new RegistryProblem($"\"{keyPath}\" does not look like a registry key path.",
                "Registry paths start with HKCU:, HKLM:, HKCR:, HKU:, or HKCC: - for example \"HKCU:\\Software\\MyApp\".");
        }
    }

    // The hive and the path below it, or no hive when the drive does not exist.
    static (RegistryKey? Hive, string Drive, string SubKey) Split(string keyPath)
    {
        int colon = keyPath.IndexOf(':');
        string drive = keyPath.Substring(0, colon);
        string subKey = keyPath.Substring(colon + 1).Replace('/', '\\').Trim('\\');
        while (subKey.Contains(@"\\", StringComparison.Ordinal)) subKey = subKey.Replace(@"\\", @"\", StringComparison.Ordinal);
        RegistryKey? hive = drive.ToUpperInvariant() switch
        {
            "HKCU" => Registry.CurrentUser,
            "HKLM" => Registry.LocalMachine,
            _ => null,
        };
        return (hive, drive, subKey);
    }

    static RegistryKey? Open(string keyPath, bool writable = false)
    {
        var (hive, _, subKey) = Split(keyPath);
        if (hive == null) return null;
        try { return hive.OpenSubKey(subKey, writable); }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException or ArgumentException) { return null; }
    }

    // Test-Path on the key.
    public static bool KeyExists(string keyPath)
    {
        if (!KeyPathPattern.IsMatch(keyPath)) return false;
        using RegistryKey? key = Open(keyPath);
        return key != null;
    }

    // gone when the key or the value does not exist.
    public static object? GetValue(string valueName, string keyPath)
    {
        AssertKeyPath(keyPath);
        using RegistryKey? key = Open(keyPath);
        if (key == null) return null;
        try
        {
            string name = string.Equals(valueName, "(default)", StringComparison.OrdinalIgnoreCase) ? "" : valueName;
            if (!key.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase)) return null;
            return ToOtter(key.GetValue(name));
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    // What a registry value becomes in Otter: numbers are numbers, several
    // texts are a list.
    public static object? ToOtter(object? raw) => raw switch
    {
        null => null,
        string text => text,
        int number => (double)number,
        long number => (double)number,
        string[] texts => new OtterList(texts.Select(t => (object?)t)),
        byte[] bytes => new OtterList(bytes.Select(b => (object?)(double)b)),
        _ => raw.ToString(),
    };

    // Creates the key when it does not exist; the value is always text.
    public static void SetValue(string valueName, string value, string keyPath)
    {
        AssertKeyPath(keyPath);
        try
        {
            var (hive, drive, subKey) = Split(keyPath);
            if (hive == null) throw new RegistryProblem($"Cannot find drive. A drive with the name '{drive}' does not exist.");
            using RegistryKey key = hive.CreateSubKey(subKey, writable: true);
            string name = string.Equals(valueName, "(default)", StringComparison.OrdinalIgnoreCase) ? "" : valueName;
            key.SetValue(name, value, RegistryValueKind.String);
        }
        catch (Exception e) when (e is RegistryProblem or System.Security.SecurityException or UnauthorizedAccessException or IOException or ArgumentException)
        {
            string reason = e switch
            {
                RegistryProblem problem => problem.Message,
                System.Security.SecurityException or UnauthorizedAccessException => "Requested registry access is not allowed.",
                _ => e.Message,
            };
            throw new RegistryProblem($"I could not set the registry value \"{valueName}\" in \"{keyPath}\". {reason}");
        }
    }

    // A value that is already gone is success.
    public static void DeleteValue(string valueName, string keyPath)
    {
        AssertKeyPath(keyPath);
        using RegistryKey? key = Open(keyPath, writable: true);
        if (key == null) return;
        try
        {
            string name = string.Equals(valueName, "(default)", StringComparison.OrdinalIgnoreCase) ? "" : valueName;
            key.DeleteValue(name, throwOnMissingValue: false);
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException or ArgumentException)
        {
            // already gone, or never there - nothing left to do
        }
    }
}
