using System.Collections;
using System.Text;
using Otter.Language;

namespace Otter.Cli;

// -DebugAst's NeedsPowerShell (src/Otter.Cli/Host.cs), for AstDump.cs.
internal sealed class NeedsPowerShell(string reason) : Exception(reason);

// Every syntax node otter-net's parser builds for a program, by otterPS
// NodeKind: the parser's tree is walked through every public property (not
// only the ones -DebugAst prints), and each node is named as -DebugAst names
// it (AstDump.PsClass and the generated table).
//
//   dotnet NodeCensus.dll <file.ot>...      one line per file:
//     FILE<TAB>path<TAB>Kind=count,Kind=count,...   (or ERROR<TAB>path<TAB>reason)
//   then one line per otter-net class seen: CLASS<TAB>otter-net class<TAB>Kind
//   and one per node class in Otter.Language: NODE<TAB>otter-net class<TAB>Kind or ?
internal static partial class AstDump
{
    static int Main(string[] args)
    {
        var classes = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var output = new StringBuilder();
        foreach (string path in args)
        {
            ProgramNode program;
            try
            {
                string source = File.ReadAllText(path);
                program = Parser.Parse(Lexer.Tokenize(source));
            }
            catch (Exception problem)
            {
                output.Append("ERROR\t").Append(path).Append('\t').Append(problem.GetType().Name).Append('\n');
                continue;
            }
            var counts = new SortedDictionary<string, int>(StringComparer.Ordinal) { ["Program"] = 1 };
            var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
            foreach (Stmt statement in program.Statements) Walk(statement, counts, classes, seen);
            output.Append("FILE\t").Append(path).Append('\t')
                .Append(string.Join(",", counts.Select(pair => $"{pair.Key}={pair.Value}"))).Append('\n');
        }
        foreach (var pair in classes)
            foreach (string kind in pair.Value) output.Append("CLASS\t").Append(pair.Key).Append('\t').Append(kind).Append('\n');
        foreach (Type type in typeof(Node).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(Node)) && !t.IsAbstract).OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            string kind = Table.TryGetValue(type.Name, out var entry) ? entry.Kind : "?";
            output.Append("NODE\t").Append(type.Name).Append('\t').Append(kind).Append('\n');
        }
        Console.Out.Write(output.ToString());
        return 0;
    }

    static void Walk(object? value, SortedDictionary<string, int> counts, SortedDictionary<string, SortedSet<string>> classes, HashSet<object> seen)
    {
        if (value is null or string || value.GetType().IsPrimitive || value.GetType().IsEnum) return;
        if (!value.GetType().IsValueType && !seen.Add(value)) return;
        if (value is Node node)
        {
            string kind;
            try { kind = KindOf(node); }
            catch (NeedsPowerShell) { kind = "?" + node.GetType().Name; }
            counts[kind] = counts.TryGetValue(kind, out int n) ? n + 1 : 1;
            if (!classes.TryGetValue(node.GetType().Name, out var kinds)) classes[node.GetType().Name] = kinds = new SortedSet<string>(StringComparer.Ordinal);
            kinds.Add(kind);
        }
        if (value is IEnumerable items)
        {
            foreach (object? item in items) Walk(item, counts, classes, seen);
            return;
        }
        Type type = value.GetType();
        if (type.Assembly != typeof(Node).Assembly && !(type.IsGenericType && type.Name.StartsWith("KeyValuePair", StringComparison.Ordinal))) return;
        foreach (var property in type.GetProperties())
        {
            if (property.GetIndexParameters().Length > 0) continue;
            object? child;
            try { child = property.GetValue(value); } catch { continue; }
            Walk(child, counts, classes, seen);
        }
    }
}
