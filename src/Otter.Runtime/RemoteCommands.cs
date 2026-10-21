using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;

namespace Otter.Runtime;

// run command "..." on remote "host" using credential "n" (D84), on Windows.
//
// otterPS's Invoke-OtterRemoteCommand runs
//     Invoke-Command -ComputerName $HostName -Credential $cred -ScriptBlock {...} -ArgumentList $Command
// i.e. PowerShell remoting: the PowerShell Remoting Protocol (Psrp.cs) carried
// by WinRM to the remote machine's Microsoft.PowerShell endpoint, which runs
// the script block. otter-net does the same through Windows' own WinRM client
// (WsmSvc.dll, the library Invoke-Command's transport uses): the same endpoint
// (http://host:5985/wsman), Negotiate authentication with the stored
// credential, WinRM's message encryption (on by default; nothing here turns it
// off), and the identical script block - so the remote machine does exactly
// what it does for otterPS. Nothing else is added.
public sealed class RemoteCommandProblem(string message) : Exception(message);

public static unsafe partial class OtterRemoteCommands
{
    // The script block of Invoke-OtterRemoteCommand (src/Otter.Library.psm1),
    // as Invoke-Command sends a script block: its text between the braces.
    internal const string Script = "\n" +
        "            param($cmd)\n" +
        "            $parts = $cmd -split '\\s+', 2\n" +
        "            $exe = $parts[0]\n" +
        "            $args = if ($parts.Count -gt 1) { $parts[1] } else { '' }\n" +
        "            if ($args) {\n" +
        "                & $exe $args 2>&1 | Out-String\n" +
        "            } else {\n" +
        "                & $exe 2>&1 | Out-String\n" +
        "            }\n" +
        "        ";

    const string ResourceUri = "http://schemas.microsoft.com/powershell/Microsoft.PowerShell";
    const string CommandStateDone = "http://schemas.microsoft.com/wbem/wsman/1/windows/shell/CommandState/Done";

    // The remote Out-String text, as otterPS returns [string]$result. Failures
    // throw RemoteCommandProblem with the message PowerShell's exception has
    // (otterPS adds its own "I could not run that command on ..." around it).
    public static string Run(string command, string hostName, string userName, string password)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("WinRM");
        var runspacePool = Guid.NewGuid();
        var pipeline = Guid.NewGuid();
        long objectId = 1;
        byte[] creation = Psrp.Fragment(objectId++, Psrp.Message(Psrp.DestinationServer, Psrp.SessionCapability, runspacePool, Guid.Empty, Psrp.SessionCapabilityXml()))
            .Concat(Psrp.Fragment(objectId++, Psrp.Message(Psrp.DestinationServer, Psrp.InitRunspacePool, runspacePool, Guid.Empty, Psrp.InitRunspacePoolXml()))).ToArray();
        byte[] createPipeline = Psrp.Fragment(objectId++, Psrp.Message(Psrp.DestinationServer, Psrp.CreatePipeline, runspacePool, pipeline, Psrp.CreatePipelineXml(Script, new[] { command })));

