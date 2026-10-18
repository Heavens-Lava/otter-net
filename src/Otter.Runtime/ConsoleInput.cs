namespace Otter.Runtime;

// Read-Host with redirected input, as PowerShell's console host
// (ReadLineFromFile) does it: one character at a time and, when an echo
// writer is given, each one written back as it is read - the line break
// too, but only if the input has one: a last line without "\n" is echoed
// without it. CR LF is one line break. null at the end of the input.
// Used by `ask`/`choose` and by the REPL.
public static class ConsoleInput
{
    public static string? ReadLine(TextReader input, TextWriter? echo)
    {
        var line = new System.Text.StringBuilder();
        while (true)
        {
            int read = input.Read();
            if (read == -1) return line.Length == 0 ? null : line.ToString();
            char c = (char)read;
            echo?.Write(c);
            if (c == '\r')
            {
                if (input.Peek() == '\n')
                {
                    input.Read();
                    echo?.Write('\n');
                }
                return line.ToString();
            }
            if (c == '\n') return line.ToString();
            line.Append(c);
        }
    }
}
