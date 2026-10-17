namespace Otter.Language;

// The parser for operating system integration: processes, commands, system
// information, clipboard, notifications, dialogs, links, owners, read-only
// files, registry, event log, credentials, secrets, power, printing, remote
// commands. A port of the matching branches of otterPS's Read-OtterStatement,
// Read-OtterValue and Read-OtterConditionPrimary, with the same messages.
//
// The forms already in Parser.cs (environment variables, system folders, the
// current directory, zip, folder copy and move, `run command`) stay there;
// this file takes the ones that sent the program to the PowerShell Otter.
public sealed partial class Parser
{
    // Kinds of `get system information` otter-net runs on Windows. The other
    // two (scheduled tasks and printers) come from COM and WMI, which a
    // native program cannot use; the PowerShell Otter answers those.
    static readonly string[] NativeWindowsSystemInfoKinds = { "os", "cpu", "memory", "disk", "network", "user", "groups", "software", "services" };

    // A statement this area starts at `start`, or null (the token is left unread).
    Stmt? TryReadSystemStatement(Token start)
    {
        TokenKind nextKind = At(1)?.Kind ?? TokenKind.EndOfFile;
        // `get is 5`, `run of x is 2`: the line is an assignment (otterPS
        // decides this before its statement switch).
        if (IsIdentifier(start) && nextKind is TokenKind.Is or TokenKind.Are or TokenKind.Of or TokenKind.IsNot) return null;
        Token? next = At(1);
        bool NextWord(string word) => next is { Kind: TokenKind.Identifier } && Same(next.Text, word);

        // store secret "name" with value V / delete secret "name" (D111), read
        // before otterPS's statement switch.
        if (SameAny(start.Text, "store", "delete") && NextWord("secret") && !IsAt(2, TokenKind.Is))
        {
            return ReadSecretStatement(start);
        }

        switch (start.Kind)
        {
            case TokenKind.Get when next?.Kind == TokenKind.Identifier:
                return ReadSystemGet(start, next);
            case TokenKind.Set when next?.Kind == TokenKind.File || NextWord("priority") || NextWord("registry") || NextWord("credential"):
                return ReadSystemSet(start, next!);
            case TokenKind.Delete when NextWord("registry") || NextWord("credential"):
                return ReadSystemDelete(start, next!);
            case TokenKind.Copy when next?.Kind != TokenKind.Folder:
                return TryReadCopyToClipboard(start);
            case TokenKind.Create when NextWord("symbolic"):
                return ReadCreateSymbolicLink(start);
            case TokenKind.Kill:
                return ReadKill(start);
            case TokenKind.Wait when NextWord("for"):
                return ReadWaitForProcess(start);
            case TokenKind.Lock:
            case TokenKind.Sign:
            case TokenKind.Restart:
            case TokenKind.Shut:
                return ReadPowerAction(start);
            case TokenKind.Print:
            {
                Read();
                Expr path = ReadValue();
                Expect(TokenKind.To, "I expected \"to\" and a printer name.");
                Expr printer = ReadValue();
                Expect(TokenKind.Newline, "I expected the print statement to end here.");
                if (OperatingSystem.IsWindows()) throw Unsupported("printing");
                return new PrintFileStmt(path, printer, start.Line);
            }
            case TokenKind.Notify:
            {
                Read();
                Expr title = ReadValue();
                Expect(TokenKind.With, "I expected \"with\" and a message.");
                Expr message = ReadValue();
                Expect(TokenKind.Newline, "I expected the notify statement to end here.");
                if (OperatingSystem.IsWindows()) throw Unsupported("notifications");
                return new NotifyStmt(title, message, start.Line);
            }
            case TokenKind.Choose when nextKind != TokenKind.From:
                return ReadChooseDialog(start);
            case TokenKind.Run:
                return ReadSystemRun(start);
            default:
                return null;
        }
    }

    // A value this area starts at `token`, or null.
    Expr? TryReadSystemValue(Token token)
    {
        // D111: `secret "name"` reads a secret; `secret "name" exists` tests for
        // one. "secret" is an ordinary name unless a text follows, or a name
        // that ends the line or comes before "exists".
        if (token.Kind == TokenKind.Identifier && Same(token.Text, "secret") && At(1) is { } secretNext)
        {
            bool literalName = secretNext.Kind == TokenKind.String;
            bool variableName = secretNext.Kind == TokenKind.Identifier && At(2) is { Kind: TokenKind.Newline or TokenKind.Exists };
            if (literalName || variableName)
            {
                Read();
                Expr name = ReadValue();
                if (Is(TokenKind.Exists))
                {
                    Read();
                    return new SecretExistsExpr(name, token.Line);
                }
                return new SecretReadExpr(name, token.Line);
            }
        }
        return null;
    }

