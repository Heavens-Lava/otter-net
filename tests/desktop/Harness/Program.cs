using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;

namespace Otter.DesktopHarness;

// Runs every test program in tests/desktop with otterPS 1.0.1 and with
// otter-net, drives the windows through UI Automation with the program's
// .steps script, and compares, side by side: what each window shows (the
// automation tree after each step, and the pixels of each snapshot), what
// was printed (standard output byte for byte, standard error), and the exit
// code. Any difference fails. See tests/desktop/README.md.
//
//   desktop-harness --otterps otterps/otter.ps1 --otter artifacts/win-x64/otter.exe
//                   --tests tests/desktop --out results/desktop [--summary file.md] [--only name]
internal static class Program
{
    static string otterPs = "", otterNet = "", testsDir = "", outDir = "";
    static string? summaryPath, only, referenceUiCulture;

    static int Main(string[] args)
    {
        for (int i = 0; i + 1 < args.Length; i += 2)
        {
            switch (args[i])
            {
                case "--otterps": otterPs = Path.GetFullPath(args[i + 1]); break;
                case "--otter": otterNet = Path.GetFullPath(args[i + 1]); break;
                case "--tests": testsDir = Path.GetFullPath(args[i + 1]); break;
                case "--out": outDir = Path.GetFullPath(args[i + 1]); break;
                case "--summary": summaryPath = Path.GetFullPath(args[i + 1]); break;
                case "--only": only = args[i + 1]; break;
                // Runs otterPS with this UI culture (its thread's
                // CurrentUICulture and CurrentCulture), as on a Windows
                // whose display language it is; otter-net reads the
                // system's. For measuring, not for the comparison in CI.
                case "--reference-ui-culture": referenceUiCulture = args[i + 1]; break;
            }
        }
        if (otterPs == "" || otterNet == "" || testsDir == "" || outDir == "")
        {
            Console.Error.WriteLine("usage: desktop-harness --otterps otter.ps1 --otter otter.exe --tests dir --out dir [--summary file.md] [--only name] [--reference-ui-culture de-DE]");
            return 2;
        }
        Directory.CreateDirectory(outDir);
        // A steady caret: snapshots of a focused text box do not depend on
        // when the caret blinks.
        Native.SetCaretBlinkTime(uint.MaxValue);

        var tests = Directory.GetFiles(testsDir, "*.ot").OrderBy(f => f, StringComparer.Ordinal)
            .Where(f => only == null || Path.GetFileNameWithoutExtension(f) == only).ToList();
        var rows = new List<(string Test, bool Same, string Notes)>();
        var details = new StringBuilder();
        foreach (string test in tests)
        {
            string name = Path.GetFileNameWithoutExtension(test);
            Console.WriteLine($"== {name}");
            List<string> steps = ReadSteps(test);
            SideResult reference = RunSide("otterps", test, steps);
            SideResult candidate = RunSide("otter-net", test, steps);
            var differences = Compare(name, reference, candidate);
            string notes = differences.Count == 0
                ? $"{reference.Observations.Count} steps, {reference.Images.Count} snapshots, exit {reference.Exit}" + (nearlySame > 0 ? $"; {nearlySame} pixels within 2/255" : "")
                : differences[0];
            if (!candidate.RanInDesktopHost && candidate.CreatesWindows)
            {
                differences.Add("otter-net did not run the program in otter-desktop (no `otter-net desktop:` trace)");
                notes = differences[^1];
            }
            rows.Add((name, differences.Count == 0, notes));
            WriteTranscript(name, "otterps", reference);
            WriteTranscript(name, "otter-net", candidate);
            if (differences.Count > 0)
            {
                details.AppendLine($"### {name}").AppendLine();
                foreach (string difference in differences) details.AppendLine($"- {difference.Replace("\n", "\n  ")}");
                details.AppendLine();
                Console.WriteLine($"   DIFFERENT: {differences[0]}");
            }
            else
            {
                Console.WriteLine("   same");
            }
        }

        var summary = new StringBuilder();
        int same = rows.Count(r => r.Same);
        summary.AppendLine("# Desktop windows: otter-net against otterPS 1.0.1").AppendLine();
        summary.AppendLine($"{same} of {rows.Count} test programs identical (window tree after every step, snapshot pixels, standard output byte for byte, standard error, exit code).").AppendLine();
        summary.AppendLine("| Test | Result | Notes |").AppendLine("|---|---|---|");
        foreach (var row in rows) summary.AppendLine($"| `{row.Test}` | {(row.Same ? "same" : "**different**")} | {Cell(row.Notes)} |");
        summary.AppendLine();
        if (details.Length > 0) summary.AppendLine("## Differences").AppendLine().Append(details);
        File.WriteAllText(Path.Combine(outDir, "summary.md"), summary.ToString());
        if (summaryPath != null) File.AppendAllText(summaryPath, summary.ToString());
        Console.WriteLine(summary.ToString());
        return same == rows.Count && rows.Count > 0 ? 0 : 1;
    }

