using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace Otter.Runtime;

// The PowerShell Remoting Protocol (MS-PSRP) messages a client sends and reads
// to run one script block on a remote PowerShell, as Invoke-Command does: the
// message and fragment layouts (MS-PSRP 2.2.1, 2.2.4) and the CLIXML (MS-PSRP
// 2.2.5) of the few messages involved. The transport is WinRM
// (RemoteCommands.cs); nothing here talks to a network.
public static class Psrp
{
    public const int DestinationClient = 1;
    public const int DestinationServer = 2;

    // MS-PSRP 2.2.1 message types.
    public const uint SessionCapability = 0x00010002;
    public const uint InitRunspacePool = 0x00010004;
    public const uint ApplicationPrivateData = 0x00021009;
    public const uint RunspacePoolState = 0x00021005;
    public const uint CreatePipeline = 0x00021006;
    public const uint PipelineOutput = 0x00041004;
    public const uint ErrorRecord = 0x00041005;
    public const uint PipelineState = 0x00041006;

    // RunspacePoolState and PSInvocationState values (MS-PSRP 2.2.3.4, 2.2.3.5).
    public const int RunspaceOpened = 2;
    public const int RunspaceBroken = 5;
    public const int PipelineCompleted = 4;
    public const int PipelineFailed = 5;
    public const int PipelineStopped = 3;

    // ---- messages and fragments ------------------------------------------------