    // A condition this area starts at `head`, or null.
    Expr? TryReadSystemCondition(Token head)
    {
        // D78: registry key "HKCU:\Software\MyApp" exists
        if (head.Kind == TokenKind.Identifier && Same(head.Text, "registry"))
        {
            Read();
            ExpectWord("key", "I expected \"key\" after \"registry\".", "if registry key \"HKCU:\\Software\\MyApp\" exists");
            Expr keyPath = ReadValue();
            Expect(TokenKind.Exists, "I expected \"exists\" after the registry key path.");
            return new RegistryKeyExistsExpr(keyPath, head.Line);
        }
        // file "x" exists / is locked (D72) / is read only (D74) / is a symbolic link (D73)
        if (head.Kind == TokenKind.File)
        {
            Read();
            Expr path = ReadValue();
            if (Is(TokenKind.Exists))
            {
                Read();
                return new FileExistsExpr(path, head.Line);
            }
            Expect(TokenKind.Is, "I expected \"exists\", \"is locked\", \"is read only\", or \"is a symbolic link\" after the file path.");
            Token afterIs = Current;
            if (afterIs.Kind == TokenKind.Identifier && Same(afterIs.Text, "locked"))
            {
                Read();
                return new FileLockedExpr(path, head.Line);
            }
            if (afterIs.Kind == TokenKind.Identifier && Same(afterIs.Text, "read"))
            {
                Read();
                ExpectWord("only", "I expected \"only\" after \"read\".", "file \"x\" is read only");
                return new FileIsReadOnlyExpr(path, head.Line);
            }
            Expect(TokenKind.A, "I expected \"locked\", \"read only\", or \"a symbolic link\" after \"is\".");
            ExpectWord("symbolic", "I expected \"symbolic link\" after \"is a\".", "file \"x\" is a symbolic link");
            ExpectWord("link", "I expected \"link\" after \"symbolic\".", "file \"x\" is a symbolic link");
            return new FileIsSymbolicLinkExpr(path, head.Line);
        }
        return null;
    }

    // ---- statements ----------------------------------------------------------

    Stmt ReadSecretStatement(Token start)
    {
        Read();
        Read();
        Expr name = ReadValue();
        if (Same(start.Text, "delete"))
        {
            Expect(TokenKind.Newline, "I expected the delete secret statement to end here.");
            return new DeleteSecretStmt(name, start.Line);
        }
        Expect(TokenKind.With, "I expected \"with value\" and the secret to store.", "store secret \"api-token\" with value token");
        ExpectWord("value", "I expected \"value\" after \"with\".", "store secret \"api-token\" with value token");
        Expr value = ReadMathExpression();
        Expect(TokenKind.Newline, "I expected the store secret statement to end here.");
        return new StoreSecretStmt(name, value, start.Line);
    }

