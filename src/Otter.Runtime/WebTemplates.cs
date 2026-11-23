using System.Net;
using System.Text;

namespace Otter.Runtime;

// The `web template` object of the Otter Web runtime (ADR WEB-003,
// docs/web/RUNTIME_CONTRACT.md §10): a small logic-less Mustache subset,
// HTML-escaped by default and checked against the HTML around each
// placeholder when the file is loaded.
//
//   {{name}} {{entry.name}} {{.}}   the value, HTML-escaped
//   {{{name}}}                       the value unescaped (text only; logged)
//   {{#each list}}...{{/each}}       once per item
//   {{#if value}}...{{else}}...{{/if}}
//   {{> header}}                     views/header.html, in text only
//
// Template files live in the project's `views` folder and are resolved with
// the static-file rules (no dot names, no `..`, no links out).

public sealed class OtterWebTemplateProblem(string message) : Exception(message);

public sealed class OtterWebTemplates
{
    // A template file larger than this is refused (they are read whole).
    public const int LargestFile = 1024 * 1024;
    // Partials inside partials, at most this deep.
    const int DeepestPartial = 16;

    readonly Dictionary<string, Loaded> cache = new(StringComparer.Ordinal);
    readonly Action<string>? warn;

    public OtterWebTemplates(Action<string>? warn) => this.warn = warn;

    sealed record Loaded(Template Template, DateTime Written, long Length);

    // ---- what a template is made of ----

    abstract record Node(int Line);
    sealed record TextNode(string Text, int Line) : Node(Line);
    sealed record ValueNode(string Path, bool Raw, int Line) : Node(Line);
    sealed record EachNode(string Path, List<Node> Body, int Line) : Node(Line);
    sealed record IfNode(string Path, List<Node> Then, List<Node> Else, int Line) : Node(Line);
    sealed record PartialNode(string Name, int Line) : Node(Line);
    // The value of a URL attribute (href, src, ...) with a placeholder in
    // it: rendered, then checked whole, and replaced by "#" when unsafe.
    sealed record UrlNode(string Attribute, bool List, List<Node> Body, int Line) : Node(Line);

    sealed class Template(string name, List<Node> nodes, List<string> partials)
    {
        public string Name { get; } = name;
        public List<Node> Nodes { get; } = nodes;
        public List<string> Partials { get; } = partials;
    }

    // ---- loading ----

    // The real path of the views folder of `project`, or a problem.
    public static string ViewsRoot(string project)
    {
        string projectReal = OtterWebStatic.RealPath(project);
        string views = Path.Combine(project, "views");
        if (!Directory.Exists(views))
            throw new OtterWebTemplateProblem("The project has no views folder: web templates are .html files in a folder called \"views\" next to the program.");
        string real = OtterWebStatic.RealPath(views);
        if (!OtterWebStatic.IsInside(real, projectReal) || OtterWebStatic.SamePath(real, projectReal))
            throw new OtterWebTemplateProblem("The views folder must be a folder inside the project, not a link to somewhere else.");
        return real;
    }

    // `file is "views/guestbook.html"`: the real path of that template.
    public static string ResolveFile(string viewsRoot, string file)
    {
        string text = file.Trim().Replace('\\', '/');
        if (!OtterWebPaths.TryDecodeSegments("/" + text, out var segments) || text.Contains('%') || text.StartsWith('/') ||
            segments.Count < 2 || segments[0] != "views")
        {
            throw new OtterWebTemplateProblem($"The web template file \"{file}\" must be a path inside the project's views folder, such as \"views/page.html\".");
        }
        return ResolveInViews(viewsRoot, segments.Skip(1).ToList(), $"\"{file}\"");
    }

