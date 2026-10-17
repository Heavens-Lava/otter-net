"""Generates src/Otter.Web/Templates.g.cs from otterPS 1.0.1's sources.

The web compiler's large HTML, CSS and JavaScript blocks are PowerShell
here-strings in otterPS (src/Otter.Web.psm1, src/Otter.Compiler.JavaScript.psm1).
They are copied here mechanically - never retyped - so the generated pages
stay byte-identical: single-quoted here-strings verbatim, double-quoted ones
with PowerShell's escapes resolved and each $variable or $(...) turned into a
method parameter.

    python3 tools/web-templates.py ../otterPS-1.0.1 > src/Otter.Web/Templates.g.cs
"""
import re
import sys

ROOT = sys.argv[1]

# (file, line of the opening @" or @', C# name, {subexpression: parameter})
TEMPLATES = [
    ('src/Otter.Compiler.JavaScript.psm1', 102, 'JsFromCsvFunc', {}),
    ('src/Otter.Compiler.JavaScript.psm1', 153, 'JsToCsvFunc', {}),
    ('src/Otter.Compiler.JavaScript.psm1', 192, 'JsParseExactDateFunc', {}),
    ('src/Otter.Compiler.JavaScript.psm1', 228, 'JsBytesFromHexFunc', {}),
    ('src/Otter.Compiler.JavaScript.psm1', 240, 'JsBytesFromBase64Func', {}),
    ('src/Otter.Compiler.JavaScript.psm1', 253, 'JsBytesToHexFunc', {}),
    ('src/Otter.Compiler.JavaScript.psm1', 261, 'JsBytesToBase64Func', {}),
    ('src/Otter.Compiler.JavaScript.psm1', 269, 'JsBytesToTextFunc', {}),
    ('src/Otter.Compiler.JavaScript.psm1', 306, 'JsXmlChildElementsFuncTemplate', {}),
    ('src/Otter.Compiler.JavaScript.psm1', 312, 'JsXmlFindByNameFuncTemplate', {}),
    ('src/Otter.Compiler.JavaScript.psm1', 320, 'JsXmlPrettyFunc', {}),
    ('src/Otter.Compiler.JavaScript.psm1', 417, 'JsCryptoRuntime', {}),
    ('src/Otter.Web.psm1', 297, 'DeclarativeListener', {}),
    ('src/Otter.Web.psm1', 461, 'WebRuntimeUiJs', {}),
    ('src/Otter.Web.psm1', 1063, 'WindowElement', {'Escape-OtterHtmlText -Text $appTitle': 'title'}),
    ('src/Otter.Web.psm1', 1076, 'PageElement', {}),
    ('src/Otter.Web.psm1', 1088, 'CardElement', {}),
    ('src/Otter.Web.psm1', 1117, 'RunnableTextElement', {}),
    ('src/Otter.Web.psm1', 1164, 'SelectElement', {}),
    ('src/Otter.Web.psm1', 1262, 'RowElement', {}),
    ('src/Otter.Web.psm1', 1312, 'ColumnElement', {}),
    ('src/Otter.Web.psm1', 1319, 'ScrollElement', {}),
    ('src/Otter.Web.psm1', 1450, 'DeriveInit', {}),
    ('src/Otter.Web.psm1', 1473, 'WatcherInit', {}),
    ('src/Otter.Web.psm1', 1518, 'DragHandler', {'$when.EventName': 'eventName'}),
    ('src/Otter.Web.psm1', 1551, 'WhenHandler', {}),
    ('src/Otter.Web.psm1', 1591, 'RunnableCss', {}),
    ('src/Otter.Web.psm1', 1604, 'RunnableRuntimeJs', {}),
    ('src/Otter.Web.psm1', 1651, 'DragDropRuntimeJs', {}),
    ('src/Otter.Web.psm1', 1710, 'Page', {'Escape-OtterHtmlText -Text $appTitle': 'title'}),
]

ESC = {'n': '\n', 't': '\t', 'r': '\r', '0': '\0', 'a': '\a', 'b': '\b', 'f': '\f', 'v': '\v', 'e': '\x1b'}


def here_string(lines, start):
    opener = lines[start - 1].rstrip()
    quote = opener[-1]
    assert opener.endswith('@' + quote), opener
    body = []
    i = start
    while not lines[i].startswith(quote + '@'):
        body.append(lines[i])
        i += 1
    return quote, '\n'.join(body)


def segments(text, subs):
    """A double-quoted here-string's content as literal text and holes."""
    out, buf, i = [], [], 0
    def flush():
        if buf:
            out.append(('lit', ''.join(buf)))
            buf.clear()
    while i < len(text):
        ch = text[i]
        if ch == '`' and i + 1 < len(text):
            buf.append(ESC.get(text[i + 1], text[i + 1]))
            i += 2
            continue
        if ch == '$' and i + 1 < len(text):
            nxt = text[i + 1]
            if nxt == '(':
                depth, j = 0, i + 1
                while True:
                    if text[j] == '(':
                        depth += 1
                    elif text[j] == ')':
                        depth -= 1
                        if depth == 0:
                            break
                    j += 1
                expr = text[i + 2:j]
                if expr not in subs:
                    raise SystemExit(f'unmapped subexpression: $({expr})')
                flush()
                out.append(('var', subs[expr]))
                i = j + 1
                continue
            if nxt == '{':
                j = text.index('}', i)
                flush()
                out.append(('var', text[i + 2:j]))
                i = j + 1
                continue
            m = re.match(r'(?:script:)?[A-Za-z_][A-Za-z0-9_]*', text[i + 1:])
            if m:
                flush()
                out.append(('var', m.group(0).replace('script:', '')))
                i += 1 + m.end()
                continue
        buf.append(ch)
        i += 1
    flush()
    return out


def raw(text, dollars):
    """A C# raw string literal for text (holes already written as {{...}})."""
    quotes = 3
    while '"' * quotes in text:
        quotes += 1
    q = '"' * quotes
    return ('$' * dollars) + q + '\n' + text + '\n' + q


def braces_needed(text):
    longest = max((len(m) for m in re.findall(r'\{+|\}+', text)), default=0)
    return longest + 1


def main():
    print('// <auto-generated>')
    print('// Generated by tools/web-templates.py from otterPS 1.0.1 (src/Otter.Web.psm1,')
    print('// src/Otter.Compiler.JavaScript.psm1): the here-strings, copied mechanically.')
    print('// Do not edit by hand.')
    print('// </auto-generated>')
    print('namespace Otter.Web;')
    print('')
    print('static class Templates')
    print('{')
    cache = {}
    for file, line, name, subs in TEMPLATES:
        if file not in cache:
            cache[file] = open(f'{ROOT}/{file}', encoding='utf-8').read().split('\n')
        quote, body = here_string(cache[file], line)
        print(f'    // {file}, line {line}')
        if quote == "'":
            print(f'    public const string {name} = ' + raw(body, 0).replace('\n', '\n') + ';')
            print('')
            continue
        segs = segments(body, subs)
        params = []
        for kind, value in segs:
            if kind == 'var' and value not in params:
                params.append(value)
        literal = ''.join(v for k, v in segs if k == 'lit')
        n = braces_needed(literal)
        open_b, close_b = '{' * n, '}' * n
        text = ''.join(v if k == 'lit' else open_b + v + close_b for k, v in segs)
        if not params:
            print(f'    public const string {name} = ' + raw(text, 0) + ';')
        else:
            args = ', '.join(f'string {p}' for p in params)
            print(f'    public static string {name}({args}) => ' + raw(text, n) + ';')
        print('')
    print('}')


main()