    // The `get` forms of this area; null for the others (Parser.cs reads them).
    Stmt? ReadSystemGet(Token start, Token word)
    {
        string text = word.Text.ToLowerInvariant();
        switch (text)
        {
            // get clipboard into text (D67)
            case "clipboard":
                Read(); Read();
                return new GetClipboardStmt(ReadGetResult(), start.Line);
            // get arguments into args (D94)
            case "arguments":
                Read(); Read();
                return new AssignStmt(new VariableExpr(ReadGetResult(), start.Line), new VariableExpr("arguments", start.Line), start.Line);
            // get system information "os" into info (D69); `get system folder`
            // is read in Parser.cs.
            case "system" when !IsAt(2, TokenKind.Folder):
            {
                Read(); Read();
                ExpectWord("information", "I expected \"folder\" or \"information\" after \"system\".", "get system information \"os\" into info");
                Expr infoKind = ReadValue();
                string target = ReadGetResult();
                if (OperatingSystem.IsWindows() &&
                    !(infoKind is LiteralExpr { Value: string kindText } && SameAny(kindText.ToLowerInvariant(), NativeWindowsSystemInfoKinds)))
                {
                    throw Unsupported("this kind of system information");
                }
                return new GetSystemInfoStmt(infoKind, target, start.Line);
            }
            // get processes into list (D70)
            case "processes":
                Read(); Read();
                return new GetProcessesStmt(ReadGetResult(), start.Line);
            // get symbolic link target of "l" into t (D73)
            case "symbolic":
            {
                Read(); Read();
                ExpectWord("link", "I expected \"link\" after \"symbolic\".", "get symbolic link target of \"l\" into t");
                ExpectWord("target", "I expected \"target\" after \"link\".", "get symbolic link target of \"l\" into t");
                Expect(TokenKind.Of, "I expected \"of\" and a link path.");
                Expr linkPath = ReadValue();
                return new GetSymbolicLinkTargetStmt(linkPath, ReadGetResult(), start.Line);
            }
            // get owner of "x" into owner (D74)
            case "owner":
            {
                Read(); Read();
                Expect(TokenKind.Of, "I expected \"of\" and a file path.");
                Expr path = ReadValue();
                return new GetFileOwnerStmt(path, ReadGetResult(), start.Line);
            }
            // get registry value "n" from "path" into t (D78)
            case "registry":
            {
                Read(); Read();
                ExpectWord("value", "I expected \"value\" after \"registry\".", "get registry value \"n\" from \"path\" into t");
                Expr valueName = ReadValue();
                Expect(TokenKind.From, "I expected \"from\" and a registry key path.");
                Expr keyPath = ReadValue();
                return new GetRegistryValueStmt(valueName, keyPath, ReadGetResult(), start.Line);
            }
            // get credential "n" into secret (D81)
            case "credential":
            {
                Read(); Read();
                Expr name = ReadValue();
                return new GetCredentialStmt(name, ReadGetResult(), start.Line);
            }
            // get event log entries from "System" up to 20 into entries (D79)
            case "event":
            {
                Read(); Read();
                const string example = "get event log entries from \"System\" up to 20 into entries";
                ExpectWord("log", "I expected \"log\" after \"event\".", example);
                ExpectWord("entries", "I expected \"entries\" after \"log\".", example);
                Expect(TokenKind.From, "I expected \"from\" and a log name.");
                Expr logName = ReadValue();
                ExpectWord("up", "I expected \"up to\" and a maximum count.", example);
                Expect(TokenKind.To, "I expected \"to\" and a maximum count.");
                Expr maxEntries = ReadValue();
                string target = ReadGetResult();
                if (OperatingSystem.IsWindows()) throw Unsupported("the event log");
                return new GetEventLogEntriesStmt(logName, maxEntries, target, start.Line);
            }
            default:
                return null;
        }
    }

    Stmt ReadSystemSet(Token start, Token word)
    {
        Read(); Read();
        // set file "x" to read only / to writable (D74)
        if (word.Kind == TokenKind.File)
        {
            Expr path = ReadValue();
            Expect(TokenKind.To, "I expected \"to\" and \"read only\" or \"writable\".");
            Token setting = Current;
            bool readOnly;
            if (setting.Kind == TokenKind.Identifier && Same(setting.Text, "read"))
            {
                Read();
                ExpectWord("only", "I expected \"only\" after \"read\".", "set file \"x\" to read only");
                readOnly = true;
            }
            else if (setting.Kind == TokenKind.Identifier && Same(setting.Text, "writable"))
            {
                Read();
                readOnly = false;
            }
            else
            {
                throw Error("I expected \"read only\" or \"writable\" after \"to\".", setting, "set file \"x\" to read only");
            }
            Expect(TokenKind.Newline, "I expected the set statement to end here.");
            return new SetFileReadOnlyStmt(path, readOnly, start.Line);
        }
        switch (word.Text.ToLowerInvariant())
        {
            // set priority of process p to "high" (D71)
            case "priority":
            {
                Expect(TokenKind.Of, "I expected \"of process\" after \"priority\".");
                ExpectWord("process", "I expected \"process\" after \"of\".", "set priority of process p to \"high\"");
                Expr process = ReadValue();
                Expect(TokenKind.To, "I expected \"to\" and a priority level.");
                Expr priority = ReadValue();
                Expect(TokenKind.Newline, "I expected the set statement to end here.");
                return new SetProcessPriorityStmt(process, priority, start.Line);
            }
            // set registry value "n" to "d" in "path" (D78)
            case "registry":
            {
                ExpectWord("value", "I expected \"value\" after \"registry\".", "set registry value \"n\" to \"d\" in \"path\"");
                Expr valueName = ReadValue();
                Expect(TokenKind.To, "I expected \"to\" and a value.");
                Expr value = ReadValue();
                Expect(TokenKind.In, "I expected \"in\" and a registry key path.");
                Expr keyPath = ReadValue();
                Expect(TokenKind.Newline, "I expected the set statement to end here.");
                return new SetRegistryValueStmt(valueName, value, keyPath, start.Line);
            }
            // set credential "n" to "secret" (D81)
            default:
            {
                Expr name = ReadValue();
                Expect(TokenKind.To, "I expected \"to\" and a secret value.");
                Expr secret = ReadValue();
                Expect(TokenKind.Newline, "I expected the set statement to end here.");
                return new SetCredentialStmt(name, secret, start.Line);
            }
        }
    }