    static string ResolveInViews(string viewsRoot, List<string> segments, string shown)
    {
        if (!segments[^1].EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            throw new OtterWebTemplateProblem($"The web template {shown} must be an .html file.");
        string? real = OtterWebStatic.Resolve(viewsRoot, segments);
        if (real == null)
            throw new OtterWebTemplateProblem($"The web template {shown} is not a file in the views folder (hidden names, \"..\" and links out of the folder are refused).");
        return real;
    }

    // Renders the template at `realPath` (from ResolveFile) with `values`.
    public string Render(string viewsRoot, string realPath, OtterThing values)
    {
        var templates = new Dictionary<string, Template>(StringComparer.Ordinal);
        Template root = LoadTree(viewsRoot, realPath, templates, new List<string>());
        var output = new StringBuilder();
        new Renderer(viewsRoot, templates, output).Run(root, new List<object?> { values }, 0);
        return output.ToString();
    }

    // The template and every partial it reaches, each fresh from the cache,
    // so that a missing partial or a loop is found before anything is sent.
    Template LoadTree(string viewsRoot, string realPath, Dictionary<string, Template> templates, List<string> path)
    {
        if (templates.TryGetValue(realPath, out var done)) return done;
        Template template = Load(viewsRoot, realPath);
        if (path.Contains(realPath))
            throw new OtterWebTemplateProblem($"The web template {template.Name} includes itself through its partials ({string.Join(" > ", path.Select(p => Shown(viewsRoot, p)))} > {template.Name}).");
        path.Add(realPath);
        foreach (string partial in template.Partials)
        {
            if (path.Count > DeepestPartial)
                throw new OtterWebTemplateProblem($"The web template {template.Name}: partials are nested more than {DeepestPartial} deep.");
            LoadTree(viewsRoot, PartialPath(viewsRoot, partial, template.Name), templates, path);
        }
        path.RemoveAt(path.Count - 1);
        templates[realPath] = template;
        return template;
    }

    static string PartialPath(string viewsRoot, string name, string from) =>
        ResolveInViews(viewsRoot, (name + ".html").Split('/').ToList(), $"\"views/{name}.html\" (included by {from})");

    static string Shown(string viewsRoot, string realPath) =>
        "views/" + Path.GetRelativePath(viewsRoot, realPath).Replace(Path.DirectorySeparatorChar, '/');

    // Parsed once; parsed again when the file's size or time changes, so an
    // edit shows on the next request (development reloads).
    Template Load(string viewsRoot, string realPath)
    {
        var info = new FileInfo(realPath);
        if (cache.TryGetValue(realPath, out var loaded) && loaded.Written == info.LastWriteTimeUtc && loaded.Length == info.Length)
            return loaded.Template;
        string name = Shown(viewsRoot, realPath);
        if (info.Length > LargestFile)
            throw new OtterWebTemplateProblem($"The web template {name} is larger than {LargestFile / 1024} KiB.");
        string text;
        try { text = File.ReadAllText(realPath, new UTF8Encoding(false, throwOnInvalidBytes: true)); }
        catch (DecoderFallbackException) { throw new OtterWebTemplateProblem($"The web template {name} is not UTF-8 text."); }
        catch (IOException e) { throw new OtterWebTemplateProblem($"The web template {name} could not be read: {e.Message}"); }
        var template = Parse(name, text, warn);
        cache[realPath] = new Loaded(template, info.LastWriteTimeUtc, info.Length);
        return template;
    }

    // ---- parsing: placeholders, then the HTML around them ----

    sealed record Token(string? Text, string? Tag, int Line, bool Triple);

    static Template Parse(string name, string text, Action<string>? warn)
    {
        var (tokens, contexts) = new HtmlContexts(name).Classify(Tokenize(name, text));
        var partials = new List<string>();
        var stack = new Stack<(Token Open, string Kind, string Path, List<Node> Outer, List<Node> Then, List<Node>? Else, bool IsUrl)>();
        var nodes = new List<Node>();
        for (int i = 0; i < tokens.Count; i++)
        {
            Token token = tokens[i];
            Mark mark = contexts[i];
            if (mark.UrlStart is { } url)
            {
                // From here to the matching end, the nodes are one URL value.
                var body = new List<Node>();
                stack.Push((token, "url:" + url.Attribute, url.List ? "list" : "", nodes, body, null, true));
                nodes = body;
            }
            if (token.Text != null)
            {
                if (token.Text.Length > 0) nodes.Add(new TextNode(token.Text, token.Line));
            }
            else
            {
                string tag = token.Tag!;
                string where = $"{name}, line {token.Line}";
                if (token.Triple)
                {
                    string path = CheckPath(tag, where, "{{{" + tag + "}}}");
                    nodes.Add(new ValueNode(path, true, token.Line));
                    warn?.Invoke($"Otter Web: warning: {where} writes {{{{{{{tag}}}}}}} without HTML escaping.");
                }
                else if (tag.StartsWith('#'))
                {
                    string[] parts = tag[1..].Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (parts.Length != 2 || parts[0] is not ("each" or "if"))
                        throw new OtterWebTemplateProblem($"{where}: {{{{{tag}}}}} is not a template block. Use {{{{#each list}}}} or {{{{#if value}}}}.");
                    string path = CheckPath(parts[1], where, "{{" + tag + "}}");
                    var body = new List<Node>();
                    stack.Push((token, parts[0], path, nodes, body, null, false));
                    nodes = body;
                }
                else if (tag == "else")
                {
                    if (stack.Count == 0 || stack.Peek().Kind != "if" || stack.Peek().Else != null)
                        throw new OtterWebTemplateProblem($"{where}: {{{{else}}}} belongs between {{{{#if ...}}}} and {{{{/if}}}}.");
                    var top = stack.Pop();
                    var other = new List<Node>();
                    stack.Push(top with { Else = other });
                    nodes = other;
                }
                else if (tag.StartsWith('/'))
                {
                    string kind = tag[1..].Trim();
                    if (stack.Count == 0 || stack.Peek().IsUrl || stack.Peek().Kind != kind)
                        throw new OtterWebTemplateProblem($"{where}: {{{{{tag}}}}} does not close an open block" +
                            (stack.Count > 0 && !stack.Peek().IsUrl ? $" (the {{{{#{stack.Peek().Kind} ...}}}} on line {stack.Peek().Open.Line} is still open)." : "."));
                    var open = stack.Pop();
                    nodes = open.Outer;
                    nodes.Add(kind == "each"
                        ? new EachNode(open.Path, open.Then, open.Open.Line)
                        : new IfNode(open.Path, open.Then, open.Else ?? new List<Node>(), open.Open.Line));
                }
                else if (tag.StartsWith('>'))
                {
                    string partial = tag[1..].Trim();
                    bool valid = partial.Length > 0 && partial.Split('/').All(s => s.Length > 0 && s[0] != '.' && s.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.'));
                    if (!valid)
                        throw new OtterWebTemplateProblem($"{where}: {{{{> {partial}}}}} is not a partial name. Name a file in the views folder without .html, such as {{{{> header}}}} or {{{{> parts/footer}}}}.");
                    nodes.Add(new PartialNode(partial, token.Line));
                    if (!partials.Contains(partial)) partials.Add(partial);
                }
                else
                {
                    nodes.Add(new ValueNode(CheckPath(tag, where, "{{" + tag + "}}"), false, token.Line));
                }
            }
            if (mark.UrlEnd)
            {
                var open = stack.Pop();
                nodes = open.Outer;
                nodes.Add(new UrlNode(open.Kind["url:".Length..], open.Path == "list", open.Then, open.Open.Line));
            }
        }
        if (stack.Count > 0)
        {
            var open = stack.Peek();
            throw new OtterWebTemplateProblem($"{name}, line {open.Open.Line}: {{{{#{open.Kind} {open.Path}}}}} is never closed with {{{{/{open.Kind}}}}}.");
        }
        return new Template(name, nodes, partials);
    }

    // "name", "entry.name" or "." (the current item).
    static string CheckPath(string text, string where, string shown)
    {
        string path = text.Trim();
        if (path == ".") return path;
        bool valid = path.Length > 0 && path.Split('.').All(s => s.Length > 0 && (char.IsAsciiLetter(s[0]) || s[0] == '_') && s.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'));
        if (!valid)
            throw new OtterWebTemplateProblem($"{where}: {shown} must name a value, such as {{{{title}}}} or {{{{entry.name}}}}. Templates have no expressions or calls.");
        return path;
    }

    static List<Token> Tokenize(string name, string text)
    {
        var tokens = new List<Token>();
        int line = 1, at = 0;
        while (at < text.Length)
        {
            int open = text.IndexOf("{{", at, StringComparison.Ordinal);
            if (open < 0) open = text.Length;
            string literal = text[at..open];
            tokens.Add(new Token(literal, null, line, false));
            line += literal.Count(c => c == '\n');
            if (open == text.Length) break;
            bool triple = open + 2 < text.Length && text[open + 2] == '{';
            string close = triple ? "}}}" : "}}";
            int start = open + (triple ? 3 : 2);
            int end = text.IndexOf(close, start, StringComparison.Ordinal);
            int newline = text.IndexOf('\n', start);
            if (end < 0 || (newline >= 0 && newline < end))
                throw new OtterWebTemplateProblem($"{name}, line {line}: \"{(triple ? "{{{" : "{{")}\" is not closed with \"{close}\" on the same line.");
            tokens.Add(new Token(null, text[start..end].Trim(), line, triple));
            at = end + close.Length;
        }
        tokens.Add(new Token("", null, line, false));
        return tokens;
    }

    // ---- the HTML context of every placeholder ----

    sealed record UrlStart(string Attribute, bool List);
    sealed record Mark(UrlStart? UrlStart, bool UrlEnd);

    // A small HTML tokenizer over the template's own text, following the
    // HTML standard's states closely enough to know, at each placeholder,
    // whether it is in text, in a quoted attribute value, or somewhere a
    // value could become code or markup (then the template is refused).
    sealed class HtmlContexts(string name)
    {
        enum State { Text, TagOpen, EndTagOpen, TagName, BeforeAttrName, AttrName, AfterAttrName, BeforeAttrValue, AttrQuoted, AttrUnquoted, AfterAttrQuoted, SelfClosing, MarkupOpen, Comment, Bogus, RawText }

        State state = State.Text;
        string tagName = "", attrName = "", rawEnd = "";
        bool endTag;
        char quote;
        int tagId, attrId;
        readonly StringBuilder buffer = new();

        // Elements whose content is code, not text.
        static readonly HashSet<string> RawElements = new(StringComparer.Ordinal) { "script", "style" };
        // Elements whose attributes may never hold a value: they load code or
        // change where every other URL points.
        static readonly HashSet<string> CodeElements = new(StringComparer.Ordinal)
        {
            "script", "style", "base", "object", "embed", "applet", "frame", "frameset",
            "set", "animate", "animatemotion", "animatetransform", "animatecolor", "handler", "listener",
        };
        static readonly HashSet<string> UrlAttributes = new(StringComparer.Ordinal)
        {
            "href", "src", "action", "formaction", "poster", "cite", "background", "data", "codebase", "classid", "manifest",
            "longdesc", "usemap", "profile", "icon", "lowsrc", "dynsrc", "archive", "xlink:href", "ping", "srcset", "imagesrcset",
        };
        static readonly HashSet<string> UrlListAttributes = new(StringComparer.Ordinal) { "ping", "srcset", "imagesrcset" };

        // The tokens again, with text split at the quotes of every quoted
        // attribute value, and where each URL value with a placeholder
        // starts and ends.
        public (List<Token> Tokens, List<Mark> Marks) Classify(List<Token> tokens)
        {
            var split = new List<Token>();
            var blocks = new Stack<(string Context, int Line)>();
            var valueStart = new Dictionary<int, int>();
            var valueEnd = new Dictionary<int, int>();
            var urls = new Dictionary<int, UrlStart>();
            for (int i = 0; i < tokens.Count; i++)
            {
                Token token = tokens[i];
                if (token.Text != null)
                {
                    int line = token.Line, pieceLine = token.Line, from = 0;
                    string text = token.Text;
                    for (int k = 0; k < text.Length; k++)
                    {
                        State was = state;
                        Step(text[k]);
                        if (was != State.AttrQuoted && state == State.AttrQuoted)
                        {
                            // After the opening quote.
                            split.Add(new Token(text[from..(k + 1)], null, pieceLine, false));
                            from = k + 1; pieceLine = line;
                            valueStart[attrId] = split.Count;
                        }
                        else if (was == State.AttrQuoted && state != State.AttrQuoted)
                        {
                            // Before the closing quote.
                            if (k > from) split.Add(new Token(text[from..k], null, pieceLine, false));
                            from = k; pieceLine = line;
                            valueEnd[attrId] = split.Count - 1;
                        }
                        if (text[k] == '\n') line++;
                    }
                    split.Add(new Token(text[from..], null, pieceLine, false));
                    continue;
                }
                string where = $"{name}, line {token.Line}";
                string tag = token.Tag!;
                bool block = !token.Triple && (tag.StartsWith('#') || tag.StartsWith('/') || tag == "else");
                bool partial = !token.Triple && tag.StartsWith('>');
                if (block)
                {
                    string context = BlockContext(where, tag, PreviousChar(tokens, i), NextChar(tokens, i));
                    if (tag.StartsWith('#')) blocks.Push((context, token.Line));
                    else if (blocks.Count > 0)
                    {
                        var open = blocks.Peek();
                        if (open.Context != context)
                            throw new OtterWebTemplateProblem($"{where}: {{{{{tag}}}}} is somewhere else in the HTML than the block it belongs to (line {open.Line}). A block must open and close in the same place: both in text, or both inside the same tag or attribute value.");
                        if (tag.StartsWith('/')) blocks.Pop();
                    }
                }
                else if (partial)
                {
                    if (state != State.Text)
                        throw new OtterWebTemplateProblem($"{where}: {{{{{tag}}}}} can only be used in text, not {Describe()}.");
                }
                else
                {
                    CheckValue(where, tag, token.Triple);
                    if (state == State.AttrQuoted && UrlAttributes.Contains(attrName))
                        urls.TryAdd(attrId, new UrlStart(attrName, UrlListAttributes.Contains(attrName)));
                }
                split.Add(token);
            }
            if (state != State.Text)
                throw new OtterWebTemplateProblem($"{name}: the template ends {Describe()}. Every template (and partial) must end in ordinary text.");
            if (blocks.Count > 0)
                throw new OtterWebTemplateProblem($"{name}, line {blocks.Peek().Line}: a block is never closed.");
            var starts = urls.ToDictionary(u => valueStart[u.Key], u => u.Value);
            var ends = urls.Keys.Select(id => valueEnd[id]).ToHashSet();
            var marks = new List<Mark>();
            for (int i = 0; i < split.Count; i++) marks.Add(new Mark(starts.GetValueOrDefault(i), ends.Contains(i)));
            return (split, marks);
        }

        static char PreviousChar(List<Token> tokens, int i)
        {
            for (int j = i - 1; j >= 0; j--)
            {
                if (tokens[j].Text == null) return '￿';
                if (tokens[j].Text!.Length > 0) return tokens[j].Text![^1];
            }
            return '\n';
        }

        static char NextChar(List<Token> tokens, int i)
        {
            for (int j = i + 1; j < tokens.Count; j++)
            {
                if (tokens[j].Text == null) return '￿';
                if (tokens[j].Text!.Length > 0) return tokens[j].Text![0];
            }
            return '\n';
        }

        static bool Space(char c) => c is ' ' or '\t' or '\n' or '\r' or '\f';

        string Describe() => state switch
        {
            State.RawText => $"inside <{tagName}> (its content is code, not text)",
            State.Comment => "inside an HTML comment",
            State.Bogus => "inside a <! or <? declaration",
            State.AttrQuoted => $"inside the {attrName} attribute of <{tagName}>",
            State.AttrUnquoted or State.BeforeAttrValue => $"in the unquoted value of the {attrName} attribute of <{tagName}>",
            State.Text => "in text",
            _ => $"inside the <{tagName}> tag",
        };

        // What a block tag must match at its other end. Inside a tag, only
        // between attributes, with spaces on both sides, so that no branch
        // can join two names into one (on + click).
        string BlockContext(string where, string tag, char previous, char next)
        {
            switch (state)
            {
                case State.Text:
                    return "text";
                case State.AttrQuoted:
                    if (CodeElements.Contains(tagName) || DangerousAttribute(attrName))
                        throw new OtterWebTemplateProblem($"{where}: {{{{{tag}}}}} cannot go {Describe()}.");
                    return $"attr:{attrId}";
                case State.BeforeAttrName or State.AfterAttrName when !endTag && Space(previous) && (Space(next) || next is '>' || next is '/'):
                    return $"tag:{tagId}";
                default:
                    throw new OtterWebTemplateProblem($"{where}: {{{{{tag}}}}} cannot go {Describe()}." +
                        (state is State.BeforeAttrName or State.AttrName or State.AfterAttrName ? " Between attributes, put a space before and after it: <input {{#if on}} checked {{/if}}>." : ""));
            }
        }

        static bool DangerousAttribute(string attribute) =>
            attribute.StartsWith("on", StringComparison.Ordinal) || attribute is "style" or "srcdoc";

        void CheckValue(string where, string tag, bool triple)
        {
            string shown = triple ? "{{{" + tag + "}}}" : "{{" + tag + "}}";
            switch (state)
            {
                case State.Text:
                    return;
                case State.AttrQuoted:
                    if (triple)
                        throw new OtterWebTemplateProblem($"{where}: {shown} (without escaping) can only be used in text, not {Describe()}. Use {{{{{tag}}}}}.");
                    if (CodeElements.Contains(tagName))
                        throw new OtterWebTemplateProblem($"{where}: {shown} cannot go in an attribute of <{tagName}>: it would load or run code. Write the value into the template file itself.");
                    if (attrName.StartsWith("on", StringComparison.Ordinal))
                        throw new OtterWebTemplateProblem($"{where}: {shown} cannot go in the {attrName} attribute: its value is JavaScript. Put the value in a data- attribute and read it from a script file.");
                    if (attrName == "style")
                        throw new OtterWebTemplateProblem($"{where}: {shown} cannot go in a style attribute: its value is CSS. Choose between classes instead, with {{{{#if}}}}.");
                    if (attrName == "srcdoc")
                        throw new OtterWebTemplateProblem($"{where}: {shown} cannot go in srcdoc: its value is a whole HTML page.");
                    return;
                case State.RawText:
                    throw new OtterWebTemplateProblem($"{where}: {shown} cannot go inside <{tagName}>: its content is code, not text, and HTML escaping does not make a value safe there. Pass the value in a data- attribute, or fetch it as JSON from a route.");
                case State.BeforeAttrValue or State.AttrUnquoted:
                    throw new OtterWebTemplateProblem($"{where}: {shown} is in an unquoted attribute value. Put the value in quotes: {attrName}=\"{shown}\".");
                case State.Comment or State.Bogus:
                    throw new OtterWebTemplateProblem($"{where}: {shown} cannot go {Describe()}.");
                default:
                    throw new OtterWebTemplateProblem($"{where}: {shown} cannot be part of a tag or attribute name ({(state is State.TagOpen or State.EndTagOpen ? "right after <" : $"inside <{tagName}>")}). Use a placeholder only in text or in a quoted attribute value.");
            }
        }

        void EmitTag()
        {
            if (!endTag && RawElements.Contains(tagName))
            {
                state = State.RawText;
                rawEnd = "</" + tagName;
                buffer.Clear();
            }
            else state = State.Text;
        }

        // One character of template text.
        void Step(char c)
        {
            switch (state)
            {
                case State.Text:
                    if (c == '<') state = State.TagOpen;
                    break;
                case State.TagOpen:
                    if (char.IsAsciiLetter(c)) { tagId++; endTag = false; tagName = char.ToLowerInvariant(c).ToString(); state = State.TagName; }
                    else if (c == '/') state = State.EndTagOpen;
                    else if (c == '!') { state = State.MarkupOpen; buffer.Clear(); }
                    else if (c == '?') state = State.Bogus;
                    else { state = State.Text; Step(c); }
                    break;
                case State.EndTagOpen:
                    if (char.IsAsciiLetter(c)) { tagId++; endTag = true; tagName = char.ToLowerInvariant(c).ToString(); state = State.TagName; }
                    else if (c == '>') state = State.Text;
                    else state = State.Bogus;
                    break;
                case State.TagName:
                    if (Space(c)) state = State.BeforeAttrName;
                    else if (c == '/') state = State.SelfClosing;
                    else if (c == '>') EmitTag();
                    else tagName += char.ToLowerInvariant(c);
                    break;
                case State.BeforeAttrName:
                    if (Space(c)) break;
                    if (c is '/' or '>') { state = State.AfterAttrName; Step(c); }
                    else { attrName = char.ToLowerInvariant(c).ToString(); state = State.AttrName; }
                    break;
                case State.AttrName:
                    if (Space(c) || c is '/' or '>') { state = State.AfterAttrName; Step(c); }
                    else if (c == '=') state = State.BeforeAttrValue;
                    else attrName += char.ToLowerInvariant(c);
                    break;
                case State.AfterAttrName:
                    if (Space(c)) break;
                    if (c == '/') state = State.SelfClosing;
                    else if (c == '=') state = State.BeforeAttrValue;
                    else if (c == '>') EmitTag();
                    else { attrName = char.ToLowerInvariant(c).ToString(); state = State.AttrName; }
                    break;
                case State.BeforeAttrValue:
                    if (Space(c)) break;
                    if (c is '"' or '\'') { quote = c; attrId++; state = State.AttrQuoted; }
                    else if (c == '>') EmitTag();
                    else { attrId++; state = State.AttrUnquoted; }
                    break;
                case State.AttrQuoted:
                    if (c == quote) state = State.AfterAttrQuoted;
                    break;
                case State.AttrUnquoted:
                    if (Space(c)) state = State.BeforeAttrName;
                    else if (c == '>') EmitTag();
                    break;
                case State.AfterAttrQuoted:
                    if (Space(c)) state = State.BeforeAttrName;
                    else if (c == '/') state = State.SelfClosing;
                    else if (c == '>') EmitTag();
                    else { state = State.BeforeAttrName; Step(c); }
                    break;
                case State.SelfClosing:
                    if (c == '>') EmitTag();
                    else { state = State.BeforeAttrName; Step(c); }
                    break;
                case State.MarkupOpen:
                    buffer.Append(c);
                    if (buffer.ToString() == "--") { state = State.Comment; buffer.Clear(); }
                    else if (!"--".StartsWith(buffer.ToString(), StringComparison.Ordinal)) { state = State.Bogus; if (c == '>') state = State.Text; }
                    break;
                case State.Comment:
                    // Ends at "-->" (the standard also ends it sooner, at
                    // "--!>" and "<!-->"; ending later here only refuses more).
                    buffer.Append(c);
                    if (buffer.Length > 3) buffer.Remove(0, buffer.Length - 3);
                    if (buffer.ToString() == "-->") { state = State.Text; buffer.Clear(); }
                    break;
                case State.Bogus:
                    if (c == '>') state = State.Text;
                    break;
                case State.RawText:
                    // Ends at "</script" or "</style" followed by a space,
                    // "/" or ">" (any capitals).
                    buffer.Append(char.ToLowerInvariant(c));
                    if (buffer.Length > rawEnd.Length + 1) buffer.Remove(0, buffer.Length - rawEnd.Length - 1);
                    if (buffer.Length == rawEnd.Length + 1 && buffer.ToString(0, rawEnd.Length) == rawEnd && (Space(c) || c is '/' or '>'))
                    {
                        endTag = true;
                        tagId++;
                        buffer.Clear();
                        state = State.TagName;
                        Step(c);
                    }
                    break;
            }
        }
    }

    // ---- rendering ----

    sealed class Renderer(string viewsRoot, Dictionary<string, Template> templates, StringBuilder output)
    {
        // The URL value being rendered, as the browser will read it.
        StringBuilder? url;

        public void Run(Template template, List<object?> scopes, int depth)
        {
            foreach (Node node in template.Nodes) Write(template, node, scopes, depth);
        }

        void Write(Template template, Node node, List<object?> scopes, int depth)
        {
            switch (node)
            {
                case TextNode text:
                    output.Append(text.Text);
                    url?.Append(WebUtility.HtmlDecode(text.Text));
                    break;
                case ValueNode value:
                {
                    object? found = Lookup(template, value.Path, scopes, value.Line);
                    if (found is OtterThing or OtterList)
                        throw new OtterWebTemplateProblem($"{template.Name}, line {value.Line}: {value.Path} is {Values.TypeName(found)}, not a value to show." +
                            (found is OtterList ? $" Use {{{{#each {value.Path}}}}}...{{{{/each}}}}." : $" Name a value inside it, such as {{{{{value.Path}.name}}}}."));
                    string text = found == null ? "" : Values.Format(found);
                    output.Append(value.Raw ? text : Escape(text));
                    url?.Append(text);
                    break;
                }
                case EachNode each:
                {
                    object? found = Lookup(template, each.Path, scopes, each.Line);
                    if (found is not OtterList list)
                        throw new OtterWebTemplateProblem($"{template.Name}, line {each.Line}: {{{{#each {each.Path}}}}} needs a list, but {each.Path} is {Values.TypeName(found)}.");
                    foreach (object? item in list.ToList())
                    {
                        scopes.Add(item);
                        try { foreach (Node child in each.Body) Write(template, child, scopes, depth); }
                        finally { scopes.RemoveAt(scopes.Count - 1); }
                    }
                    break;
                }
                case IfNode test:
                {
                    object? found = Lookup(template, test.Path, scopes, test.Line);
                    bool yes = found switch { null => false, bool b => b, string s => s.Length > 0, OtterList l => l.Count > 0, _ => true };
                    foreach (Node child in yes ? test.Then : test.Else) Write(template, child, scopes, depth);
                    break;
                }
                case PartialNode partial:
                {
                    string real = PartialPath(viewsRoot, partial.Name, template.Name);
                    if (depth >= DeepestPartial || !templates.TryGetValue(real, out var inner))
                        throw new OtterWebTemplateProblem($"{template.Name}, line {partial.Line}: the partial {partial.Name} could not be included.");
                    Run(inner, scopes, depth + 1);
                    break;
                }
                case UrlNode link:
                {
                    int start = output.Length;
                    url = new StringBuilder();
                    try { foreach (Node child in link.Body) Write(template, child, scopes, depth); }
                    finally
                    {
                        string written = url.ToString();
                        url = null;
                        if (!(link.List ? SafeUrlList(written) : SafeUrl(written)))
                        {
                            output.Length = start;
                            output.Append('#');
                        }
                    }
                    break;
                }
            }
        }

        static object? Lookup(Template template, string path, List<object?> scopes, int line)
        {
            if (path == ".") return scopes[^1];
            string[] parts = path.Split('.');
            object? value = null;
            bool found = false;
            for (int i = scopes.Count - 1; i >= 0 && !found; i--)
            {
                if (scopes[i] is OtterThing thing && thing.HasProperty(parts[0]))
                {
                    value = thing.ReadProperty(parts[0]);
                    found = true;
                }
            }
            if (!found)
                throw new OtterWebTemplateProblem($"{template.Name}, line {line}: there is no value called {parts[0]}. Give the template's values a {parts[0]} (it may be gone, but it must be there).");
            for (int i = 1; i < parts.Length; i++)
            {
                string so = string.Join('.', parts[..i]);
                if (value is not OtterThing thing)
                    throw new OtterWebTemplateProblem($"{template.Name}, line {line}: {so} is {Values.TypeName(value)}, so it has no {parts[i]}.");
                if (!thing.HasProperty(parts[i]))
                    throw new OtterWebTemplateProblem($"{template.Name}, line {line}: {so} has no value called {parts[i]}.");
                value = thing.ReadProperty(parts[i]);
            }
            return value;
        }
    }

    // ---- escaping and URLs ----

    public static string Escape(string text)
    {
        if (text.AsSpan().IndexOfAny("&<>\"'") < 0) return text;
        var escaped = new StringBuilder(text.Length + 16);
        foreach (char c in text)
        {
            switch (c)
            {
                case '&': escaped.Append("&amp;"); break;
                case '<': escaped.Append("&lt;"); break;
                case '>': escaped.Append("&gt;"); break;
                case '"': escaped.Append("&quot;"); break;
                case '\'': escaped.Append("&#39;"); break;
                default: escaped.Append(c); break;
            }
        }
        return escaped.ToString();
    }

    // A URL a browser would follow without running anything: relative, or
    // http, https or mailto. Read as the browser reads it: spaces and
    // controls at the ends dropped, tabs and line breaks removed anywhere.
    public static bool SafeUrl(string value)
    {
        var clean = new StringBuilder(value.Length);
        foreach (char c in value) if (c is not ('\t' or '\n' or '\r')) clean.Append(c);
        string url = clean.ToString().Trim(Enumerable.Range(0, 0x21).Select(i => (char)i).ToArray());
        int colon = url.IndexOf(':');
        if (colon < 0) return true;
        int boundary = url.IndexOfAny(new[] { '/', '?', '#' });
        if (boundary >= 0 && boundary < colon) return true;   // a relative path with ":" later
        string scheme = url[..colon].ToLowerInvariant();
        return scheme is "http" or "https" or "mailto";
    }

    // srcset-like lists: every URL in them must be safe.
    public static bool SafeUrlList(string value) =>
        value.Split(new[] { ',', ' ', '\t', '\n', '\r', '\f' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(part => !System.Text.RegularExpressions.Regex.IsMatch(part, @"^\d+(\.\d+)?[wx]$"))
            .All(SafeUrl);
}
