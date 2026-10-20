using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Otter.Runtime;

public sealed class EventLogProblem(string message) : Exception(message);

// get event log entries from "System" up to 20 into entries (D79): a port of
// otterPS's Get-OtterEventLogEntries, which runs
//
//     Get-WinEvent -LogName $LogName -MaxEvents ([int]$MaxEntries) -ErrorAction Stop
//
// and keeps each event's ProviderName, LevelDisplayName, TimeCreated and
// Message. Get-WinEvent is System.Diagnostics.Eventing.Reader over the
// Windows Event Log API (wevtapi.dll); the same calls are made here:
//
// - the log name is matched against every channel (EvtOpenChannelEnum),
//   exactly or as a PowerShell wildcard, ignoring case; a debug or analytic
//   channel is skipped when matched by a wildcard and refused when named;
// - the events are read newest first (EvtQuery, reverse direction), one log
//   by its name, several through a structured query;
// - the level name and the message come from the provider's metadata
//   (EvtOpenPublisherMetadata, EvtFormatMessage); either is gone when the
//   provider or its message cannot be found, as EventLogRecord gives them.
//
// Every failure becomes the text otterPS shows after "I could not read the
// event log "NAME". "; "No events were found ..." is an empty list.
[SupportedOSPlatform("windows")]
public static unsafe partial class OtterEventLog
{
    const int EvtQueryChannelPath = 0x1;
    const int EvtQueryReverseDirection = 0x200;
    const int EvtQueryTolerateQueryErrors = 0x1000;
    const int EvtRenderContextSystem = 1;
    const int EvtRenderEventValues = 0;
    const int EvtFormatMessageEvent = 1;
    const int EvtFormatMessageLevel = 2;
    const int EvtChannelConfigType = 2;
    const int EvtSystemProviderName = 0;
    const int EvtSystemTimeCreated = 8;
    const uint EvtVarTypeString = 1;
    const uint EvtVarTypeUInt32 = 8;
    const uint EvtVarTypeFileTime = 17;
    const int EvtChannelTypeAnalytic = 2;
    const int EvtChannelTypeDebug = 3;

    const int ErrorFileNotFound = 2;
    const int ErrorPathNotFound = 3;
    const int ErrorAccessDenied = 5;
    const int ErrorInsufficientBuffer = 122;
    const int ErrorNoMoreItems = 259;
    const int ErrorCancelled = 1223;
    const int ErrorResourceLangNotFound = 1815;
    const int ErrorMuiFileNotFound = 15100;
    const int ErrorEvtPublisherMetadataNotFound = 15002;
    const int ErrorEvtChannelNotFound = 15007;
    const int ErrorEvtMessageNotFound = 15027;
    const int ErrorEvtMessageIdNotFound = 15028;
    const int ErrorEvtUnresolvedValueInsert = 15029;
    const int ErrorEvtMessageLocaleNotFound = 15033;

    [StructLayout(LayoutKind.Sequential)]
    struct EvtVariant
    {
        public ulong Value;
        public uint Count;
        public uint Type;
    }

    [LibraryImport("wevtapi.dll", EntryPoint = "EvtOpenChannelEnum", SetLastError = true)]
    private static partial IntPtr EvtOpenChannelEnum(IntPtr session, int flags);