    Stmt ReadSystemDelete(Token start, Token word)
    {
        Read(); Read();
        // delete registry value "n" from "path" (D78)
        if (Same(word.Text, "registry"))
        {
            ExpectWord("value", "I expected \"value\" after \"registry\".", "delete registry value \"n\" from \"path\"");
            Expr valueName = ReadValue();
            Expect(TokenKind.From, "I expected \"from\" and a registry key path.");
            Expr keyPath = ReadValue();
            Expect(TokenKind.Newline, "I expected the delete statement to end here.");
            return new DeleteRegistryValueStmt(valueName, keyPath, start.Line);
        }
        // delete credential "n" (D81)
        Expr name = ReadValue();
        Expect(TokenKind.Newline, "I expected the delete statement to end here.");
        return new DeleteCredentialStmt(name, start.Line);
    }

    // copy "text" to clipboard (D67). Any other copy is left to Parser.cs,
    // which reads the line again from the start.
    Stmt? TryReadCopyToClipboard(Token start)
    {
        int saved = position;
        try
        {
            Read();
            ReadValue();
            bool clipboard = Is(TokenKind.To) && At(1) is { Kind: TokenKind.Identifier } word && Same(word.Text, "clipboard");
            position = saved;
            if (!clipboard) return null;
        }
        catch (OtterError)
        {
            position = saved;
            return null;
        }
        Read();
        Expr source = ReadValue();
        Read(); Read();
        Expect(TokenKind.Newline, "I expected the copy statement to end here.");
        return new CopyToClipboardStmt(source, start.Line);
    }

    // create symbolic link "l" pointing to "t" (D73)
    Stmt ReadCreateSymbolicLink(Token start)
    {
        Read(); Read();
        const string example = "create symbolic link \"l\" pointing to \"t\"";
        ExpectWord("link", "I expected \"link\" after \"symbolic\".", example);
        Expr linkPath = ReadValue();
        ExpectWord("pointing", "I expected \"pointing to\" and a target path.", example);
        Expect(TokenKind.To, "I expected \"to\" and a target path.");
        Expr targetPath = ReadValue();
        Expect(TokenKind.Newline, "I expected the create statement to end here.");
        return new CreateSymbolicLinkStmt(linkPath, targetPath, start.Line);
    }

    // kill process p [and its children] (D70)
    Stmt ReadKill(Token start)
    {
        Read();
        ExpectWord("process", "I expected \"process\" after \"kill\".", "kill process p");
        Expr process = ReadValue();
        bool includeChildren = false;
        if (Is(TokenKind.And))
        {
            Read();
            ExpectWord("its", "I expected \"its children\" after \"and\".", "kill process p and its children");
            ExpectWord("children", "I expected \"children\" after \"its\".", "kill process p and its children");
            includeChildren = true;
        }
        Expect(TokenKind.Newline, "I expected the kill statement to end here.");
        return new KillProcessStmt(process, includeChildren, start.Line);
    }

    // wait for process p up to 5 seconds [into finished] (D71)
    Stmt ReadWaitForProcess(Token start)
    {
        Read(); Read();
        const string example = "wait for process p up to 5 seconds";
        ExpectWord("process", "I expected \"process\" after \"for\".", example);
        Expr process = ReadValue();
        ExpectWord("up", "I expected \"up to\" and a number of seconds.", example);
        Expect(TokenKind.To, "I expected \"to\" and a number of seconds.");
        Expr seconds = ReadValue();
        Expect(TokenKind.Second, "I expected \"seconds\" after the number.");
        string? target = null;
        if (Is(TokenKind.Into))
        {
            Read();
            target = ReadVariableName("I expected a result name after \"into\".").Text;
        }
        Expect(TokenKind.Newline, "I expected the wait statement to end here.");
        return new WaitForProcessStmt(process, seconds, target, start.Line);
    }

