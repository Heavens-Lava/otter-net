namespace Otter.Language;

// Syntax nodes for operating system integration (see docs/PORTING.md and
// docs/port/system.md). Named as otterPS's contract names them.

// ---- programs and remote commands (D70, D84, D85) ---------------------------

// run "notepad.exe" [into p] - starts a program and carries straight on
public sealed class RunProgramStmt(Expr target, string? resultTarget, int line) : Stmt(line)
{
    public Expr Target { get; } = target;
    public string? ResultTarget { get; } = resultTarget;
}

// run command "..." over ssh to "user@host" [into result]
public sealed class RunSshCommandStmt(Expr command, Expr hostName, string? resultTarget, int line) : Stmt(line)
{
    public Expr Command { get; } = command;
    public Expr HostName { get; } = hostName;
    public string? ResultTarget { get; } = resultTarget;
}

// run command "..." on remote "host" using credential "n" [into result]
public sealed class RunRemoteCommandStmt(Expr command, Expr hostName, Expr credentialName, string? resultTarget, int line) : Stmt(line)
{
    public Expr Command { get; } = command;
    public Expr HostName { get; } = hostName;
    public Expr CredentialName { get; } = credentialName;
    public string? ResultTarget { get; } = resultTarget;
}

// ---- system information and processes (D69, D70, D71, D75-D77, D80, D83, D86)

// get system information "os" into info
public sealed class GetSystemInfoStmt(Expr infoKind, string target, int line) : Stmt(line)
{
    public Expr InfoKind { get; } = infoKind;
    public string Target { get; } = target;
}

// get processes into list
public sealed class GetProcessesStmt(string target, int line) : Stmt(line) { public string Target { get; } = target; }

// kill process p [and its children]
public sealed class KillProcessStmt(Expr process, bool includeChildren, int line) : Stmt(line)
{
    public Expr Process { get; } = process;
    public bool IncludeChildren { get; } = includeChildren;
}

// set priority of process p to "high"
public sealed class SetProcessPriorityStmt(Expr process, Expr priority, int line) : Stmt(line)
{
    public Expr Process { get; } = process;
    public Expr Priority { get; } = priority;
}

// wait for process p up to 5 seconds [into finished]
public sealed class WaitForProcessStmt(Expr process, Expr timeoutSeconds, string? target, int line) : Stmt(line)
{
    public Expr Process { get; } = process;
    public Expr TimeoutSeconds { get; } = timeoutSeconds;
    public string? Target { get; } = target;
}

// ---- clipboard, notifications, dialogs (D67) --------------------------------

// copy "text" to clipboard
public sealed class CopyToClipboardStmt(Expr text, int line) : Stmt(line) { public Expr Text { get; } = text; }

// get clipboard into text
public sealed class GetClipboardStmt(string target, int line) : Stmt(line) { public string Target { get; } = target; }

// notify "Title" with "Message"
public sealed class NotifyStmt(Expr title, Expr message, int line) : Stmt(line)
{
    public Expr Title { get; } = title;
    public Expr Message { get; } = message;
}

public enum FileDialogKind { OpenFile, Folder, SaveFile }

// choose file into path / choose folder into path / choose file to save into path
public sealed class ChooseFileStmt(FileDialogKind dialog, string target, int line) : Stmt(line)
{
    public FileDialogKind Dialog { get; } = dialog;
    public string Target { get; } = target;
}

// ---- links, owners, read-only files (D73, D74) ------------------------------

// create symbolic link "l" pointing to "t"
public sealed class CreateSymbolicLinkStmt(Expr linkPath, Expr targetPath, int line) : Stmt(line)
{
    public Expr LinkPath { get; } = linkPath;
    public Expr TargetPath { get; } = targetPath;
}

// get symbolic link target of "l" into t
public sealed class GetSymbolicLinkTargetStmt(Expr linkPath, string target, int line) : Stmt(line)
{
    public Expr LinkPath { get; } = linkPath;
    public string Target { get; } = target;
}