    static string Cell(string text)
    {
        string flat = text.Replace("\r", "").Replace("\n", " / ").Replace("|", "\\|");
        return flat.Length > 160 ? flat[..157] + "..." : flat;
    }

    // ---- steps ----

    static List<string> ReadSteps(string test)
    {
        string path = Path.ChangeExtension(test, ".steps");
        if (!File.Exists(path)) return new() { "window *", "snap", "close", "exit" };
        return File.ReadAllLines(path).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();
    }

    static List<string> Tokens(string line)
    {
        var tokens = new List<string>();
        int i = 0;
        while (i < line.Length)
        {
            if (char.IsWhiteSpace(line[i])) { i++; continue; }
            if (line[i] == '"')
            {
                var text = new StringBuilder();
                i++;
                while (i < line.Length && line[i] != '"')
                {
                    if (line[i] == '\\' && i + 1 < line.Length) { i++; text.Append(line[i] == 'n' ? '\n' : line[i]); }
                    else text.Append(line[i]);
                    i++;
                }
                i++;
                tokens.Add("\"" + text);
                continue;
            }
            int start = i;
            while (i < line.Length && !char.IsWhiteSpace(line[i])) i++;
            tokens.Add(line[start..i]);
        }
        return tokens;
    }

    // ---- one side ----

    sealed class SideResult
    {
        public List<string> Observations = new();
        public List<(int Step, int Width, int Height, byte[] Pixels)> Images = new();
        public byte[] Stdout = Array.Empty<byte>();
        public string Stderr = "";
        public string Exit = "";
        public bool RanInDesktopHost;
        public bool CreatesWindows;
        public List<string> Trace = new();
    }

