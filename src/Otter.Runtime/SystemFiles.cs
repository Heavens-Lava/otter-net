using System.Security.AccessControl;
using System.Security.Principal;

namespace Otter.Runtime;

// Symbolic links (D73), owners and read-only files (D74): a port of otterPS's
// New-OtterSymbolicLink, Get-OtterSymbolicLinkTarget, Test-OtterSymbolicLink,
// Get-OtterFileOwner, Test-OtterFileReadOnly and Set-OtterFileReadOnly. They
// report problems as FileProblem, like the rest of the file library.
public static class OtterSystemFiles
{
    // Test-Path: the item is there (a link counts even when what it points
    // at is gone).
    static bool ItemExists(string full) =>
        File.Exists(full) || Directory.Exists(full) || new FileInfo(full).LinkTarget != null;

    static FileSystemInfo Item(string full) =>
        Directory.Exists(full) ? new DirectoryInfo(full) : new FileInfo(full);

    // create symbolic link "l" pointing to "t": a file or folder link,
    // whichever the target is; an existing file or link there is replaced.
    public static void CreateSymbolicLink(string linkPath, string targetPath)
    {
        string fullTarget = OtterFiles.Resolve(targetPath);
        if (!File.Exists(fullTarget) && !Directory.Exists(fullTarget))
        {
            throw new FileProblem($"I could not find \"{targetPath}\" to point the link at.");
        }
        string fullLink = OtterFiles.Resolve(linkPath);
        OtterFiles.EnsureParentFolder(fullLink);
        try
        {
            if (new FileInfo(fullLink).LinkTarget != null)
            {
                // Only the link goes, never what it points at.
                if (OperatingSystem.IsWindows() && Directory.Exists(fullLink)) Directory.Delete(fullLink);
                else File.Delete(fullLink);
            }
            else if (File.Exists(fullLink))
            {
                File.Delete(fullLink);
            }
            else if (Directory.Exists(fullLink))
            {
                throw new IOException($"An item with the specified name {fullLink} already exists.");
            }
            if (Directory.Exists(fullTarget)) Directory.CreateSymbolicLink(fullLink, fullTarget);
            else File.CreateSymbolicLink(fullLink, fullTarget);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            string message = e.Message;
            if (message.Contains("privilege", StringComparison.OrdinalIgnoreCase))
            {
                throw new FileProblem($"I could not create the symbolic link \"{linkPath}\" - this needs Administrator privileges or Developer Mode turned on.",
                    "Run as Administrator, or turn on Developer Mode in Windows Settings.");
            }
            throw new FileProblem($"I could not create the symbolic link \"{linkPath}\". {message}");
        }
    }

    // get symbolic link target of "l" into t: the path the link holds.
    public static string GetSymbolicLinkTarget(string linkPath)
    {
        string full = OtterFiles.Resolve(linkPath);
        if (!ItemExists(full)) throw new FileProblem($"I could not find a link called \"{linkPath}\".");
        string? target = new FileInfo(full).LinkTarget;
        if (target == null) throw new FileProblem($"\"{linkPath}\" is not a symbolic link.");
        return target;
    }

    // if file "l" is a symbolic link
    public static bool IsSymbolicLink(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        string full = OtterFiles.Resolve(path);
        if (!ItemExists(full)) return false;
        return new FileInfo(full).LinkTarget != null;
    }

    // get owner of "x" into owner (Windows only; a file or a folder).
    public static string GetOwner(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        string full = OtterFiles.Resolve(path);
        if (!ItemExists(full)) throw new FileProblem($"I could not find \"{path}\" to check its owner.");
        try
        {
            FileSystemSecurity security = Directory.Exists(full)
                ? new DirectoryInfo(full).GetAccessControl(AccessControlSections.Owner)
                : new FileInfo(full).GetAccessControl(AccessControlSections.Owner);
            IdentityReference? owner = security.GetOwner(typeof(SecurityIdentifier));
            if (owner == null) return "";
            try { return owner.Translate(typeof(NTAccount)).Value; }
            catch (IdentityNotMappedException) { return owner.Value; }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SystemException)
        {
            throw new FileProblem($"I could not read the owner of \"{path}\". {e.Message}");
        }
    }

    // if file "x" is read only - files only: the read-only flag on a folder
    // does not mean what it means on a file.
    public static bool IsReadOnly(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        string full = OtterFiles.Resolve(path);
        if (!File.Exists(full)) return false;
        return (Item(full).Attributes & FileAttributes.ReadOnly) != 0;
    }

    // set file "x" to read only / to writable
    public static void SetReadOnly(string path, bool readOnly)
    {
        string full = OtterFiles.Resolve(path);
        if (!File.Exists(full)) throw new FileProblem($"I could not find a file called \"{path}\" to change.");
        try
        {
            FileAttributes current = File.GetAttributes(full);
            FileAttributes updated = readOnly ? current | FileAttributes.ReadOnly : current & ~FileAttributes.ReadOnly;
            File.SetAttributes(full, updated);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new FileProblem($"I could not change \"{path}\"'s read-only setting. Exception calling \"SetAttributes\" with \"2\" argument(s): \"{e.Message}\"");
        }
    }
}
