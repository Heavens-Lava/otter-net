using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Otter.Language;

namespace Otter.Runtime;

// The runtime for operating system integration: processes, commands, system
// information, clipboard, notifications, dialogs, links, owners, read-only
// files, registry, event log, credentials, secrets, power, printing, remote
// commands - the matching cases of otterPS's Invoke-OtterStatement and
// Get-OtterValue, with the same messages. The work itself is in
// Processes.cs, SystemInfo.cs, SystemFiles.cs, Registry.cs, Credentials.cs
// and Clipboard.cs.
public sealed partial class Interpreter
{
    // The program file's path, which names the program's own secrets (D111);
    // otterPS's Set-OtterApplicationId. Without one: "otter-repl".
    public string? ApplicationPath { get; init; }

    bool TryExecuteSystem(Stmt statement, Scope scope)
    {
        switch (statement)
        {
            // ---- programs and processes (D70, D71, D85) ----
            case RunProgramStmt runProgram:
            {
                string target = Values.Format(Evaluate(runProgram.Target, scope));
                OtterThing handle;
                try { handle = OtterProcesses.Start(target); }
                catch (CommandProblem problem) { throw RuntimeError(problem.Message, runProgram.Line); }
                if (runProgram.ResultTarget != null) scope.Set(runProgram.ResultTarget, handle);
                return true;
            }
            case RunSshCommandStmt runSsh:
            {
                string command = Values.Format(Evaluate(runSsh.Command, scope));
                string hostName = Values.Format(Evaluate(runSsh.HostName, scope));
                string output = RunOverSsh(command, hostName, runSsh.Line);
                if (runSsh.ResultTarget != null) scope.Set(runSsh.ResultTarget, output);
                return true;
            }
            // Off Windows only (the parser hands Windows' to the PowerShell Otter).
            case RunRemoteCommandStmt runRemote:
            {
                string command = Values.Format(Evaluate(runRemote.Command, scope));
                string hostName = Values.Format(Evaluate(runRemote.HostName, scope));
                string credentialName = Values.Format(Evaluate(runRemote.CredentialName, scope));
                _ = command; _ = hostName;   // evaluated, as otterPS does, before the credential
                string? password;
                try { password = OtterRemoteCredentials.ReadOffWindows(credentialName); }
                catch (CredentialProblem problem) { throw RuntimeError(problem.Message, runRemote.Line, problem.Suggestion); }
                if (password == null)
                {
                    throw RuntimeError($"I do not have a credential called \"{credentialName}\" - use set credential \"{credentialName}\" to \"...\" to store one first.", runRemote.Line);
                }
                // Not reached: no credential can be decrypted outside Windows.
                throw new InternalProblem("Remote commands need Windows PowerShell remoting.");
            }
            case GetProcessesStmt getProcesses:
                scope.Set(getProcesses.Target, OtterProcesses.List());
                return true;
            case KillProcessStmt kill:
            {
                object? process = Evaluate(kill.Process, scope);
                // Stop-OtterProcess: $rootPid = [int]$ProcessId, before anything is tried.
                int id = CastProcessId(ProcessHandleId(process, "kill a real process handle", kill.Line));
                OtterProcesses.Kill(id, kill.IncludeChildren);
                return true;
            }
            case SetProcessPriorityStmt setPriority:
            {
                object? process = Evaluate(setPriority.Process, scope);
                if (!IsProcessHandle(process)) throw NotAProcessHandle("set the priority of a real process handle", setPriority.Line);
                string priority = Values.Format(Evaluate(setPriority.Priority, scope));
                double processId = ProcessId(((OtterThing)process!).ReadProperty("id"));
                if (!OtterProcesses.TryPriorityClass(priority, out var priorityClass))
                {
                    throw RuntimeError($"I do not know a process priority called \"{priority}\".", setPriority.Line,
                        "set priority of process p to \"high\" (also: \"low\", \"below normal\", \"normal\", \"above normal\", \"realtime\")");
                }
                // Set-OtterProcessPriority casts the id inside its try: a failed cast is its message.
                if (!PowerShellInt32.TryCast(processId, out int id, out string castError))
                {
                    throw RuntimeError($"I could not set that process's priority. {castError}", setPriority.Line);
                }
                try { OtterProcesses.SetPriority(id, priorityClass); }
                catch (CommandProblem problem) { throw RuntimeError(problem.Message, setPriority.Line); }
                return true;
            }
            case WaitForProcessStmt waitFor:
            {
                object? process = Evaluate(waitFor.Process, scope);
                if (!IsProcessHandle(process)) throw NotAProcessHandle("wait for a real process handle", waitFor.Line);
                double seconds = RequireNumber(Evaluate(waitFor.TimeoutSeconds, scope), waitFor.Line, "a number of seconds");
                double processId = ProcessId(((OtterThing)process!).ReadProperty("id"));
                bool finished;
                try { finished = OtterProcesses.Wait(processId, seconds); }
                catch (ProcessTimeoutCast cast) { throw new InternalProblem(cast.Message); }
                if (waitFor.Target != null) scope.Set(waitFor.Target, finished);
                return true;
            }
            // ---- system information (D69, D76, D77) ----
            case GetSystemInfoStmt systemInfo:
            {
                string kind = Values.Format(Evaluate(systemInfo.InfoKind, scope));
                object? value;
                try { value = OtterSystemInfo.Get(kind); }
                catch (SystemInfoProblem problem) { throw RuntimeError(problem.Message, systemInfo.Line, problem.Suggestion); }
                scope.Set(systemInfo.Target, value);
                return true;
            }
            // ---- clipboard, notifications, dialogs (D67) ----
            case CopyToClipboardStmt copyToClipboard:
            {
                string text = Values.Format(Evaluate(copyToClipboard.Text, scope));
                try { OtterClipboard.SetText(text); }
                catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException)
                {
                    throw RuntimeError($"I could not copy to the clipboard: {e.Message}", copyToClipboard.Line);
                }
                return true;
            }
            case GetClipboardStmt getClipboard:
            {
                string? text;
                try { text = OtterClipboard.GetText(); }
                catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException)
                {
                    throw RuntimeError($"I could not read the clipboard: {e.Message}", getClipboard.Line);
                }
                scope.Set(getClipboard.Target, text);
                return true;
            }
            case NotifyStmt notify:
            {
                if (!OperatingSystem.IsWindows()) throw NotOnWindows("Notifications (notify)", notify.Line);
                string title = Values.Format(Evaluate(notify.Title, scope));
                string message = Values.Format(Evaluate(notify.Message, scope));
                // A desktop that cannot show a notification shows nothing.
                try { OtterDesktop.Notify(title, message); }
                catch (Exception e) when (e is ExternalException or Win32Exception or InvalidOperationException) { }
                return true;
            }
            case ChooseFileStmt choose:
                if (!OperatingSystem.IsWindows())
                {
                    throw NotOnWindows(choose.Dialog switch
                    {
                        FileDialogKind.Folder => "Folder dialogs (choose folder)",
                        FileDialogKind.SaveFile => "File dialogs (choose file to save)",
                        _ => "File dialogs (choose file)",
                    }, choose.Line);
                }
                scope.Set(choose.Target, OtterDesktop.ChooseFile(choose.Dialog == FileDialogKind.Folder, choose.Dialog == FileDialogKind.SaveFile));
                return true;
            // ---- links, owners, read-only files (D73, D74) ----
            case CreateSymbolicLinkStmt createLink:
            {
                string linkPath = PathArgument(createLink.LinkPath, scope);
                string targetPath = PathArgument(createLink.TargetPath, scope);
                Library(() => OtterSystemFiles.CreateSymbolicLink(linkPath, targetPath), createLink.Line);
                return true;
            }
            case GetSymbolicLinkTargetStmt linkTarget:
            {
                string linkPath = PathArgument(linkTarget.LinkPath, scope);
                scope.Set(linkTarget.Target, Library(() => OtterSystemFiles.GetSymbolicLinkTarget(linkPath), linkTarget.Line));
                return true;
            }
            case GetFileOwnerStmt owner:
            {
                if (!OperatingSystem.IsWindows()) throw NotOnWindows("Reading a file's owner", owner.Line);
                string path = PathArgument(owner.Path, scope);
                try { scope.Set(owner.Target, OtterSystemFiles.GetOwner(path)); }
                catch (FileProblem problem) { throw RuntimeError(problem.Message, owner.Line, problem.Suggestion); }
                return true;
            }
            case SetFileReadOnlyStmt setReadOnly:
            {
                string path = PathArgument(setReadOnly.Path, scope);
                Library(() => OtterSystemFiles.SetReadOnly(path, setReadOnly.ReadOnly), setReadOnly.Line);
                return true;
            }
            // ---- registry (D78) and event log (D79) ----
            case GetRegistryValueStmt getRegistry:
            {
                if (!OperatingSystem.IsWindows()) throw NotOnWindows("The registry", getRegistry.Line);
                string valueName = Values.Format(Evaluate(getRegistry.ValueName, scope));
                string keyPath = Values.Format(Evaluate(getRegistry.KeyPath, scope));
                try { scope.Set(getRegistry.Target, OtterRegistry.GetValue(valueName, keyPath)); }
                catch (RegistryProblem problem) { throw RuntimeError(problem.Message, getRegistry.Line, problem.Suggestion); }
                return true;
            }
            case SetRegistryValueStmt setRegistry:
            {
                if (!OperatingSystem.IsWindows()) throw NotOnWindows("The registry", setRegistry.Line);
                string valueName = Values.Format(Evaluate(setRegistry.ValueName, scope));
                string value = Values.Format(Evaluate(setRegistry.Value, scope));
                string keyPath = Values.Format(Evaluate(setRegistry.KeyPath, scope));
                try { OtterRegistry.SetValue(valueName, value, keyPath); }
                catch (RegistryProblem problem) { throw RuntimeError(problem.Message, setRegistry.Line, problem.Suggestion); }
                return true;
            }
            case DeleteRegistryValueStmt deleteRegistry:
            {
                if (!OperatingSystem.IsWindows()) throw NotOnWindows("The registry", deleteRegistry.Line);
                string valueName = Values.Format(Evaluate(deleteRegistry.ValueName, scope));
                string keyPath = Values.Format(Evaluate(deleteRegistry.KeyPath, scope));
                try { OtterRegistry.DeleteValue(valueName, keyPath); }
                catch (RegistryProblem problem) { throw RuntimeError(problem.Message, deleteRegistry.Line, problem.Suggestion); }
                return true;
            }
            case GetEventLogEntriesStmt eventLog:
            {
                if (!OperatingSystem.IsWindows()) throw NotOnWindows("The event log", eventLog.Line);
                string logName = Values.Format(Evaluate(eventLog.LogName, scope));
                double maxEntries = RequireNumber(Evaluate(eventLog.MaxEntries, scope), eventLog.Line, "a maximum number of entries");
                OtterList entries;
                try { entries = OtterEventLog.Read(logName, maxEntries); }
                catch (EventLogProblem problem) { throw RuntimeError($"I could not read the event log \"{logName}\". {problem.Message}", eventLog.Line); }
                scope.Set(eventLog.Target, entries);
                return true;
            }
            // ---- credentials (D81) ----
            case SetCredentialStmt setCredential:
            {
                if (!OperatingSystem.IsWindows()) throw NotOnWindows("Stored credentials (set credential)", setCredential.Line);
                string name = Values.Format(Evaluate(setCredential.Name, scope));
                string secret = Values.Format(Evaluate(setCredential.Secret, scope));
                try { OtterCredentials.Set(name, secret); }
                catch (CredentialProblem problem) { throw RuntimeError(problem.Message, setCredential.Line, problem.Suggestion); }
                return true;
            }
            case GetCredentialStmt getCredential:
            {
                if (!OperatingSystem.IsWindows()) throw NotOnWindows("Stored credentials (get credential)", getCredential.Line);
                string name = Values.Format(Evaluate(getCredential.Name, scope));
                try { scope.Set(getCredential.Target, OtterCredentials.Get(name)); }
                catch (CredentialProblem problem) { throw RuntimeError(problem.Message, getCredential.Line, problem.Suggestion); }
                return true;
            }
            case DeleteCredentialStmt deleteCredential:
            {
                if (!OperatingSystem.IsWindows()) throw NotOnWindows("Stored credentials (delete credential)", deleteCredential.Line);
                string name = Values.Format(Evaluate(deleteCredential.Name, scope));
                try { OtterCredentials.Delete(name); }
                catch (CredentialProblem problem) { throw RuntimeError(problem.Message, deleteCredential.Line, problem.Suggestion); }
                return true;
            }
            // ---- the credential vault (D111) ----
            case StoreSecretStmt storeSecret:
                StoreSecret(storeSecret, scope);
                return true;
            case DeleteSecretStmt deleteSecret:
            {
                if (!OperatingSystem.IsWindows()) throw NoVault(deleteSecret.Line);
                object? nameValue = Evaluate(deleteSecret.Name, scope);
                string target = SecretTarget(nameValue, deleteSecret.Line);
                bool removed;
                try { removed = OtterVault.Delete(target); }
                catch (Win32Exception e) { throw RuntimeError($"I could not delete the secret: {OtterCommands.FrameworkMessage(e)}", deleteSecret.Line); }
                if (!removed)
                {
                    throw RuntimeError($"There is no secret called \"{Values.Format(nameValue)}\" to delete.", deleteSecret.Line, "if secret \"name\" exists ...");
                }
                return true;
            }
            // ---- power and printing (D82, D83) ----
            case PowerActionStmt power:
                if (!OperatingSystem.IsWindows()) throw NotOnWindows("Locking, signing out, restarting or shutting down the computer", power.Line);
                RunPowerAction(power.Action, power.Line);
                return true;
            case PrintFileStmt print:
            {
                if (!OperatingSystem.IsWindows()) throw NotOnWindows("Printing", print.Line);
                string path = PathArgument(print.Path, scope);
                string printerName = Values.Format(Evaluate(print.PrinterName, scope));
                string content = Library(() => OtterFiles.Read(path), print.Line);
                try { OtterDesktop.Print(path, content, printerName); }
                catch (PrintProblem problem) { throw RuntimeError(problem.Message, print.Line, problem.Suggestion); }
                return true;
            }
            default:
                return false;
        }
    }

    bool TryEvaluateSystem(Expr expression, Scope scope, out object? value)
    {
        switch (expression)
        {
            case FileIsSymbolicLinkExpr isLink:
            {
                string path = PathArgument(isLink.Path, scope);
                value = Library(() => OtterSystemFiles.IsSymbolicLink(path), isLink.Line);
                return true;
            }
            case FileIsReadOnlyExpr isReadOnly:
            {
                string path = PathArgument(isReadOnly.Path, scope);
                value = Library(() => OtterSystemFiles.IsReadOnly(path), isReadOnly.Line);
                return true;
            }
            case RegistryKeyExistsExpr keyExists:
            {
                if (!OperatingSystem.IsWindows()) throw NotOnWindows("The registry", keyExists.Line);
                string keyPath = Values.Format(Evaluate(keyExists.KeyPath, scope));
                value = OtterRegistry.KeyExists(keyPath);
                return true;
            }
            case SecretReadExpr secretRead:
            {
                if (!OperatingSystem.IsWindows()) throw NoVault(secretRead.Line);
                object? nameValue = Evaluate(secretRead.Name, scope);
                string target = SecretTarget(nameValue, secretRead.Line);
                string? kind;
                byte[]? blob;
                bool found;
                try { found = OtterVault.Read(target, out kind, out blob); }
                catch (Win32Exception e) { throw RuntimeError($"I could not read the secret: {OtterCommands.FrameworkMessage(e)}", secretRead.Line); }
                // A missing secret is an error, never an empty text pretending to exist.
                if (!found)
                {
                    throw RuntimeError($"There is no secret called \"{Values.Format(nameValue)}\".", secretRead.Line, "if secret \"name\" exists ...");
                }
                value = kind == "otter-bytes" ? new OtterBytes(blob!) : Encoding.UTF8.GetString(blob!);
                return true;
            }
            case SecretExistsExpr secretExists:
            {
                if (!OperatingSystem.IsWindows()) throw NoVault(secretExists.Line);
                string target = SecretTarget(Evaluate(secretExists.Name, scope), secretExists.Line);
                try { value = OtterVault.Read(target, out _, out _); }
                catch (Win32Exception e) { throw RuntimeError($"I could not check for the secret: {OtterCommands.FrameworkMessage(e)}", secretExists.Line); }
                return true;
            }
            default:
                value = null;
                return false;
        }
    }

    // ---- helpers ----------------------------------------------------------------

    // Assert-OtterWindowsFeature: off Windows, one clear Otter message.
    OtterError NotOnWindows(string feature, int line)
    {
        string os = OperatingSystem.IsMacOS() ? "macOS" : "Linux";
        return RuntimeError($"{feature} is only available on Windows, and this program is running on {os}.", line);
    }

    static bool IsProcessHandle(object? value) => value is OtterThing thing && thing.HasProperty("id");

    OtterError NotAProcessHandle(string what, int line) =>
        RuntimeError($"I can only {what}, such as the one \"run ... into p\" or \"get processes into list\" gives you.", line);

    double ProcessHandleId(object? process, string what, int line)
    {
        if (!IsProcessHandle(process)) throw NotAProcessHandle(what, line);
        return ProcessId(((OtterThing)process!).ReadProperty("id"));
    }

    // [int]$ProcessId: the nearest whole number, halves to even; PowerShell's
    // own error beyond Int32.
    static int CastProcessId(double processId)
    {
        if (!PowerShellInt32.TryCast(processId, out int id, out string message)) throw new InternalProblem(message);
        return id;
    }

    // A process id reaches otterPS's helpers as a [double] parameter, which
    // refuses what is not a number; each helper casts it to [int] itself.
    static double ProcessId(object? id)
    {
        double number;
        if (id is null) number = 0;
        else if (id is double d) number = d;
        else if (id is bool b) number = b ? 1 : 0;
        else if (!Values.TryNumber(id, out number))
        {
            throw new InternalProblem($"Cannot process argument transformation on parameter 'ProcessId'. Cannot convert value \"{Values.Format(id)}\" to type \"System.Double\". Error: \"Input string was not in a correct format.\"");
        }
        return number;
    }

    // Invoke-OtterSshCommand: the system's ssh client, never waiting at a
    // password prompt (BatchMode), trusting a new host key but never a
    // changed one.
    string RunOverSsh(string command, string hostName, int line)
    {
        string sshPath = File.Exists(@"C:\Windows\System32\OpenSSH\ssh.exe") ? @"C:\Windows\System32\OpenSSH\ssh.exe" : "ssh.exe";
        // A host ssh could read as an option or as several arguments is refused.
        if (string.IsNullOrEmpty(hostName) || hostName.StartsWith('-') || Regex.IsMatch(hostName, "[\\s\"'\\p{Cc}]"))
        {
            string shown = Regex.Replace(hostName, "\\p{Cc}", " ");
            throw RuntimeError($"I cannot use \"{shown}\" as an ssh host. A host cannot be empty, start with '-', or contain spaces, quotes or control characters.", line);
        }
        OtterThing result;
        try { result = OtterCommands.Run($"{sshPath} -o BatchMode=yes -o StrictHostKeyChecking=accept-new -- {hostName} {command}"); }
        catch (CommandProblem problem) { throw RuntimeError(problem.Message, line); }
        string output = Values.Format(result.ReadProperty("output"));
        string errorOutput = Values.Format(result.ReadProperty("error output"));
        if (result.ReadProperty("exit code") is double exitCode && exitCode != 0)
        {
            string detail = errorOutput.Length > 0 ? errorOutput : output;
            throw RuntimeError($"I could not run that command over ssh on \"{hostName}\". {detail}".Trim(), line);
        }
        return output;
    }

    // Invoke-OtterPowerAction: Windows' own shutdown.exe and rundll32.exe, with
    // 30 seconds' warning before a restart or shutdown.
    void RunPowerAction(PowerAction action, int line)
    {
        (string name, string commandLine) = action switch
        {
            PowerAction.Lock => ("lock", "rundll32.exe user32.dll,LockWorkStation"),
            PowerAction.SignOut => ("signOut", "shutdown.exe /l"),
            PowerAction.Restart => ("restart", "shutdown.exe /r /t 30 /c \"An Otter program requested a restart.\""),
            _ => ("shutDown", "shutdown.exe /s /t 30 /c \"An Otter program requested a shutdown.\""),
        };
        try { OtterCommands.Run(commandLine); }
        catch (CommandProblem problem) { throw RuntimeError($"I could not {name} the computer. {problem.Message}", line); }
    }

    // ---- the credential vault (D111) ----

    // Assert-OtterVaultAvailable.
    OtterError NoVault(int line) =>
        RuntimeError("The credential vault needs an operating-system credential store, and this platform does not have one Otter supports yet.", line);

    // Get-OtterSecretTarget: each program's secrets are its own.
    string SecretTarget(object? name, int line)
    {
        if (name is not string text || string.IsNullOrWhiteSpace(text))
        {
            throw RuntimeError($"I need a secret name as non-empty text, but this is {Values.TypeName(name)}.", line, "store secret \"api-token\" with value token");
        }
        return $"otter:{ApplicationId()}:{text}";
    }

    // Set-OtterApplicationId: the file's name and the start of the SHA-256 of
    // its full path in lower case.
    string ApplicationId()
    {
        if (string.IsNullOrEmpty(ApplicationPath)) return "otter-repl";
        string full = Path.GetFullPath(ApplicationPath).ToLowerInvariant();
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(full));
        string shortHash = Convert.ToHexString(digest, 0, 8).ToLowerInvariant();
        return $"{Path.GetFileNameWithoutExtension(ApplicationPath)}-{shortHash}";
    }

    void StoreSecret(StoreSecretStmt storeSecret, Scope scope)
    {
        if (!OperatingSystem.IsWindows()) throw NoVault(storeSecret.Line);
        string target = SecretTarget(Evaluate(storeSecret.Name, scope), storeSecret.Line);
        object? secretValue = Evaluate(storeSecret.Value, scope);
        // Text and bytes only; never silently converted to one another.
        string kind;
        byte[] blob;
        if (secretValue is string text)
        {
            kind = "otter-text";
            blob = Encoding.UTF8.GetBytes(text);
        }
        else if (secretValue is OtterBytes bytes)
        {
            kind = "otter-bytes";
            blob = bytes.Value;
        }
        else
        {
            throw RuntimeError($"A secret can hold text or bytes, but this is {Values.TypeName(secretValue)}.", storeSecret.Line);
        }
        if (blob.Length == 0) throw RuntimeError("A secret cannot be empty.", storeSecret.Line);
        if (blob.Length > OtterVault.MaxBlob)
        {
            throw RuntimeError($"That secret is {blob.Length} bytes, but the credential store holds at most {OtterVault.MaxBlob} bytes per secret.", storeSecret.Line);
        }
        // Never echo the value; the Windows message names only the failure.
        try { OtterVault.Write(target, kind, blob); }
        catch (Win32Exception e) { throw RuntimeError($"I could not store the secret: {OtterCommands.FrameworkMessage(e)}", storeSecret.Line); }
    }
}