    static SideResult RunSide(string side, string test, List<string> steps)
    {
        var result = new SideResult { CreatesWindows = File.ReadAllText(test).Contains("create ", StringComparison.Ordinal) };
        // Each side runs in its own fresh copy of the test folder, so files a
        // program writes never reach the other side.
        string work = Path.Combine(Path.GetTempPath(), "otter-desktop-tests", $"{Path.GetFileNameWithoutExtension(test)}-{side}");
        if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
        Directory.CreateDirectory(work);
        foreach (string file in Directory.GetFiles(testsDir)) File.Copy(file, Path.Combine(work, Path.GetFileName(file)));
        // <name>.args: the command line instead of `run <name>.ot` (empty: the REPL).
        string argsPath = Path.ChangeExtension(test, ".args");
        string[] arguments = File.Exists(argsPath)
            ? File.ReadAllText(argsPath).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            : new[] { "run", Path.GetFileName(test) };
        ProcessStartInfo start;
        if (side == "otterps")
        {
            start = new ProcessStartInfo("powershell.exe");
            start.Environment.Remove("OTTER_ENGINE");
            if (referenceUiCulture != null)
            {
                string quoted = string.Join(" ", arguments.Select(a => "'" + a.Replace("'", "''") + "'"));
                string command = $"& {{ $c = [System.Globalization.CultureInfo]::GetCultureInfo('{referenceUiCulture}'); " +
                    "[System.Threading.Thread]::CurrentThread.CurrentUICulture = $c; [System.Threading.Thread]::CurrentThread.CurrentCulture = $c; " +
                    $"& '{otterPs.Replace("'", "''")}' {quoted}; exit $LASTEXITCODE }}";
                foreach (string a in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", command }) start.ArgumentList.Add(a);
                arguments = Array.Empty<string>();
            }
            else
            {
                foreach (string a in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", otterPs }) start.ArgumentList.Add(a);
            }
        }
        else
        {
            start = new ProcessStartInfo(otterNet);
            start.Environment["OTTER_NET_TRACE"] = "1";
            start.Environment["OTTER_ENGINE"] = "native";
        }
        foreach (string a in arguments) start.ArgumentList.Add(a);
        start.WorkingDirectory = work;
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardInput = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;

        using var process = Process.Start(start)!;
        var stdout = new MemoryStream();
        var stderr = new MemoryStream();
        Task outTask = process.StandardOutput.BaseStream.CopyToAsync(stdout);
        Task errTask = process.StandardError.BaseStream.CopyToAsync(stderr);
        string inputPath = test + ".input";
        try
        {
            if (File.Exists(inputPath)) process.StandardInput.BaseStream.Write(File.ReadAllBytes(inputPath));
            process.StandardInput.Close();
        }
        catch (IOException) { }

        var session = new Session(process);
        int stepNumber = 0;
        foreach (string step in steps)
        {
            stepNumber++;
            string observation;
            try
            {
                observation = session.Run(step, stepNumber, result);
            }
            catch (Exception e)
            {
                observation = $"{step}: failed ({e.GetType().Name})";
            }
            result.Observations.Add(observation);
        }

        if (!process.WaitForExit(60_000))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            process.WaitForExit(10_000);
            result.Exit = "killed (still running 60 s after the last step)";
        }
        else
        {
            result.Exit = process.ExitCode.ToString();
        }
        Task.WaitAll(new[] { outTask, errTask }, 10_000);
        result.Stdout = stdout.ToArray();
        string errorText = Encoding.UTF8.GetString(stderr.ToArray());
        var kept = new List<string>();
        foreach (string line in errorText.Replace("\r\n", "\n").Split('\n'))
        {
            if (side == "otter-net" && line.StartsWith("otter-net ", StringComparison.Ordinal))
            {
                result.Trace.Add(line);
                if (line.StartsWith("otter-net desktop:", StringComparison.Ordinal)) result.RanInDesktopHost = true;
                continue;
            }
            kept.Add(line);
        }
        result.Stderr = string.Join("\n", kept);
        return result;
    }

    // Pixels whose colours differ by 1 or 2 of 255, in the last comparison.
    static int nearlySame;

    static List<string> Compare(string name, SideResult reference, SideResult candidate)
    {
        nearlySame = 0;
        var differences = new List<string>();
        if (reference.Exit != candidate.Exit) differences.Add($"exit code: otterPS {reference.Exit}, otter-net {candidate.Exit}");
        if (!reference.Stdout.AsSpan().SequenceEqual(candidate.Stdout))
        {
            differences.Add("standard output differs:\n" + LineDiff(Encoding.UTF8.GetString(reference.Stdout), Encoding.UTF8.GetString(candidate.Stdout)));
        }
        if (reference.Stderr != candidate.Stderr)
        {
            differences.Add("standard error differs:\n" + LineDiff(reference.Stderr, candidate.Stderr));
        }
        if (candidate.Trace.Any(t => t.Contains("fallback", StringComparison.Ordinal)))
        {
            differences.Add("otter-net handed the program to the PowerShell Otter: " + string.Join(" / ", candidate.Trace));
        }
        int count = Math.Max(reference.Observations.Count, candidate.Observations.Count);
        for (int i = 0; i < count; i++)
        {
            string r = i < reference.Observations.Count ? reference.Observations[i] : "(none)";
            string c = i < candidate.Observations.Count ? candidate.Observations[i] : "(none)";
            if (r != c) differences.Add($"step {i + 1} differs:\n" + LineDiff(r, c));
        }
        for (int i = 0; i < Math.Max(reference.Images.Count, candidate.Images.Count); i++)
        {
            if (i >= reference.Images.Count || i >= candidate.Images.Count)
            {
                differences.Add($"snapshot {i + 1}: only one side has it");
                continue;
            }
            var r = reference.Images[i];
            var c = candidate.Images[i];
            string? pixelDifference = null;
            if (r.Width != c.Width || r.Height != c.Height) pixelDifference = $"size {r.Width}x{r.Height} against {c.Width}x{c.Height}";
            else
            {
                // A pixel differs when a colour channel differs by more than 2
                // of 255: .NET Framework's and .NET 10's WPF anti-alias some
                // glyph edges one step apart (seen on bold heading text).
                int differing = 0, nearly = 0;
                for (int p = 0; p + 3 < r.Pixels.Length; p += 4)
                {
                    int delta = Math.Max(Math.Abs(r.Pixels[p] - c.Pixels[p]), Math.Max(Math.Abs(r.Pixels[p + 1] - c.Pixels[p + 1]), Math.Abs(r.Pixels[p + 2] - c.Pixels[p + 2])));
                    if (delta > 2) differing++;
                    else if (delta > 0) nearly++;
                }
                if (differing > 0) pixelDifference = $"{differing} of {r.Width * r.Height} pixels differ";
                if (nearly > 0) nearlySame += nearly;
            }
            if (pixelDifference != null)
            {
                differences.Add($"snapshot {i + 1} (step {r.Step}) pixels: {pixelDifference} (images in the artifact: {name}-{i + 1}-otterps.png, {name}-{i + 1}-otter-net.png)");
                Png.Write(Path.Combine(outDir, $"{name}-{i + 1}-otterps.png"), r.Width, r.Height, r.Pixels);
                Png.Write(Path.Combine(outDir, $"{name}-{i + 1}-otter-net.png"), c.Width, c.Height, c.Pixels);
            }
        }
        return differences;
    }

    static string LineDiff(string expected, string actual)
    {
        string[] e = expected.Replace("\r\n", "\n").Split('\n');
        string[] a = actual.Replace("\r\n", "\n").Split('\n');
        var text = new StringBuilder();
        int shown = 0;
        for (int i = 0; i < Math.Max(e.Length, a.Length) && shown < 12; i++)
        {
            string el = i < e.Length ? e[i] : "(end)";
            string al = i < a.Length ? a[i] : "(end)";
            if (el == al) continue;
            text.AppendLine($"  line {i + 1}: otterPS   `{el}`");
            text.AppendLine($"  line {i + 1}: otter-net `{al}`");
            shown++;
        }
        if (text.Length == 0) text.AppendLine("  (line endings or bytes differ)");
        return text.ToString().TrimEnd();
    }

    static void WriteTranscript(string name, string side, SideResult result)
    {
        var text = new StringBuilder();
        text.AppendLine($"# {name} ({side})").AppendLine();
        text.AppendLine($"exit: {result.Exit}").AppendLine();
        text.AppendLine("## stdout").AppendLine("```").Append(Encoding.UTF8.GetString(result.Stdout)).AppendLine("```");
        text.AppendLine("## stderr").AppendLine("```").AppendLine(result.Stderr).AppendLine("```");
        if (result.Trace.Count > 0) text.AppendLine("## trace").AppendLine("```").AppendLine(string.Join("\n", result.Trace)).AppendLine("```");
        text.AppendLine("## steps");
        for (int i = 0; i < result.Observations.Count; i++) text.AppendLine($"### {i + 1}").AppendLine("```").AppendLine(result.Observations[i]).AppendLine("```");
        File.WriteAllText(Path.Combine(outDir, $"{name}.{side}.md"), text.ToString());
    }

    // ---- driving the windows ----

    sealed class Session(Process process)
    {
        AutomationElement? window;
        IntPtr windowHandle;

        HashSet<int> ProcessTree()
        {
            var tree = new HashSet<int> { process.Id };
            var parents = Native.ParentProcesses();
            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (var (pid, parent) in parents)
                {
                    if (tree.Contains(parent) && tree.Add(pid)) grew = true;
                }
            }
            return tree;
        }

        bool Running()
        {
            try { return !process.HasExited; } catch (InvalidOperationException) { return false; }
        }

        List<AutomationElement> Windows()
        {
            var tree = ProcessTree();
            var found = new List<AutomationElement>();
            foreach (AutomationElement candidate in AutomationElement.RootElement.FindAll(TreeScope.Children, System.Windows.Automation.Condition.TrueCondition))
            {
                try
                {
                    if (tree.Contains(candidate.Current.ProcessId) && candidate.Current.ControlType == ControlType.Window) found.Add(candidate);
                }
                catch (ElementNotAvailableException) { }
            }
            return found;
        }

        public string Run(string step, int number, SideResult result)
        {
            List<string> t = Tokens(step);
            string verb = t[0];
            switch (verb)
            {
                case "window":
                {
                    string? title = t.Count > 1 && t[1] != "*" ? t[1].TrimStart('"') : null;
                    int timeout = t.Count > 2 ? int.Parse(t[2]) : 60_000;
                    var watch = Stopwatch.StartNew();
                    while (watch.ElapsedMilliseconds < timeout)
                    {
                        var match = Windows().FirstOrDefault(w => title == null || w.Current.Name == title);
                        if (match != null)
                        {
                            window = match;
                            windowHandle = new IntPtr(match.Current.NativeWindowHandle);
                            // Windows open where Windows places them, which
                            // differs between runs; a part off screen is not
                            // drawn. Every window is moved to the top left.
                            Native.MoveToTopLeft(new IntPtr(match.Current.NativeWindowHandle));
                            Settle();
                            return $"window: \"{match.Current.Name}\"";
                        }
                        if (!Running()) return "window: none (the program ended)";
                        Thread.Sleep(200);
                    }
                    return "window: none (timed out)";
                }
                case "snap":
                {
                    if (window == null) return "snap: no window";
                    Native.SetCursorPos(1, 1);
                    Settle();
                    string tree = Dump(window);
                    var image = Capture(window);
                    if (image != null) result.Images.Add((number, image.Value.Width, image.Value.Height, image.Value.Pixels));
                    return tree;
                }
                case "click":
                {
                    var element = Find(t, 1, out string selector);
                    if (element == null) return $"click {selector}: not found";
                    ((InvokePattern)element.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                    Settle();
                    return $"click {selector}: done";
                }
                case "type":
                {
                    // The last token is the text; the selector is before it.
                    var element = Find(t.Take(t.Count - 1).ToList(), 1, out string selector);
                    if (element == null) return $"type {selector}: not found";
                    string text = t[^1].TrimStart('"');
                    ((ValuePattern)element.GetCurrentPattern(ValuePattern.Pattern)).SetValue(text);
                    Settle();
                    return $"type {selector}: done";
                }
                case "focus":
                {
                    var element = Find(t, 1, out string selector);
                    if (element == null) return $"focus {selector}: not found";
                    element.SetFocus();
                    Settle();
                    return $"focus {selector}: done";
                }
                case "keys":
                {
                    Native.TypeText(t[1].TrimStart('"'));
                    Settle();
                    return "keys: done";
                }
                case "mouse":
                {
                    // mouse <selector> [dx dy]: a real click, at the element's
                    // centre or at dx, dy from its top left corner.
                    var element = Find(t, 1, out string selector);
                    if (element == null) return $"mouse {selector}: not found";
                    Rect r = element.Current.BoundingRectangle;
                    int consumed = 1 + SelectorLength(t, 1);
                    int x, y;
                    if (t.Count >= consumed + 2) { x = (int)r.Left + int.Parse(t[consumed]); y = (int)r.Top + int.Parse(t[consumed + 1]); }
                    else { x = (int)(r.Left + r.Width / 2); y = (int)(r.Top + r.Height / 2); }
                    Native.Click(x, y);
                    Settle();
                    return $"mouse {selector}: done";
                }
                case "close":
                {
                    if (window == null) return "close: no window";
                    // UI Automation can fail while a window is busy or as it
                    // goes away under the call (a COMException): a window that
                    // no longer exists (Win32's IsWindow) is closed; otherwise
                    // try again.
                    // After two failures it is closed as Alt+F4 would (WM_CLOSE).
                    IntPtr hwnd = windowHandle;
                    for (int attempt = 1; ; attempt++)
                    {
                        try
                        {
                            if (attempt > 2) Native.PostMessage(hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero);
                            else ((WindowPattern)window.GetCurrentPattern(WindowPattern.Pattern)).Close();
                            break;
                        }
                        catch (Exception e) when (e is COMException or ElementNotAvailableException or InvalidOperationException)
                        {
                            Thread.Sleep(500);
                            if (!Native.IsWindow(hwnd)) break;
                            if (attempt == 5) return $"close: failed ({e.GetType().Name})";
                        }
                    }
                    // The window is gone (or going): nothing of it to observe.
                    window = null;
                    Settle();
                    return "close: done";
                }
                case "wait":
                    Thread.Sleep(int.Parse(t[1]));
                    return $"wait {t[1]}";
                case "exit":
                {
                    int timeout = t.Count > 1 ? int.Parse(t[1]) : 60_000;
                    return process.WaitForExit(timeout) ? "exit: the program ended" : "exit: still running";
                }
                case "windows":
                {
                    // The titles of the program's windows, now.
                    Settle();
                    return "windows: " + string.Join(", ", Windows().Select(w => $"\"{w.Current.Name}\"").OrderBy(n => n, StringComparer.Ordinal));
                }
            }
            return $"{step}: unknown step";
        }

        // Waits until the window's tree stops changing.
        void Settle()
        {
            Thread.Sleep(400);
            if (window == null) return;
            string? last = null;
            for (int i = 0; i < 20; i++)
            {
                string now;
                // A window that goes away while it is read throws either.
                try { now = Dump(window); } catch (Exception e) when (e is ElementNotAvailableException or COMException) { return; }
                if (now == last) return;
                last = now;
                Thread.Sleep(150);
            }
        }

        static int SelectorLength(List<string> t, int at) => at + 1 < t.Count && t[at + 1].StartsWith('"') ? 2 : 1;

        // button "Say Hello" | button#2 | edit#1 | text "Hi" | pane#1
        AutomationElement? Find(List<string> t, int at, out string selector)
        {
            selector = SelectorLength(t, at) == 2 ? $"{t[at]} \"{t[at + 1][1..]}\"" : t[at];
            if (window == null) return null;
            string kind = t[at];
            int index = 1;
            int hash = kind.IndexOf('#');
            if (hash >= 0) { index = int.Parse(kind[(hash + 1)..]); kind = kind[..hash]; }
            string? name = SelectorLength(t, at) == 2 ? t[at + 1][1..] : null;
            ControlType type = kind switch
            {
                "button" => ControlType.Button,
                "edit" => ControlType.Edit,
                "text" => ControlType.Text,
                "pane" => ControlType.Pane,
                "window" => ControlType.Window,
                _ => throw new ArgumentException($"unknown kind {kind}"),
            };
            if (type == ControlType.Window && name == null) return window;
            int seen = 0;
            foreach (var element in Content(window))
            {
                if (element.Current.ControlType != type) continue;
                if (name != null && element.Current.Name != name) continue;
                if (++seen == index) return element;
            }
            return null;
        }

        // The window's own content, in tree order, without its title bar.
        static IEnumerable<AutomationElement> Content(AutomationElement root)
        {
            var walker = TreeWalker.ControlViewWalker;
            var stack = new Stack<AutomationElement>();
            var children = new List<AutomationElement>();
            for (var child = walker.GetFirstChild(root); child != null; child = walker.GetNextSibling(child)) children.Add(child);
            for (int i = children.Count - 1; i >= 0; i--) stack.Push(children[i]);
            while (stack.Count > 0)
            {
                var element = stack.Pop();
                if (element.Current.ControlType == ControlType.TitleBar || element.Current.ControlType == ControlType.MenuBar) continue;
                yield return element;
                var inner = new List<AutomationElement>();
                for (var child = walker.GetFirstChild(element); child != null; child = walker.GetNextSibling(child)) inner.Add(child);
                for (int i = inner.Count - 1; i >= 0; i--) stack.Push(inner[i]);
            }
        }

        static string Dump(AutomationElement root)
        {
            var text = new StringBuilder();
            Rect origin = root.Current.BoundingRectangle;
            text.Append($"Window \"{Escape(root.Current.Name)}\" size {Size(origin)}");
            text.Append(root.Current.IsEnabled ? "" : " disabled");
            var walker = TreeWalker.ControlViewWalker;
            void Walk(AutomationElement parent, int depth)
            {
                for (var element = walker.GetFirstChild(parent); element != null; element = walker.GetNextSibling(element))
                {
                    var c = element.Current;
                    if (c.ControlType == ControlType.TitleBar || c.ControlType == ControlType.MenuBar) continue;
                    // .NET Framework's WPF lists the hidden scroll bars of text
                    // boxes and scroll viewers (off screen, no rectangle) in the
                    // tree; .NET 10's does not. Nothing a user sees: left out on
                    // both sides.
                    if (c.ControlType == ControlType.ScrollBar && c.IsOffscreen && c.BoundingRectangle.IsEmpty) continue;
                    text.Append('\n').Append(new string(' ', depth * 2));
                    text.Append(c.ControlType.ProgrammaticName.Replace("ControlType.", ""));
                    text.Append($" \"{Escape(c.Name)}\"");
                    if (c.ClassName.Length > 0) text.Append($" class={c.ClassName}");
                    if (!c.IsEnabled) text.Append(" disabled");
                    if (c.IsOffscreen) text.Append(" offscreen");
                    Rect r = c.BoundingRectangle;
                    text.Append(r.IsEmpty ? " at nowhere" : $" at {Math.Round(r.Left - origin.Left)},{Math.Round(r.Top - origin.Top)} size {Size(r)}");
                    if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object? value))
                    {
                        var v = ((ValuePattern)value).Current;
                        text.Append($" value=\"{Escape(v.Value)}\"{(v.IsReadOnly ? " readonly" : "")}");
                    }
                    if (element.TryGetCurrentPattern(ScrollPattern.Pattern, out object? scroll))
                    {
                        var s = ((ScrollPattern)scroll).Current;
                        text.Append($" scroll v={s.VerticallyScrollable}:{Math.Round(s.VerticalViewSize)} h={s.HorizontallyScrollable}:{Math.Round(s.HorizontalViewSize)}");
                    }
                    Walk(element, depth + 1);
                }
            }
            Walk(root, 1);
            return text.ToString();
        }

        static string Size(Rect r) => $"{Math.Round(r.Width)}x{Math.Round(r.Height)}";

        static string Escape(string text) => text.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\"", "\\\"");

        static (int Width, int Height, byte[] Pixels)? Capture(AutomationElement element)
        {
            IntPtr hwnd = new(element.Current.NativeWindowHandle);
            return hwnd == IntPtr.Zero ? null : Native.CaptureClient(hwnd);
        }
    }
}