    // A message: destination, type, runspace pool id, pipeline id (both as .NET
    // writes a Guid's bytes), then the CLIXML as UTF-8 with its byte order mark.
    public static byte[] Message(int destination, uint type, Guid runspacePool, Guid pipeline, string clixml)
    {
        byte[] data = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(clixml)).ToArray();
        var message = new byte[40 + data.Length];
        BinaryPrimitives.WriteInt32LittleEndian(message.AsSpan(0), destination);
        BinaryPrimitives.WriteUInt32LittleEndian(message.AsSpan(4), type);
        runspacePool.ToByteArray().CopyTo(message, 8);
        pipeline.ToByteArray().CopyTo(message, 24);
        data.CopyTo(message, 40);
        return message;
    }

    // One message as a single fragment (start and end): object id and fragment id
    // big-endian, the flags, the length big-endian, the message. Otter's messages
    // are a few kilobytes at most, far below WinRM's envelope size.
    public static byte[] Fragment(long objectId, byte[] message)
    {
        var fragment = new byte[21 + message.Length];
        BinaryPrimitives.WriteInt64BigEndian(fragment.AsSpan(0), objectId);
        BinaryPrimitives.WriteInt64BigEndian(fragment.AsSpan(8), 0);
        fragment[16] = 0x3;
        BinaryPrimitives.WriteInt32BigEndian(fragment.AsSpan(17), message.Length);
        message.CopyTo(fragment, 21);
        return fragment;
    }

    public readonly record struct Received(uint Type, Guid RunspacePool, Guid Pipeline, string Clixml);

    // Joins fragments (in any number of pieces of stream data) back into
    // messages, as they arrive.
    public sealed class Defragmenter
    {
        readonly List<byte> pending = new();
        readonly Dictionary<long, List<byte>> objects = new();

        public List<Received> Add(ReadOnlySpan<byte> data)
        {
            pending.AddRange(data.ToArray());
            var messages = new List<Received>();
            while (pending.Count >= 21)
            {
                byte[] head = pending.GetRange(0, 21).ToArray();
                long objectId = BinaryPrimitives.ReadInt64BigEndian(head);
                byte flags = head[16];
                int length = BinaryPrimitives.ReadInt32BigEndian(head.AsSpan(17));
                if (pending.Count < 21 + length) break;
                byte[] blob = pending.GetRange(21, length).ToArray();
                pending.RemoveRange(0, 21 + length);
                if ((flags & 0x1) != 0) objects[objectId] = new List<byte>();
                if (!objects.TryGetValue(objectId, out List<byte>? parts)) continue;
                parts.AddRange(blob);
                if ((flags & 0x2) == 0) continue;
                objects.Remove(objectId);
                messages.Add(Parse(parts.ToArray()));
            }
            return messages;
        }

        static Received Parse(byte[] message)
        {
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(message.AsSpan(4));
            var runspacePool = new Guid(message.AsSpan(8, 16));
            var pipeline = new Guid(message.AsSpan(24, 16));
            ReadOnlySpan<byte> data = message.AsSpan(40);
            if (data.StartsWith(Encoding.UTF8.GetPreamble())) data = data[3..];
            return new Received(type, runspacePool, pipeline, Encoding.UTF8.GetString(data));
        }
    }

    // ---- CLIXML -------------------------------------------------------------------

    // PowerShell's serializer: XML escaping, plus _xHHHH_ for characters XML
    // cannot carry or that it always encodes (control characters, and the "_x"
    // of a literal "_xHHHH_" so it reads back unchanged).
    public static string EncodeString(string text)
    {
        var result = new StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '_' && i + 1 < text.Length && text[i + 1] == 'x') result.Append("_x005F_");
            else if (c < 0x20 || c is '￾' or '￿' || char.IsSurrogate(c)) result.Append("_x").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture)).Append('_');
            else if (c == '&') result.Append("&amp;");
            else if (c == '<') result.Append("&lt;");
            else if (c == '>') result.Append("&gt;");
            else result.Append(c);
        }
        return result.ToString();
    }

    // The reverse, for text already taken out of the XML (entities resolved).
    public static string DecodeString(string text)
    {
        var result = new StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '_' && i + 6 < text.Length && text[i + 1] == 'x' && text[i + 6] == '_' &&
                int.TryParse(text.AsSpan(i + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
            {
                result.Append((char)code);
                i += 6;
            }
            else result.Append(text[i]);
        }
        return result.ToString();
    }

    const string HostInfo = "<MS><B N=\"_isHostNull\">true</B><B N=\"_isHostUINull\">true</B><B N=\"_isHostRawUINull\">true</B><B N=\"_useRunspaceHost\">true</B></MS>";

    static string Enum(string name, int refId, string typeName, int typeRef, bool firstUse, string shown, int value) =>
        $"<Obj N=\"{name}\" RefId=\"{refId}\">" +
        (firstUse ? $"<TN RefId=\"{typeRef}\"><T>{typeName}</T><T>System.Enum</T><T>System.ValueType</T><T>System.Object</T></TN>" : $"<TNRef RefId=\"{typeRef}\" />") +
        $"<ToString>{shown}</ToString><I32>{value}</I32></Obj>";

    // MS-PSRP 2.2.2.1: protocol 2.3, as Windows PowerShell 5.1 announces.
    public static string SessionCapabilityXml() =>
        "<Obj RefId=\"0\"><MS><Version N=\"protocolversion\">2.3</Version><Version N=\"PSVersion\">2.0</Version><Version N=\"SerializationVersion\">1.1.0.1</Version></MS></Obj>";

    // MS-PSRP 2.2.2.2: one runspace, no host (a script block that does not prompt).
    public static string InitRunspacePoolXml() =>
        "<Obj RefId=\"0\"><MS><I32 N=\"MinRunspaces\">1</I32><I32 N=\"MaxRunspaces\">1</I32>" +
        Enum("PSThreadOptions", 1, "System.Management.Automation.Runspaces.PSThreadOptions", 0, true, "Default", 0) +
        Enum("ApartmentState", 2, "System.Threading.ApartmentState", 1, true, "Unknown", 2) +
        $"<Obj N=\"HostInfo\" RefId=\"3\">{HostInfo}</Obj><Nil N=\"ApplicationArguments\" /></MS></Obj>";

    // MS-PSRP 2.2.2.10: the script block as one script command with its
    // positional arguments, no input, output merged as PowerShell sends it.
    public static string CreatePipelineXml(string script, IReadOnlyList<string> arguments)
    {
        const string resultTypes = "System.Management.Automation.Runspaces.PipelineResultTypes";
        var xml = new StringBuilder();
        xml.Append("<Obj RefId=\"0\"><MS><B N=\"NoInput\">true</B>");
        xml.Append(Enum("ApartmentState", 1, "System.Threading.ApartmentState", 0, true, "Unknown", 2));
        xml.Append(Enum("RemoteStreamOptions", 2, "System.Management.Automation.RemoteStreamOptions", 1, true, "0", 0));
        xml.Append("<B N=\"AddToHistory\">false</B>");
        xml.Append($"<Obj N=\"HostInfo\" RefId=\"3\">{HostInfo}</Obj>");
        xml.Append("<Obj N=\"PowerShell\" RefId=\"4\"><MS>");
        xml.Append("<Obj N=\"Cmds\" RefId=\"5\"><TN RefId=\"2\"><T>System.Collections.Generic.List`1[[System.Management.Automation.PSObject, System.Management.Automation, Version=3.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35]]</T><T>System.Object</T></TN><LST>");
        xml.Append("<Obj RefId=\"6\"><MS>");
        xml.Append("<S N=\"Cmd\">").Append(EncodeString(script)).Append("</S><B N=\"IsScript\">true</B><Nil N=\"UseLocalScope\" />");
        int refId = 7;
        bool first = true;
        foreach (string merge in new[] { "MergeMyResult", "MergeToResult", "MergePreviousResults", "MergeError", "MergeWarning", "MergeVerbose", "MergeDebug", "MergeInformation" })
        {
            xml.Append(Enum(merge, refId++, resultTypes, 3, first, "None", 0));
            first = false;
        }
        xml.Append($"<Obj N=\"Args\" RefId=\"{refId++}\"><TNRef RefId=\"2\" /><LST>");
        foreach (string argument in arguments)
        {
            xml.Append($"<Obj RefId=\"{refId++}\"><MS><Nil N=\"N\" /><S N=\"V\">").Append(EncodeString(argument)).Append("</S></MS></Obj>");
        }
        xml.Append("</LST></Obj></MS></Obj></LST></Obj>");
        xml.Append("<B N=\"IsNested\">false</B><Nil N=\"History\" /><B N=\"RedirectShellErrorOutputPipe\">true</B></MS></Obj>");
        xml.Append("<B N=\"IsNested\">false</B></MS></Obj>");
        return xml.ToString();
    }

    // ---- reading what the server sends ----------------------------------------------

    // A PIPELINE_OUTPUT object as PowerShell's [string] shows it: a string as
    // it is; anything else by its ToString.
    public static string OutputText(string clixml)
    {
        XElement root = XElement.Parse(clixml);
        if (root.Name.LocalName == "Nil") return "";
        if (root.Name.LocalName == "S") return DecodeString(root.Value);
        XElement? shown = root.Element("ToString");
        return shown != null ? DecodeString(shown.Value) : DecodeString(root.Value);
    }

    // The state number of a RUNSPACEPOOL_STATE or PIPELINE_STATE message, and
    // the message of its ExceptionAsErrorRecord when there is one.
    public static (int State, string? Error) State(string clixml, string stateName)
    {
        XElement root = XElement.Parse(clixml);
        XElement? members = root.Element("MS");
        int state = -1;
        string? error = null;
        if (members != null)
        {
            foreach (XElement member in members.Elements())
            {
                string? name = member.Attribute("N")?.Value;
                if (name == stateName && int.TryParse(member.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)) state = value;
                if (name == "ExceptionAsErrorRecord") error = ErrorText(member);
            }
        }
        return (state, error);
    }

    // An ERROR_RECORD message's text (its ToString: the exception's message).
    public static string ErrorRecordText(string clixml) => ErrorText(XElement.Parse(clixml));

    static string ErrorText(XElement record)
    {
        XElement? shown = record.Element("ToString");
        if (shown != null) return DecodeString(shown.Value);
        XElement? message = record.Descendants("S").FirstOrDefault(s => s.Attribute("N")?.Value == "Message");
        return message != null ? DecodeString(message.Value) : "";
    }
}