    // lock the computer / sign out / restart the computer / shut down the computer (D82)
    Stmt ReadPowerAction(Token start)
    {
        Read();
        PowerAction action;
        switch (start.Kind)
        {
            case TokenKind.Lock:
                ReadOptionalTheBeforeName();
                ExpectWord("computer", "I expected \"the computer\" after \"lock\".", "lock the computer");
                Expect(TokenKind.Newline, "I expected the lock statement to end here.");
                action = PowerAction.Lock;
                break;
            case TokenKind.Sign:
                ExpectWord("out", "I expected \"out\" after \"sign\".", "sign out");
                Expect(TokenKind.Newline, "I expected the sign out statement to end here.");
                action = PowerAction.SignOut;
                break;
            case TokenKind.Restart:
                ReadOptionalTheBeforeName();
                ExpectWord("computer", "I expected \"the computer\" after \"restart\".", "restart the computer");
                Expect(TokenKind.Newline, "I expected the restart statement to end here.");
                action = PowerAction.Restart;
                break;
            default:
                ExpectWord("down", "I expected \"down\" after \"shut\".", "shut down the computer");
                ReadOptionalTheBeforeName();
                ExpectWord("computer", "I expected \"the computer\" after \"shut down\".", "shut down the computer");
                Expect(TokenKind.Newline, "I expected the shut down statement to end here.");
                action = PowerAction.ShutDown;
                break;
        }
        return new PowerActionStmt(action, start.Line);
    }

    // otterPS's Read-OtterOptionalTheBeforeName.
    void ReadOptionalTheBeforeName()
    {
        if (Same(Current.Text, "the") && At(1) is { } after && IsIdentifier(after)) Read();
    }

    // choose file into path / choose folder into path / choose file to save into path (D67)
    Stmt ReadChooseDialog(Token start)
    {
        Read();
        FileDialogKind dialog;
        if (Is(TokenKind.Folder))
        {
            Read();
            dialog = FileDialogKind.Folder;
        }
        else
        {
            Expect(TokenKind.File, "I expected \"file\" or \"folder\" after \"choose\".");
            dialog = FileDialogKind.OpenFile;
            if (Is(TokenKind.To))
            {
                Read();
                ExpectWord("save", "I expected \"save\" after \"to\".", "choose file to save into path");
                dialog = FileDialogKind.SaveFile;
            }
        }
        Expect(TokenKind.Into, "I expected \"into\" and a result name.");
        Token target = ReadVariableName("I expected a result name after \"into\".");
        Expect(TokenKind.Newline, "I expected the choose statement to end here.");
        if (OperatingSystem.IsWindows()) throw Unsupported("file dialogs");
        return new ChooseFileStmt(dialog, target.Text, start.Line);
    }

    // run "notepad.exe" [into p] / run command "git status" [into result]
    // run command "..." on remote "host" using credential "n" [into result] (D84)
    // run command "..." over ssh to "user@host" [into result] (D85)
    Stmt ReadSystemRun(Token start)
    {
        Read();
        bool isCommand = false;
        if (Is(TokenKind.Command)) { Read(); isCommand = true; }
        Expr target = ReadValue();
        Token after = Current;
        if (isCommand && after.Kind == TokenKind.Identifier && Same(after.Text, "on"))
        {
            Read();
            const string example = "run command \"...\" on remote \"host\" using credential \"n\"";
            ExpectWord("remote", "I expected \"remote\" after \"on\".", example);
            ReadValue();
            ExpectWord("using", "I expected \"using credential\" after the host name.", example);
            ExpectWord("credential", "I expected \"credential\" after \"using\".", example);
            ReadValue();
            ReadRunResultTarget();
            // PowerShell remoting (Invoke-Command over WinRM) has no
            // equivalent outside PowerShell: the PowerShell Otter runs these.
            throw Unsupported("remote commands");
        }
        if (isCommand && after.Kind == TokenKind.Identifier && Same(after.Text, "over"))
        {
            Read();
            ExpectWord("ssh", "I expected \"ssh\" after \"over\".", "run command \"...\" over ssh to \"user@host\"");
            Expect(TokenKind.To, "I expected \"to\" and a host.");
            Expr hostName = ReadValue();
            return new RunSshCommandStmt(target, hostName, ReadRunResultTarget(), start.Line);
        }
        string? resultTarget = ReadRunResultTarget();
        return isCommand ? new RunCommandStmt(target, resultTarget, start.Line) : new RunProgramStmt(target, resultTarget, start.Line);
    }

    // [into NAME], then the end of the run statement.
    string? ReadRunResultTarget()
    {
        string? resultTarget = null;
        if (Is(TokenKind.Into))
        {
            Read();
            resultTarget = ReadVariableName("I expected a variable name after \"into\".").Text;
        }
        Expect(TokenKind.Newline, "I expected the run statement to end here.");
        return resultTarget;
    }
}
