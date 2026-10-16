using System.Globalization;
using System.Text;

namespace Otter.Runtime;

// Files and folders (rules.md section 30, D20, D21, D61, D72): a port of
// otterPS's src/Otter.Library.psm1, with the same messages and the same side
// effects. otterPS does this with Windows PowerShell 5.1 cmdlets on .NET
// Framework, so where those behave differently from plain .NET 10 calls -
// which names count as a path, what Get-ChildItem lists and in what order,
// what Copy-Item -Force and Move-Item -Force overwrite - the PowerShell
// behaviour is reproduced here.
public sealed class FileProblem(string message, string? suggestion = null) : Exception(message)
{
    public string? Suggestion { get; } = suggestion;
}

public static class OtterFiles
{
    static readonly UTF8Encoding Utf8NoBom = new(false);

    // ---- paths -----------------------------------------------------------

    // Resolve-OtterPath: relative to wherever otter was run from. A rooted
    // path is used exactly as written.
    public static string Resolve(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new FileProblem("I need the name of a file.");
        if (!FrameworkAccepts(path, out bool rooted)) throw new FileProblem($"\"{path}\" is not a name Otter can use for a file.");
        if (rooted) return path;
        string full = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, path));
        // .NET Framework's MAX_PATH limits (PathTooLongException).
        if (full.Length >= 260 || (Path.GetDirectoryName(full)?.Length ?? 0) >= 248)
        {
            throw new FileProblem($"\"{path}\" is not a name Otter can use for a file.");
        }
        return full;
    }

    // What .NET Framework's Path.IsPathRooted, Path.Combine and
    // Path.GetFullPath refuse: control characters and "<>| anywhere; * and ?
    // and a colon after the drive in a path that is not rooted.
    static bool FrameworkAccepts(string path, out bool rooted)
    {
        rooted = false;
        foreach (char c in path)
        {
            if (c < 32 || c is '"' or '<' or '>' or '|') return false;
        }
        rooted = path[0] is '\\' or '/' || (path.Length >= 2 && path[1] == ':');
        if (rooted) return true;
        return path.IndexOfAny(new[] { '*', '?', ':' }) < 0;
    }

    // Initialize-OtterParentFolder: copy "a.txt" to "backup/a.txt" makes backup.
    static void EnsureParentFolder(string fullPath)
    {
        string? folder = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(folder) || File.Exists(folder) || Directory.Exists(folder)) return;
        try { Directory.CreateDirectory(folder); }
        catch (Exception e) when (IsFileSystemFailure(e)) { throw new FileProblem($"I could not make the folder \"{folder}\"."); }
    }

    // A .NET method failing inside PowerShell reaches otterPS's message
    // wrapped as PowerShell wraps it (MethodInvocationException). Cmdlets
    // (Copy-Item, Move-Item, Remove-Item, New-Item) report the bare message.
    static string Calling(string method, int arguments, Exception e) =>
        $"Exception calling \"{method}\" with \"{arguments}\" argument(s): \"{e.Message}\"";

    static bool IsFileSystemFailure(Exception e) =>
        e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException;

    // Resolve-OtterFileArgument: a path the programmer typed, or a file or
    // folder object from `get files`.
    public static string PathOf(object? value)
    {
        if (value is OtterThing thing)
        {
            if (thing.HasProperty("path")) return AsText(thing.ReadProperty("path"));
            if (thing.HasProperty("name")) return AsText(thing.ReadProperty("name"));
            throw new FileProblem($"I need a file here, but this {thing.TypeName} has no path.");
        }
        return Values.Format(value);
    }

    // PowerShell's [string] cast.
    static string AsText(object? value) => value switch
    {
        null => "",
        string s => s,
        bool b => b ? "True" : "False",
        double d => OtterJson.RoundTrip(d),
        _ => Values.Format(value),
    };

    // ---- file and folder objects -----------------------------------------

    // New-OtterFileObject: name, extension, path, size, created, modified.
    public static OtterThing FileObject(string path)
    {
        string full = Resolve(path);
        var file = new OtterThing("file");
        file.WriteProperty("name", Path.GetFileName(full));
        file.WriteProperty("extension", Path.GetExtension(full));
        file.WriteProperty("path", full);
        double size = 0;
        string? created = null, modified = null;
        if (File.Exists(full))
        {
            var info = new FileInfo(full);
            size = info.Length;
            created = Stamp(info.CreationTime);
            modified = Stamp(info.LastWriteTime);
        }
        file.WriteProperty("size", size);
        file.WriteProperty("created", created);
        file.WriteProperty("modified", modified);
        return file;
    }

    // New-OtterFolderObject: no size - measuring a folder means walking it.
    public static OtterThing FolderObject(string path)
    {
        string full = Resolve(path);
        var folder = new OtterThing("folder");
        folder.WriteProperty("name", Path.GetFileName(full));
        folder.WriteProperty("path", full);
        if (Directory.Exists(full))
        {
            var info = new DirectoryInfo(full);
            folder.WriteProperty("created", Stamp(info.CreationTime));
            folder.WriteProperty("modified", Stamp(info.LastWriteTime));
        }
        else
        {
            folder.WriteProperty("created", null);
            folder.WriteProperty("modified", null);
        }
        return folder;
    }

    static string Stamp(DateTime time) => time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    // ---- files -----------------------------------------------------------

    // read "notes.txt" into notes
    public static string Read(string path)
    {
        string full = Resolve(path);
        if (!File.Exists(full)) throw new FileProblem($"I could not find a file called \"{path}\".", $"if file \"{path}\" exists");
        try { return File.ReadAllText(full, Utf8NoBom); }
        catch (Exception e) when (IsFileSystemFailure(e)) { throw new FileProblem($"I could not read \"{path}\". {Calling("ReadAllText", 2, e)}"); }
    }

    // write "Hello!" to "hello.txt" [atomically] - replaces what was there,
    // as UTF-8 with no byte order mark.
    public static void Write(string path, string content, bool atomic)
    {
        string full = Resolve(path);
        EnsureParentFolder(full);
        if (Directory.Exists(full)) throw new FileProblem($"\"{path}\" is a folder, not a file.");
        if (!atomic)
        {
            try { File.WriteAllText(full, content, Utf8NoBom); }
            catch (Exception e) when (IsFileSystemFailure(e)) { throw new FileProblem($"I could not write to \"{path}\". {Calling("WriteAllText", 3, e)}"); }
            return;
        }
        // D72: a temporary file in the same folder, renamed into place.
        if (File.Exists(full) && new FileInfo(full).IsReadOnly) throw new FileProblem($"I could not write to \"{path}\". The file is read-only.");
        string temporary = full + ".otter-tmp-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        string backup = full + ".otter-bak-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        // Which .NET call failed is part of PowerShell's message.
        (string Name, int Arguments) step = ("WriteAllText", 3);
        try
        {
            File.WriteAllText(temporary, content, Utf8NoBom);
            if (File.Exists(full))
            {
                step = ("Replace", 3);
                File.Replace(temporary, full, backup);
                try { File.Delete(backup); } catch (Exception e) when (IsFileSystemFailure(e)) { }
            }
            else
            {
                step = ("Move", 2);
                File.Move(temporary, full);
            }
        }
        catch (Exception e) when (IsFileSystemFailure(e))
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch (Exception cleanup) when (IsFileSystemFailure(cleanup)) { }
            throw new FileProblem($"I could not write to \"{path}\". {Calling(step.Name, step.Arguments, e)}");
        }
    }

    // bytes from file "photo.png" (D115)
    public static byte[] ReadBytes(string path)
    {
        string full = Resolve(path);
        if (Directory.Exists(full)) throw new FileProblem($"\"{path}\" is a folder, not a file.");
        if (!File.Exists(full)) throw new FileProblem($"I could not find a file called \"{path}\".", $"if file \"{path}\" exists");
        try { return File.ReadAllBytes(full); }
        catch (Exception e) when (IsFileSystemFailure(e)) { throw new FileProblem($"I could not read bytes from \"{path}\". {Calling("ReadAllBytes", 1, e)}"); }
    }

    // write bytes data to file "copy.png" [atomically] (D115)
    public static void WriteBytes(string path, byte[] data, bool atomic)
    {
        string full = Resolve(path);
        EnsureParentFolder(full);
        if (Directory.Exists(full)) throw new FileProblem($"\"{path}\" is a folder, not a file.");
        if (!atomic)
        {
            try { File.WriteAllBytes(full, data); }
            catch (Exception e) when (IsFileSystemFailure(e)) { throw new FileProblem($"I could not write bytes to \"{path}\". {Calling("WriteAllBytes", 2, e)}"); }
            return;
        }
        if (File.Exists(full) && new FileInfo(full).IsReadOnly) throw new FileProblem($"I could not write bytes to \"{path}\". The file is read-only.");
        string temporary = full + ".otter-tmp-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        string backup = full + ".otter-bak-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        (string Name, int Arguments) step = ("WriteAllBytes", 2);
        try
        {
            File.WriteAllBytes(temporary, data);
            if (File.Exists(full))
            {
                step = ("Replace", 3);
                File.Replace(temporary, full, backup);
                try { File.Delete(backup); } catch (Exception e) when (IsFileSystemFailure(e)) { }
            }
            else
            {
                step = ("Move", 2);
                File.Move(temporary, full);
            }
        }
        catch (Exception e) when (IsFileSystemFailure(e))
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch (Exception cleanup) when (IsFileSystemFailure(cleanup)) { }
            throw new FileProblem($"I could not write bytes to \"{path}\". {Calling(step.Name, step.Arguments, e)}");
        }
    }

    // append "line one" to "log.txt" (D61) - creates the file if need be.
    public static void Append(string path, string content)
    {
        string full = Resolve(path);
        EnsureParentFolder(full);
        if (Directory.Exists(full)) throw new FileProblem($"\"{path}\" is a folder, not a file.");
        try { File.AppendAllText(full, content, Utf8NoBom); }
        catch (Exception e) when (IsFileSystemFailure(e)) { throw new FileProblem($"I could not append to \"{path}\". {Calling("AppendAllText", 3, e)}"); }
    }

    // copy "hello.txt" to "backup/hello.txt"; copy into a folder keeps the name.
    public static void CopyFile(string source, string destination)
    {
        string from = Resolve(source);
        string to = Resolve(destination);
        if (!File.Exists(from)) throw new FileProblem($"I could not find a file called \"{source}\" to copy.");
        if (Directory.Exists(to)) to = Path.Combine(to, Path.GetFileName(from));
        else EnsureParentFolder(to);
        try
        {
            // Copy-Item -Force: into a folder that is there by now, never onto
            // itself, and over a read-only file.
            if (Directory.Exists(to)) to = Path.Combine(to, Path.GetFileName(from));
            if (SamePath(from, to)) throw new IOException($"Cannot overwrite the item {from} with itself.");
            ClearReadOnly(to);
            File.Copy(from, to, overwrite: true);
        }
        catch (Exception e) when (IsFileSystemFailure(e)) { throw new FileProblem($"I could not copy \"{source}\". {e.Message}"); }
    }

    // move "hello.txt" to "Documents"
    public static void MoveFile(string source, string destination)
    {
        string from = Resolve(source);
        string to = Resolve(destination);
        if (!File.Exists(from)) throw new FileProblem($"I could not find a file called \"{source}\" to move.");
        if (Directory.Exists(to)) to = Path.Combine(to, Path.GetFileName(from));
        else EnsureParentFolder(to);
        try
        {
            // Move-Item -Force: replaces a file already there, read-only or not.
            if (Directory.Exists(to)) to = Path.Combine(to, Path.GetFileName(from));
            if (SamePath(from, to))
            {
                if (!string.Equals(Path.GetFullPath(from), Path.GetFullPath(to), StringComparison.Ordinal)) File.Move(from, to);
                return;
            }
            if (File.Exists(to))
            {
                ClearReadOnly(to);
                File.Delete(to);
            }
            File.Move(from, to);
        }
        catch (Exception e) when (IsFileSystemFailure(e)) { throw new FileProblem($"I could not move \"{source}\". {e.Message}"); }
    }

    // delete file "hello.txt" - files only; a folder is never erased by this.
    public static void DeleteFile(string path)
    {
        string full = Resolve(path);
        if (Directory.Exists(full)) throw new FileProblem($"\"{path}\" is a folder. Otter only deletes files.");
        if (!File.Exists(full)) throw new FileProblem($"I could not find a file called \"{path}\" to delete.", $"if file \"{path}\" exists");
        try
        {
            ClearReadOnly(full);
            File.Delete(full);
        }
        catch (Exception e) when (IsFileSystemFailure(e)) { throw new FileProblem($"I could not delete \"{path}\". {e.Message}"); }
    }

    // if file "hello.txt" exists
    public static bool Exists(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        return File.Exists(Resolve(path));
    }

    // if file "x" is locked (D72): held open elsewhere right now.
    public static bool IsLocked(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        string full = Resolve(path);
        if (!File.Exists(full)) return false;
        try
        {
            using var stream = File.Open(full, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException) { return true; }
        catch (Exception e) when (IsFileSystemFailure(e)) { throw new FileProblem($"I could not check whether \"{path}\" is locked. {Calling("Open", 4, e)}"); }
    }

    static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    static void ClearReadOnly(string path)
    {
        if (!File.Exists(path)) return;
        var info = new FileInfo(path);
        if (info.IsReadOnly) info.IsReadOnly = false;
    }

    // ---- folders and discovery (D20, D21) ----------------------------------

    static string RequireFolder(string path)
    {
        string full = Resolve(path);
        if (!Directory.Exists(full)) throw new FileProblem($"I could not find a folder called \"{path}\".");
        return full;
    }

    // get files in "Pictures" [and subfolders] into files (also folders):
    // what Get-ChildItem -File / -Directory [-Recurse] lists - hidden items
    // left out and not searched, the file system's own order, and each
    // folder's own entries before anything inside its subfolders.
    public static OtterList Entries(string path, bool folders, bool includeSubfolders)
    {
        string full = RequireFolder(path);
        var found = new OtterList();
        Walk(new DirectoryInfo(full), folders, includeSubfolders, found);
        return found;
    }

    static void Walk(DirectoryInfo folder, bool folders, bool recurse, OtterList found)
    {
        List<FileSystemInfo> visible;
        try
        {
            visible = folder.EnumerateFileSystemInfos()
                .Where(entry => (entry.Attributes & FileAttributes.Hidden) == 0)
                .ToList();
            // NTFS lists names in upper-cased ordinal order; other file systems
            // use their own (ext4 hashes names), so the same program would list
            // its files differently. Elsewhere, sort the way Windows lists them.
            if (!OperatingSystem.IsWindows())
            {
                visible = visible.OrderBy(entry => entry.Name.ToUpperInvariant(), StringComparer.Ordinal).ToList();
            }
        }
        catch (Exception e) when (IsFileSystemFailure(e)) { return; }
        foreach (FileSystemInfo entry in visible)
        {
            if (folders && entry is DirectoryInfo) found.Add(FolderObject(entry.FullName));
            if (!folders && entry is FileInfo) found.Add(FileObject(entry.FullName));
        }
        if (!recurse) return;
        foreach (DirectoryInfo sub in visible.OfType<DirectoryInfo>()) Walk(sub, folders, recurse, found);
    }

    // create folder "Backup" - nothing to do if it is already there.
    public static void CreateFolder(string path)
    {
        string full = Resolve(path);
        if (Directory.Exists(full)) return;
        // New-Item -ItemType Directory -Force quietly leaves a file of that
        // name alone.
        if (File.Exists(full)) return;
        try { Directory.CreateDirectory(full); }
        catch (Exception e) when (IsFileSystemFailure(e)) { throw new FileProblem($"I could not make the folder \"{path}\". {e.Message}"); }
    }

    // delete folder "Backup" - only an empty one.
    public static void DeleteFolder(string path)
    {
        string full = Resolve(path);
        if (File.Exists(full)) throw new FileProblem($"\"{path}\" is a file, not a folder.", $"delete file \"{path}\"");
        if (!Directory.Exists(full)) throw new FileProblem($"I could not find a folder called \"{path}\" to delete.");
        int count;
        try { count = Directory.EnumerateFileSystemEntries(full).Count(); }
        catch (Exception e) when (IsFileSystemFailure(e)) { count = 0; }
        if (count > 0) throw new FileProblem($"The folder \"{path}\" still has {count} things in it. Otter only deletes empty folders.");
        try
        {
            var info = new DirectoryInfo(full);
            if ((info.Attributes & FileAttributes.ReadOnly) != 0) info.Attributes &= ~FileAttributes.ReadOnly;
            info.Delete();
        }
        catch (Exception e) when (IsFileSystemFailure(e)) { throw new FileProblem($"I could not delete the folder \"{path}\". {e.Message}"); }
    }

    // copy folder "Photos" to "Backup": into Backup if it is there.
    public static void CopyFolder(string source, string destination)
    {
        string from = RequireFolder(source);
        string to = Resolve(destination);
        if (Directory.Exists(to)) to = Path.Combine(to, Path.GetFileName(from));
        try
        {
            // Copy-Item -Recurse -Force: a folder that exists by now gets the
            // copy inside it; missing parents are made; hidden items come too.
            if (Directory.Exists(to)) to = Path.Combine(to, Path.GetFileName(from));
            CopyTree(new DirectoryInfo(from), to);
        }
        catch (Exception e) when (IsFileSystemFailure(e)) { throw new FileProblem($"I could not copy the folder \"{source}\". {e.Message}"); }
    }

    static void CopyTree(DirectoryInfo from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (FileSystemInfo entry in from.EnumerateFileSystemInfos())
        {
            string target = Path.Combine(to, entry.Name);
            if (entry is DirectoryInfo sub) CopyTree(sub, target);
            else
            {
                ClearReadOnly(target);
                ((FileInfo)entry).CopyTo(target, overwrite: true);
            }
        }
    }

    // zip folder "src" into "archive.zip" (D87) - never over an existing file.
    // The archive holds the same entries as otterPS's; its compressed bytes
    // can differ, because .NET Framework and .NET 10 ship different zlib builds.
    public static void ZipFolder(string source, string archivePath)
    {
        string folder = RequireFolder(source);
        string archive = Resolve(archivePath);
        EnsureParentFolder(archive);
        if (File.Exists(archive) || Directory.Exists(archive))
        {
            throw new FileProblem($"\"{archivePath}\" already exists - I will not overwrite an existing archive.", $"delete \"{archivePath}\" first if you want to replace it");
        }
        try { System.IO.Compression.ZipFile.CreateFromDirectory(folder, archive); }
        catch (Exception e) when (IsFileSystemFailure(e) || e is System.IO.InvalidDataException)
        {
            throw new FileProblem($"I could not zip \"{source}\" to \"{archivePath}\". {Calling("CreateFromDirectory", 2, e)}");
        }
    }

    // unzip "archive.zip" into "dest" (D87) - makes the folder if need be.
    public static void Unzip(string archivePath, string destination)
    {
        string archive = Resolve(archivePath);
        if (!File.Exists(archive)) throw new FileProblem($"I could not find an archive called \"{archivePath}\".");
        string folder = Resolve(destination);
        try { System.IO.Compression.ZipFile.ExtractToDirectory(archive, folder); }
        catch (Exception e) when (IsFileSystemFailure(e) || e is System.IO.InvalidDataException)
        {
            throw new FileProblem($"I could not unzip \"{archivePath}\" to \"{destination}\". {Calling("ExtractToDirectory", 2, e)}");
        }
    }

    // move folder "Old" to "Archive": into Archive if it is there.
    public static void MoveFolder(string source, string destination)
    {
        string from = RequireFolder(source);
        string to = Resolve(destination);
        if (Directory.Exists(to)) to = Path.Combine(to, Path.GetFileName(from));
        try
        {
            if (Directory.Exists(to)) to = Path.Combine(to, Path.GetFileName(from));
            // Move-Item reports a missing parent folder without naming it.
            string? parent = Path.GetDirectoryName(Path.GetFullPath(to));
            if (parent != null && !Directory.Exists(parent)) throw new IOException("Could not find a part of the path.");
            Directory.Move(from, to);
        }
        catch (Exception e) when (IsFileSystemFailure(e)) { throw new FileProblem($"I could not move the folder \"{source}\". {e.Message}"); }
    }
}