static class Native
{
    [DllImport("user32.dll")] public static extern bool SetCaretBlinkTime(uint milliseconds);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    // SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE
    public static void MoveToTopLeft(IntPtr hwnd) => SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0004 | 0x0010);
    [DllImport("user32.dll")] static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32FirstW(IntPtr snapshot, ref PROCESSENTRY32W entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32NextW(IntPtr snapshot, ref PROCESSENTRY32W entry);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER
    {
        public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PROCESSENTRY32W
    {
        public uint dwSize, cntUsage, th32ProcessID; public IntPtr th32DefaultHeapID; public uint th32ModuleID, cntThreads, th32ParentProcessID;
        public int pcPriClassBase; public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public InputUnion u; }

    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    public static List<(int Pid, int Parent)> ParentProcesses()
    {
        var list = new List<(int, int)>();
        IntPtr snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot == new IntPtr(-1)) return list;
        try
        {
            var entry = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
            if (!Process32FirstW(snapshot, ref entry)) return list;
            do list.Add(((int)entry.th32ProcessID, (int)entry.th32ParentProcessID));
            while (Process32NextW(snapshot, ref entry));
        }
        finally { CloseHandle(snapshot); }
        return list;
    }

    public static void Click(int x, int y)
    {
        SetCursorPos(x, y);
        Thread.Sleep(100);
        var inputs = new[]
        {
            new INPUT { type = 0, u = new InputUnion { mi = new MOUSEINPUT { dwFlags = 0x0002 } } },
            new INPUT { type = 0, u = new InputUnion { mi = new MOUSEINPUT { dwFlags = 0x0004 } } },
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    public static void TypeText(string text)
    {
        foreach (char ch in text)
        {
            var inputs = new[]
            {
                new INPUT { type = 1, u = new InputUnion { ki = new KEYBDINPUT { wScan = ch, dwFlags = 0x0004 } } },
                new INPUT { type = 1, u = new InputUnion { ki = new KEYBDINPUT { wScan = ch, dwFlags = 0x0004 | 0x0002 } } },
            };
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
            Thread.Sleep(20);
        }
    }

    // The window's client area as WPF draws it (PW_CLIENTONLY |
    // PW_RENDERFULLCONTENT), 32-bit BGRA, top row first.
    public static (int Width, int Height, byte[] Pixels)? CaptureClient(IntPtr hwnd)
    {
        if (!GetClientRect(hwnd, out RECT rect)) return null;
        int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0) return null;
        IntPtr screen = GetDC(IntPtr.Zero);
        IntPtr memory = CreateCompatibleDC(screen);
        var header = new BITMAPINFOHEADER { biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = width, biHeight = -height, biPlanes = 1, biBitCount = 32 };
        IntPtr bitmap = CreateDIBSection(screen, ref header, 0, out IntPtr bits, IntPtr.Zero, 0);
        IntPtr old = SelectObject(memory, bitmap);
        try
        {
            if (!PrintWindow(hwnd, memory, 3)) return null;
            var pixels = new byte[width * height * 4];
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            return (width, height, pixels);
        }
        finally
        {
            SelectObject(memory, old);
            DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }
}

// A minimal PNG writer (8-bit RGB), for the images of a difference.
static class Png
{
    static readonly uint[] Table = Enumerable.Range(0, 256).Select(n =>
    {
        uint c = (uint)n;
        for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    static uint Crc(byte[] data, int start, int length)
    {
        uint c = 0xFFFFFFFF;
        for (int i = start; i < start + length; i++) c = Table[(c ^ data[i]) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFF;
    }

    static void Chunk(Stream output, string type, byte[] data)
    {
        var length = new byte[] { (byte)(data.Length >> 24), (byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length };
        output.Write(length);
        var body = new byte[4 + data.Length];
        Encoding.ASCII.GetBytes(type).CopyTo(body, 0);
        data.CopyTo(body, 4);
        output.Write(body);
        uint crc = Crc(body, 0, body.Length);
        output.Write(new byte[] { (byte)(crc >> 24), (byte)(crc >> 16), (byte)(crc >> 8), (byte)crc });
    }

    public static void Write(string path, int width, int height, byte[] bgra)
    {
        using var file = File.Create(path);
        file.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        header[0] = (byte)(width >> 24); header[1] = (byte)(width >> 16); header[2] = (byte)(width >> 8); header[3] = (byte)width;
        header[4] = (byte)(height >> 24); header[5] = (byte)(height >> 16); header[6] = (byte)(height >> 8); header[7] = (byte)height;
        header[8] = 8; header[9] = 2;
        Chunk(file, "IHDR", header);
        var raw = new MemoryStream();
        for (int y = 0; y < height; y++)
        {
            raw.WriteByte(0);
            for (int x = 0; x < width; x++)
            {
                int p = (y * width + x) * 4;
                raw.WriteByte(bgra[p + 2]); raw.WriteByte(bgra[p + 1]); raw.WriteByte(bgra[p]);
            }
        }
        var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true)) raw.WriteTo(z);
        Chunk(file, "IDAT", compressed.ToArray());
        Chunk(file, "IEND", Array.Empty<byte>());
    }
}