// file "l" is a symbolic link / file "x" is read only (conditions)
public sealed class FileIsSymbolicLinkExpr(Expr path, int line) : Expr(line) { public Expr Path { get; } = path; }
public sealed class FileIsReadOnlyExpr(Expr path, int line) : Expr(line) { public Expr Path { get; } = path; }

// get owner of "x" into owner
public sealed class GetFileOwnerStmt(Expr path, string target, int line) : Stmt(line)
{
    public Expr Path { get; } = path;
    public string Target { get; } = target;
}

// set file "x" to read only / to writable
public sealed class SetFileReadOnlyStmt(Expr path, bool readOnly, int line) : Stmt(line)
{
    public Expr Path { get; } = path;
    public bool ReadOnly { get; } = readOnly;
}

// ---- registry (D78) and event log (D79) -------------------------------------

// get registry value "n" from "path" into t
public sealed class GetRegistryValueStmt(Expr valueName, Expr keyPath, string target, int line) : Stmt(line)
{
    public Expr ValueName { get; } = valueName;
    public Expr KeyPath { get; } = keyPath;
    public string Target { get; } = target;
}

// set registry value "n" to "d" in "path"
public sealed class SetRegistryValueStmt(Expr valueName, Expr value, Expr keyPath, int line) : Stmt(line)
{
    public Expr ValueName { get; } = valueName;
    public Expr Value { get; } = value;
    public Expr KeyPath { get; } = keyPath;
}

// delete registry value "n" from "path"
public sealed class DeleteRegistryValueStmt(Expr valueName, Expr keyPath, int line) : Stmt(line)
{
    public Expr ValueName { get; } = valueName;
    public Expr KeyPath { get; } = keyPath;
}

// registry key "path" exists (a condition)
public sealed class RegistryKeyExistsExpr(Expr keyPath, int line) : Expr(line) { public Expr KeyPath { get; } = keyPath; }

// get event log entries from "System" up to 20 into entries
public sealed class GetEventLogEntriesStmt(Expr logName, Expr maxEntries, string target, int line) : Stmt(line)
{
    public Expr LogName { get; } = logName;
    public Expr MaxEntries { get; } = maxEntries;
    public string Target { get; } = target;
}

// ---- credentials (D81) and the credential vault (D111) ----------------------

// set credential "n" to "secret"
public sealed class SetCredentialStmt(Expr name, Expr secret, int line) : Stmt(line)
{
    public Expr Name { get; } = name;
    public Expr Secret { get; } = secret;
}

// get credential "n" into secret
public sealed class GetCredentialStmt(Expr name, string target, int line) : Stmt(line)
{
    public Expr Name { get; } = name;
    public string Target { get; } = target;
}

// delete credential "n"
public sealed class DeleteCredentialStmt(Expr name, int line) : Stmt(line) { public Expr Name { get; } = name; }

// store secret "api-token" with value token
public sealed class StoreSecretStmt(Expr name, Expr value, int line) : Stmt(line)
{
    public Expr Name { get; } = name;
    public Expr Value { get; } = value;
}

// delete secret "api-token"
public sealed class DeleteSecretStmt(Expr name, int line) : Stmt(line) { public Expr Name { get; } = name; }

// secret "api-token" / secret "api-token" exists
public sealed class SecretReadExpr(Expr name, int line) : Expr(line) { public Expr Name { get; } = name; }
public sealed class SecretExistsExpr(Expr name, int line) : Expr(line) { public Expr Name { get; } = name; }

// ---- power and printing (D82, D83) ------------------------------------------

public enum PowerAction { Lock, SignOut, Restart, ShutDown }

// lock the computer / sign out / restart the computer / shut down the computer
public sealed class PowerActionStmt(PowerAction action, int line) : Stmt(line) { public PowerAction Action { get; } = action; }

// print "file.txt" to "PrinterName"
public sealed class PrintFileStmt(Expr path, Expr printerName, int line) : Stmt(line)
{
    public Expr Path { get; } = path;
    public Expr PrinterName { get; } = printerName;
}