    [LibraryImport("wevtapi.dll", EntryPoint = "EvtNextChannelPath", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EvtNextChannelPath(IntPtr channelEnum, int bufferSize, char* buffer, out int bufferUsed);

    [LibraryImport("wevtapi.dll", EntryPoint = "EvtOpenChannelConfig", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr EvtOpenChannelConfig(IntPtr session, string channelPath, int flags);

    [LibraryImport("wevtapi.dll", EntryPoint = "EvtGetChannelConfigProperty", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EvtGetChannelConfigProperty(IntPtr channelConfig, int propertyId, int flags, int bufferSize, void* buffer, out int bufferUsed);

    [LibraryImport("wevtapi.dll", EntryPoint = "EvtQuery", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr EvtQuery(IntPtr session, string? path, string query, int flags);

    [LibraryImport("wevtapi.dll", EntryPoint = "EvtNext", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EvtNext(IntPtr resultSet, int eventsSize, IntPtr* events, int timeout, int flags, out int returned);

    [LibraryImport("wevtapi.dll", EntryPoint = "EvtCreateRenderContext", SetLastError = true)]
    private static partial IntPtr EvtCreateRenderContext(int valuePathsCount, IntPtr valuePaths, int flags);

    [LibraryImport("wevtapi.dll", EntryPoint = "EvtRender", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EvtRender(IntPtr context, IntPtr fragment, int flags, int bufferSize, void* buffer, out int bufferUsed, out int propertyCount);

    [LibraryImport("wevtapi.dll", EntryPoint = "EvtOpenPublisherMetadata", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr EvtOpenPublisherMetadata(IntPtr session, string publisherId, string? logFilePath, int locale, int flags);

    [LibraryImport("wevtapi.dll", EntryPoint = "EvtFormatMessage", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EvtFormatMessage(IntPtr publisherMetadata, IntPtr evt, int messageId, int valueCount, IntPtr values, int flags, int bufferSize, char* buffer, out int bufferUsed);

    [LibraryImport("wevtapi.dll", EntryPoint = "EvtClose")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EvtClose(IntPtr handle);

    // The events of the log(s) LOGNAME matches, newest first, at most
    // MAXENTRIES of them (already a number).
    public static OtterList Read(string logName, double maxEntries)
    {
        var list = new OtterList();
        try
        {
            // [int]$MaxEntries, then -MaxEvents' [ValidateRange(1, Int64.MaxValue)].
            double rounded = Math.Round(maxEntries, MidpointRounding.ToEven);
            if (double.IsNaN(rounded) || rounded < int.MinValue || rounded > int.MaxValue)
            {
                throw new EventLogProblem($"Cannot convert value \"{Values.PowerShellText(maxEntries)}\" to type \"System.Int32\". Error: \"Value was either too large or too small for an Int32.\"");
            }
            int maxEvents = (int)rounded;
            if (maxEvents < 1)
            {
                throw new EventLogProblem($"Cannot validate argument on parameter 'MaxEvents'. The {maxEvents} argument is less than the minimum allowed range of 1. Supply an argument that is greater than or equal to 1 and then try the command again.");
            }
            List<string> logs = MatchingLogs(logName);
            ReadEvents(logs, maxEvents, list);
        }
        catch (EventLogProblem problem)
        {
            if (problem.Message.Contains("No events were found", StringComparison.OrdinalIgnoreCase)) return list;
            throw;
        }
        return list;
    }

    // Get-WinEvent's FindLogNamesMatchingWildcards for one pattern.
    static List<string> MatchingLogs(string pattern)
    {
        var matching = new List<string>();
        bool wildcard = ContainsWildcardCharacters(pattern);
        Func<string, bool> isMatch = WildcardMatcher(pattern);
        bool matched = false;
        foreach (string actual in LogNames())
        {
            if (!(!wildcard && string.Equals(pattern, actual, StringComparison.OrdinalIgnoreCase)) && !isMatch(actual)) continue;
            IntPtr config = EvtOpenChannelConfig(IntPtr.Zero, actual, 0);
            if (config == IntPtr.Zero)
            {
                int error = Marshal.GetLastPInvokeError();
                throw new EventLogProblem($"To access the '{actual}' log start PowerShell with elevated user rights.  Error: {ExceptionMessage(error)}");
            }
            try
            {
                int type = ChannelType(config);
                if (type is EvtChannelTypeDebug or EvtChannelTypeAnalytic)
                {
                    if (wildcard) continue;
                    throw new EventLogProblem($"The {actual} event log can be read only in the forward chronological order because it is an analytical or a debug log. To see events from the {actual} event log, use the Oldest parameter in the command.");
                }
            }
            finally { EvtClose(config); }
            string lower = actual.ToLowerInvariant();
            if (!matching.Contains(lower)) matching.Add(lower);
            matched = true;
        }
        if (!matched) throw new EventLogProblem($"There is not an event log on the localhost computer that matches \"{pattern}\".");
        return matching;
    }

    // EventLogSession.GetLogNames.
    static List<string> LogNames()
    {
        var names = new List<string>();
        IntPtr channels = EvtOpenChannelEnum(IntPtr.Zero, 0);
        if (channels == IntPtr.Zero) throw Failure(Marshal.GetLastPInvokeError());
        try
        {
            char[] buffer = new char[512];
            while (true)
            {
                int used;
                bool ok;
                fixed (char* chars = buffer) ok = EvtNextChannelPath(channels, buffer.Length, chars, out used);
                if (!ok)
                {
                    int error = Marshal.GetLastPInvokeError();
                    if (error == ErrorNoMoreItems) break;
                    if (error != ErrorInsufficientBuffer) throw Failure(error);
                    buffer = new char[used];
                    fixed (char* chars = buffer) ok = EvtNextChannelPath(channels, buffer.Length, chars, out used);
                    if (!ok) throw Failure(Marshal.GetLastPInvokeError());
                }
                names.Add(new string(buffer, 0, Math.Max(0, used - 1)));
            }
        }
        finally { EvtClose(channels); }
        return names;
    }

    // EventLogConfiguration.LogType.
    static int ChannelType(IntPtr config)
    {
        EvtVariant value;
        if (!EvtGetChannelConfigProperty(config, EvtChannelConfigType, 0, sizeof(EvtVariant), &value, out int used))
        {
            int error = Marshal.GetLastPInvokeError();
            if (error != ErrorInsufficientBuffer) throw Failure(error);
            byte* buffer = stackalloc byte[Math.Max(used, sizeof(EvtVariant))];
            if (!EvtGetChannelConfigProperty(config, EvtChannelConfigType, 0, used, buffer, out used)) throw Failure(Marshal.GetLastPInvokeError());
            value = *(EvtVariant*)buffer;
        }
        return value.Type == EvtVarTypeUInt32 ? (int)(uint)value.Value : 0;
    }

    // Get-WinEvent's ProcessGetLog and ReadEvents.
    static void ReadEvents(List<string> logs, int maxEvents, OtterList list)
    {
        string? path;
        string query;
        int flags = EvtQueryChannelPath | EvtQueryReverseDirection;
        if (logs.Count > 1)
        {
            if (logs.Count > 256)
            {
                throw new EventLogProblem($"Log count ({logs.Count}) is exceeded Windows Event Log API limit (256). Adjust filter to return less log names.");
            }
            var structured = new StringBuilder("<QueryList>");
            for (int id = 0; id < logs.Count; id++)
            {
                structured.Append(CultureInfo.InvariantCulture, $"<Query Id=\"{id}\" Path=\"{logs[id]}\"><Select Path=\"{logs[id]}\">*</Select></Query>");
            }
            structured.Append("</QueryList>");
            path = null;
            query = structured.ToString();
            flags |= EvtQueryTolerateQueryErrors;
        }
        else
        {
            path = logs[0];
            query = "*";
        }
        IntPtr results = EvtQuery(IntPtr.Zero, path, query, flags);
        if (results == IntPtr.Zero) throw Failure(Marshal.GetLastPInvokeError());
        IntPtr renderContext = IntPtr.Zero;
        var publishers = new Dictionary<string, IntPtr>(StringComparer.OrdinalIgnoreCase);
        try
        {
            renderContext = EvtCreateRenderContext(0, IntPtr.Zero, EvtRenderContextSystem);
            if (renderContext == IntPtr.Zero) throw Failure(Marshal.GetLastPInvokeError());
            int count = 0;
            while (true)
            {
                IntPtr evt;
                if (!EvtNext(results, 1, &evt, -1, 0, out int returned) || returned == 0)
                {
                    int error = Marshal.GetLastPInvokeError();
                    if (error == ErrorNoMoreItems || returned == 0 && error == 0) break;
                    throw Failure(error);
                }
                try
                {
                    if (count >= maxEvents) break;
                    list.Add(Entry(evt, renderContext, publishers));
                    count++;
                }
                finally { EvtClose(evt); }
            }
            if (count == 0) throw new EventLogProblem("No events were found that match the specified selection criteria.");
        }
        finally
        {
            foreach (IntPtr publisher in publishers.Values) if (publisher != IntPtr.Zero) EvtClose(publisher);
            if (renderContext != IntPtr.Zero) EvtClose(renderContext);
            EvtClose(results);
        }
    }

    static OtterThing Entry(IntPtr evt, IntPtr renderContext, Dictionary<string, IntPtr> publishers)
    {
        string? provider = null;
        DateTime? created = null;
        if (!EvtRender(renderContext, evt, EvtRenderEventValues, 0, null, out int used, out _))
        {
            int renderError = Marshal.GetLastPInvokeError();
            if (renderError != ErrorInsufficientBuffer) throw Failure(renderError);
        }
        byte[] buffer = new byte[Math.Max(used, 1)];
        fixed (byte* bytes = buffer)
        {
            if (!EvtRender(renderContext, evt, EvtRenderEventValues, buffer.Length, bytes, out used, out int properties)) throw Failure(Marshal.GetLastPInvokeError());
            var values = (EvtVariant*)bytes;
            if (properties > EvtSystemProviderName && values[EvtSystemProviderName].Type == EvtVarTypeString && values[EvtSystemProviderName].Value != 0)
            {
                provider = new string((char*)(nint)values[EvtSystemProviderName].Value);
            }
            if (properties > EvtSystemTimeCreated && values[EvtSystemTimeCreated].Type == EvtVarTypeFileTime)
            {
                created = DateTime.FromFileTime((long)values[EvtSystemTimeCreated].Value);
            }
        }
        IntPtr publisher = provider == null ? IntPtr.Zero : Publisher(provider, publishers);
        string? level = publisher == IntPtr.Zero ? null : FormatMessage(publisher, evt, EvtFormatMessageLevel);
        string? message = publisher == IntPtr.Zero ? null : FormatMessage(publisher, evt, EvtFormatMessageEvent);

        var entry = new OtterThing("event log entry");
        entry.WriteProperty("source", provider);
        entry.WriteProperty("level", level);
        entry.WriteProperty("time", created?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture));
        entry.WriteProperty("message", message);
        return entry;
    }

    // ProviderMetadataCachedInformation: a provider whose metadata is not
    // found has no level name or message.
    static IntPtr Publisher(string provider, Dictionary<string, IntPtr> publishers)
    {
        if (publishers.TryGetValue(provider, out IntPtr cached)) return cached;
        IntPtr handle = EvtOpenPublisherMetadata(IntPtr.Zero, provider, null, CultureInfo.CurrentCulture.LCID == CultureInfo.InvariantCulture.LCID ? 0 : CultureInfo.CurrentCulture.LCID, 0);
        if (handle == IntPtr.Zero)
        {
            int error = Marshal.GetLastPInvokeError();
            if (!IsNotFound(error)) throw Failure(error);
        }
        publishers[provider] = handle;
        return handle;
    }

    // NativeWrapper.EvtFormatMessageRenderName, whose not-found failures
    // (EventLogNotFoundException: a source whose message file is missing
    // reports ERROR_FILE_NOT_FOUND here) ProviderMetadataCachedInformation
    // turns into no level name and no message. Measured on a Windows CI runner
    // (tests/windows-state/w5_event_log.ot).
    static string? FormatMessage(IntPtr publisher, IntPtr evt, int flag)
    {
        bool ok = EvtFormatMessage(publisher, evt, 0, 0, IntPtr.Zero, flag, 0, null, out int needed);
        int error = Marshal.GetLastPInvokeError();
        if (!ok && error != ErrorEvtUnresolvedValueInsert)
        {
            if (IsMessageMissing(error) || IsNotFound(error)) return null;
            if (error != ErrorInsufficientBuffer) throw Failure(error);
        }
        char[] buffer = new char[Math.Max(needed, 1)];
        fixed (char* chars = buffer)
        {
            ok = EvtFormatMessage(publisher, evt, 0, 0, IntPtr.Zero, flag, buffer.Length, chars, out needed);
        }
        error = Marshal.GetLastPInvokeError();
        if (!ok && error != ErrorEvtUnresolvedValueInsert)
        {
            if (IsMessageMissing(error) || IsNotFound(error)) return null;
            throw Failure(error);
        }
        int length = Array.IndexOf(buffer, '\0');
        return new string(buffer, 0, length < 0 ? buffer.Length : length);
    }

    static bool IsMessageMissing(int error) => error is ErrorEvtMessageNotFound or ErrorEvtMessageIdNotFound or ErrorEvtMessageLocaleNotFound
        or ErrorResourceLangNotFound or ErrorMuiFileNotFound;

    // EventLogException.Throw's EventLogNotFoundException cases.
    static bool IsNotFound(int error) => error is ErrorFileNotFound or ErrorPathNotFound or ErrorEvtPublisherMetadataNotFound
        or ErrorEvtChannelNotFound or ErrorEvtMessageNotFound or ErrorEvtMessageIdNotFound;

    static EventLogProblem Failure(int error) => new(ExceptionMessage(error));

    // The message of the exception EventLogException.Throw raises.
    static string ExceptionMessage(int error) => error switch
    {
        ErrorAccessDenied => "Attempted to perform an unauthorized operation.",
        ErrorCancelled => "The operation was canceled.",
        _ => new Win32Exception(error).Message,
    };

    // PowerShell's WildcardPattern.ContainsWildcardCharacters.
    static bool ContainsWildcardCharacters(string pattern)
    {
        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];
            if (c is '*' or '?' or '[' or ']') return true;
            if (c == '`') i++;
        }
        return false;
    }

    // PowerShell's WildcardPattern (IgnoreCase): * ? [set] [a-z], ` escapes.
    static Func<string, bool> WildcardMatcher(string pattern)
    {
        var regex = new StringBuilder("^");
        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];
            if (c == '`' && i + 1 < pattern.Length) { regex.Append(System.Text.RegularExpressions.Regex.Escape(pattern[++i].ToString())); continue; }
            if (c == '*') { regex.Append(".*"); continue; }
            if (c == '?') { regex.Append('.'); continue; }
            if (c == '[')
            {
                int close = pattern.IndexOf(']', i + 1);
                if (close < 0) throw new EventLogProblem($"The specified wildcard character pattern is not valid: {pattern}");
                regex.Append('[');
                for (int j = i + 1; j < close; j++)
                {
                    char s = pattern[j];
                    if (s == '`' && j + 1 < close) s = pattern[++j];
                    else if (s == '-' && j > i + 1 && j + 1 < close) { regex.Append('-'); continue; }
                    regex.Append(s is '\\' or ']' or '[' or '^' or '-' ? "\\" + s : s.ToString());
                }
                regex.Append(']');
                i = close;
                continue;
            }
            regex.Append(System.Text.RegularExpressions.Regex.Escape(c.ToString()));
        }
        regex.Append('$');
        var compiled = new System.Text.RegularExpressions.Regex(regex.ToString(),
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.Singleline);
        return compiled.IsMatch;
    }
}