        using var strings = new NativeStrings();
        IntPtr api = IntPtr.Zero, session = IntPtr.Zero, shell = IntPtr.Zero, commandHandle = IntPtr.Zero;
        try
        {
            Check(WSManInitialize(WSMAN_FLAG_REQUESTED_API_VERSION_1_1, out api), hostName);
            var credentials = new WSMAN_AUTHENTICATION_CREDENTIALS
            {
                authenticationMechanism = WSMAN_FLAG_AUTH_NEGOTIATE,
                username = strings.Add(userName),
                password = strings.Add(password),
            };
            Check(WSManCreateSession(api, strings.Add(ConnectionUri(hostName)), 0, &credentials, IntPtr.Zero, out session), hostName);

            // The shell is the runspace pool: its creation carries the first two
            // messages, and its id is the pool's id.
            IntPtr streamIn = strings.Add("stdin pr");
            IntPtr streamOut = strings.Add("stdout");
            var inputSet = new WSMAN_STREAM_ID_SET { streamIDsCount = 1, streamIDs = (IntPtr)(&streamIn) };
            var outputSet = new WSMAN_STREAM_ID_SET { streamIDsCount = 1, streamIDs = (IntPtr)(&streamOut) };
            var startup = new WSMAN_SHELL_STARTUP_INFO { inputStreamSet = (IntPtr)(&inputSet), outputStreamSet = (IntPtr)(&outputSet) };
            var protocolOption = new WSMAN_OPTION { name = strings.Add("protocolversion"), value = strings.Add("2.3"), mustComply = 1 };
            var options = new WSMAN_OPTION_SET { optionsCount = 1, options = (IntPtr)(&protocolOption) };
            string creationXml = "<creationXml xmlns=\"http://schemas.microsoft.com/powershell\">" + Convert.ToBase64String(creation) + "</creationXml>";
            var createData = WSMAN_DATA.Text(strings.Add(creationXml), creationXml.Length);
            using (var creating = new Operation())
            {
                var async = creating.Async;
                WSManCreateShellEx(session, 0, strings.Add(ResourceUri), strings.Add(runspacePool.ToString().ToUpperInvariant()), &startup, &options, &createData, &async, out shell);
                creating.Wait();
                creating.ThrowIfFailed(hostName, connecting: true);
            }

            // The pool opens (or not): RUNSPACEPOOL_STATE on the shell's output.
            var poolMessages = new Psrp.Defragmenter();
            bool opened = false;
            Receive(shell, IntPtr.Zero, outputSet, hostName, data =>
            {
                foreach (Psrp.Received message in poolMessages.Add(data))
                {
                    if (message.Type != Psrp.RunspacePoolState) continue;
                    (int state, string? error) = Psrp.State(message.Clixml, "RunspaceState");
                    if (state == Psrp.RunspaceOpened) { opened = true; return false; }
                    if (state == Psrp.RunspaceBroken) throw new RemoteCommandProblem(error ?? $"The remote session to {hostName} could not be opened.");
                }
                return true;
            });
            if (!opened) throw new RemoteCommandProblem($"The remote session to {hostName} could not be opened.");

            // The pipeline is a command of the shell: CREATE_PIPELINE as its argument.
            IntPtr argument = strings.Add(Convert.ToBase64String(createPipeline));
            var arguments = new WSMAN_COMMAND_ARG_SET { argsCount = 1, args = (IntPtr)(&argument) };
            using (var running = new Operation())
            {
                var async = running.Async;
                WSManRunShellCommandEx(shell, 0, strings.Add(pipeline.ToString().ToUpperInvariant()), strings.Add(" "), &arguments, null, &async, out commandHandle);
                running.Wait();
                running.ThrowIfFailed(hostName, connecting: false);
            }

            var pipelineMessages = new Psrp.Defragmenter();
            var output = new StringBuilder();
            string? failure = null;
            Receive(shell, commandHandle, outputSet, hostName, data =>
            {
                foreach (Psrp.Received message in pipelineMessages.Add(data))
                {
                    if (message.Type == Psrp.PipelineOutput) output.Append(Psrp.OutputText(message.Clixml));
                    else if (message.Type == Psrp.PipelineState)
                    {
                        (int state, string? error) = Psrp.State(message.Clixml, "PipelineState");
                        if (state is Psrp.PipelineFailed or Psrp.PipelineStopped) failure = error ?? "The pipeline has been stopped.";
                        return false;
                    }
                }
                return true;
            });
            if (failure != null) throw new RemoteCommandProblem(failure);
            return output.ToString();
        }
        finally
        {
            if (commandHandle != IntPtr.Zero) using (var closing = new Operation()) { var async = closing.Async; WSManCloseCommand(commandHandle, 0, &async); closing.Wait(); }
            if (shell != IntPtr.Zero) using (var closing = new Operation()) { var async = closing.Async; WSManCloseShell(shell, 0, &async); closing.Wait(); }
            if (session != IntPtr.Zero) WSManCloseSession(session, 0);
            if (api != IntPtr.Zero) WSManDeinitialize(api, 0);
        }
    }

    // WSManConnectionInfo's default: HTTP, port 5985, application "wsman"; an
    // IPv6 address in brackets.
    internal static string ConnectionUri(string hostName)
    {
        string host = hostName.Contains(':') && !hostName.StartsWith('[') ? $"[{hostName}]" : hostName;
        return $"http://{host}:5985/wsman";
    }

    // Reads the stream data of the shell (command zero) or of a command until
    // `more` returns false or the command is done. A receive that is still
    // running then is cancelled, and its final callback awaited, before its
    // context goes away.
    static void Receive(IntPtr shell, IntPtr command, WSMAN_STREAM_ID_SET streams, string hostName, Func<byte[], bool> more)
    {
        while (true)
        {
            var receiving = new Operation(receive: true);
            var async = receiving.Async;
            WSManReceiveShellOutput(shell, command, 0, &streams, &async, out IntPtr operation);
            bool keepGoing = true;
            bool done = false;
            try
            {
                foreach (Operation.Chunk chunk in receiving.Chunks())
                {
                    if (chunk.Data.Length > 0) keepGoing = more(chunk.Data);
                    if (chunk.CommandState == CommandStateDone) done = true;
                    if (!keepGoing || done) break;
                }
                if (keepGoing && !done) receiving.ThrowIfFailed(hostName, connecting: false);
            }
            finally
            {
                bool cancelled = !receiving.Finished;
                if (operation != IntPtr.Zero) WSManCloseOperation(operation, 0);
                // Cancelling ends with one more callback (ERROR_OPERATION_ABORTED).
                if (cancelled) receiving.Wait(TimeSpan.FromSeconds(30));
                receiving.Dispose();
            }
            if (!keepGoing || done) return;
        }
    }

    static void Check(uint error, string hostName)
    {
        if (error != 0) throw new RemoteCommandProblem(ConnectMessage(hostName, $"WinRM error {error}."));
    }

    // PowerShell 5.1's PSRemotingTransportException for a connection that
    // fails (RemotingErrorIdStrings ConnectExCallBackError plus the help topic).
    internal static string ConnectMessage(string hostName, string detail) =>
        $"Connecting to remote server {hostName} failed with the following error message : {detail} For more information, see the about_Remote_Troubleshooting Help topic.";

    // WinRM's error detail is either text or a WS-Management fault whose
    // f:Message is the text PowerShell shows.
    internal static string FaultText(string detail)
    {
        string trimmed = detail.Trim();
        if (!trimmed.StartsWith('<')) return trimmed;
        try
        {
            XElement fault = XElement.Parse(trimmed);
            XElement? message = fault.Descendants().FirstOrDefault(e => e.Name.LocalName == "Message");
            return (message?.Value ?? trimmed).Trim();
        }
        catch (System.Xml.XmlException) { return trimmed; }
    }

    // ---- the asynchronous WinRM calls ---------------------------------------------------

    // One WinRM operation: its callback context, the error it ended with, and
    // the stream data it delivered.
    sealed class Operation : IDisposable
    {
        public readonly record struct Chunk(byte[] Data, string? CommandState);

        readonly object gate = new();
        readonly Queue<Chunk> chunks = new();
        GCHandle self;
        bool finished;
        uint errorCode;
        string? errorDetail;

        // A receive delivers stream data in any number of callbacks, the last
        // with END_OF_OPERATION; creating, running and closing complete with
        // one callback whose data is not stream data.
        readonly bool receive;

        public Operation(bool receive = false)
        {
            this.receive = receive;
            self = GCHandle.Alloc(this);
        }

        public WSMAN_SHELL_ASYNC Async => new() { operationContext = GCHandle.ToIntPtr(self), completionFunction = (IntPtr)(delegate* unmanaged<IntPtr, uint, WSMAN_ERROR*, IntPtr, IntPtr, IntPtr, WSMAN_RESPONSE_DATA*, void>)&Completed };

        public bool Finished { get { lock (gate) return finished; } }

        public void Wait()
        {
            lock (gate) { while (!finished) Monitor.Wait(gate); }
        }

        public void Wait(TimeSpan limit)
        {
            DateTime end = DateTime.UtcNow + limit;
            lock (gate)
            {
                while (!finished)
                {
                    TimeSpan left = end - DateTime.UtcNow;
                    if (left <= TimeSpan.Zero || !Monitor.Wait(gate, left)) return;
                }
            }
        }

        public IEnumerable<Chunk> Chunks()
        {
            while (true)
            {
                Chunk chunk;
                lock (gate)
                {
                    while (chunks.Count == 0 && !finished) Monitor.Wait(gate);
                    if (chunks.Count == 0) yield break;
                    chunk = chunks.Dequeue();
                }
                yield return chunk;
            }
        }

        public void ThrowIfFailed(string hostName, bool connecting)
        {
            if (errorCode == 0) return;
            string detail = FaultText(errorDetail ?? $"WinRM error {errorCode}.");
            throw new RemoteCommandProblem(connecting ? ConnectMessage(hostName, detail) : detail);
        }

        [UnmanagedCallersOnly]
        static void Completed(IntPtr context, uint flags, WSMAN_ERROR* error, IntPtr shell, IntPtr command, IntPtr operation, WSMAN_RESPONSE_DATA* data)
        {
            var self = (Operation)GCHandle.FromIntPtr(context).Target!;
            lock (self.gate)
            {
                if (error != null && error->code != 0)
                {
                    self.errorCode = error->code;
                    self.errorDetail = Marshal.PtrToStringUni(error->errorDetail);
                }
                bool failed = error != null && error->code != 0;
                if (self.receive && !failed && data != null)
                {
                    byte[] bytes = data->streamData.Bytes();
                    self.chunks.Enqueue(new Chunk(bytes, Marshal.PtrToStringUni(data->commandState)));
                }
                if (!self.receive || failed || (flags & WSMAN_FLAG_CALLBACK_END_OF_OPERATION) != 0)
                {
                    self.finished = true;
                }
                Monitor.PulseAll(self.gate);
            }
        }

        // The context is freed only once WinRM has made its last callback: a
        // callback into a freed handle would crash the process. An operation
        // that never finished keeps its (small) context for the process's life.
        public void Dispose()
        {
            if (Finished && self.IsAllocated) self.Free();
        }
    }

    // Unicode strings that must outlive the asynchronous calls using them.
    sealed class NativeStrings : IDisposable
    {
        readonly List<IntPtr> all = new();
        public IntPtr Add(string text) { IntPtr p = Marshal.StringToHGlobalUni(text); all.Add(p); return p; }
        public void Dispose() { foreach (IntPtr p in all) Marshal.FreeHGlobal(p); }
    }

    // ---- wsman.h --------------------------------------------------------------------------

    const uint WSMAN_FLAG_REQUESTED_API_VERSION_1_1 = 0x1;
    const uint WSMAN_FLAG_AUTH_NEGOTIATE = 0x4;
    const uint WSMAN_FLAG_CALLBACK_END_OF_OPERATION = 0x1;
    const int WSMAN_DATA_TYPE_TEXT = 1;
    const int WSMAN_DATA_TYPE_BINARY = 2;

    [StructLayout(LayoutKind.Sequential)]
    struct WSMAN_AUTHENTICATION_CREDENTIALS
    {
        public uint authenticationMechanism;
        public IntPtr username;   // union: WSMAN_USERNAME_PASSWORD_CREDS
        public IntPtr password;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WSMAN_STREAM_ID_SET { public uint streamIDsCount; public IntPtr streamIDs; }

    // WSMAN_SHELL_STARTUP_INFO_V11 - the layout WsmSvc.dll reads when the API
    // is initialized for version 1.1 (its last field, the shell's name, was
    // missing: WinRM read past the structure and the process crashed with an
    // access violation, found on a real WinRM endpoint in CI).
    [StructLayout(LayoutKind.Sequential)]
    struct WSMAN_SHELL_STARTUP_INFO
    {
        public IntPtr inputStreamSet;
        public IntPtr outputStreamSet;
        public uint idleTimeoutMs;
        public IntPtr workingDirectory;
        public IntPtr variableSet;
        public IntPtr name;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WSMAN_OPTION { public IntPtr name; public IntPtr value; public int mustComply; }

    [StructLayout(LayoutKind.Sequential)]
    struct WSMAN_OPTION_SET { public uint optionsCount; public IntPtr options; public int optionsMustUnderstand; }

    [StructLayout(LayoutKind.Sequential)]
    struct WSMAN_COMMAND_ARG_SET { public uint argsCount; public IntPtr args; }

    // WSMAN_DATA: a type, then a union whose members are a length followed by
    // a pointer (text: characters; binary: bytes). The union is 8-byte
    // aligned on 64-bit Windows (x64, Arm64 - the runtimes otter-net ships):
    // the length is at 8 and the pointer at 16.
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    struct WSMAN_DATA
    {
        [FieldOffset(0)] public int type;
        [FieldOffset(8)] public uint length;
        [FieldOffset(16)] public IntPtr pointer;

        public static WSMAN_DATA Text(IntPtr text, int characters) => new() { type = WSMAN_DATA_TYPE_TEXT, length = (uint)characters, pointer = text };

        public readonly byte[] Bytes()
        {
            if (pointer == IntPtr.Zero || length == 0) return Array.Empty<byte>();
            if (type == WSMAN_DATA_TYPE_BINARY)
            {
                var bytes = new byte[length];
                Marshal.Copy(pointer, bytes, 0, (int)length);
                return bytes;
            }
            // Text stream data is base64.
            string text = Marshal.PtrToStringUni(pointer, (int)length);
            try { return Convert.FromBase64String(text); }
            catch (FormatException) { return Encoding.UTF8.GetBytes(text); }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WSMAN_ERROR
    {
        public uint code;
        public IntPtr errorDetail;
        public IntPtr language;
        public IntPtr machineName;
        public IntPtr pluginName;
    }

    // WSMAN_RESPONSE_DATA's receiveData member (WSMAN_RECEIVE_DATA_RESULT).
    [StructLayout(LayoutKind.Sequential)]
    struct WSMAN_RESPONSE_DATA
    {
        public IntPtr streamId;
        public WSMAN_DATA streamData;
        public IntPtr commandState;
        public uint exitCode;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WSMAN_SHELL_ASYNC { public IntPtr operationContext; public IntPtr completionFunction; }

    const string WsmSvc = "WsmSvc.dll";

    [LibraryImport(WsmSvc)] private static partial uint WSManInitialize(uint flags, out IntPtr apiHandle);
    [LibraryImport(WsmSvc)] private static partial uint WSManDeinitialize(IntPtr apiHandle, uint flags);
    [LibraryImport(WsmSvc)] private static partial uint WSManCreateSession(IntPtr apiHandle, IntPtr connection, uint flags, WSMAN_AUTHENTICATION_CREDENTIALS* credentials, IntPtr proxyInfo, out IntPtr session);
    [LibraryImport(WsmSvc)] private static partial uint WSManCloseSession(IntPtr session, uint flags);
    [LibraryImport(WsmSvc)] private static partial void WSManCreateShellEx(IntPtr session, uint flags, IntPtr resourceUri, IntPtr shellId, WSMAN_SHELL_STARTUP_INFO* startupInfo, WSMAN_OPTION_SET* options, WSMAN_DATA* createXml, WSMAN_SHELL_ASYNC* async, out IntPtr shell);
    [LibraryImport(WsmSvc)] private static partial void WSManRunShellCommandEx(IntPtr shell, uint flags, IntPtr commandId, IntPtr commandLine, WSMAN_COMMAND_ARG_SET* args, WSMAN_OPTION_SET* options, WSMAN_SHELL_ASYNC* async, out IntPtr command);
    [LibraryImport(WsmSvc)] private static partial void WSManReceiveShellOutput(IntPtr shell, IntPtr command, uint flags, WSMAN_STREAM_ID_SET* desiredStreamSet, WSMAN_SHELL_ASYNC* async, out IntPtr receiveOperation);
    [LibraryImport(WsmSvc)] private static partial uint WSManCloseOperation(IntPtr operation, uint flags);
    [LibraryImport(WsmSvc)] private static partial void WSManCloseCommand(IntPtr command, uint flags, WSMAN_SHELL_ASYNC* async);
    [LibraryImport(WsmSvc)] private static partial void WSManCloseShell(IntPtr shell, uint flags, WSMAN_SHELL_ASYNC* async);
}
