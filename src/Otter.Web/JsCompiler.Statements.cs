using System.Text.RegularExpressions;
using Otter.Language;

namespace Otter.Web;

sealed partial class JsCompiler
{
    // Set-OtterJsRuntimeUiNames: UI resources created while the page runs,
    // and the page's own top-level ones (rendered to static HTML).
    HashSet<string> runtimeUiNames = new(StringComparer.Ordinal);
    HashSet<string> staticUiNames = new(StringComparer.Ordinal);

    // D130/D133: the names each loop pass and handler run owns, and the
    // stack of those in effect while a body compiles.
    Dictionary<Node, HashSet<string>> loopPassNames = new(ReferenceEqualityComparer.Instance);
    readonly List<HashSet<string>> loopPassStack = new();

    public void SetRuntimeUiNames(IEnumerable<string> names, IEnumerable<string> staticNames)
    {
        runtimeUiNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (string name in names) if (!string.IsNullOrEmpty(name)) runtimeUiNames.Add(name);
        staticUiNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (string name in staticNames) if (!string.IsNullOrEmpty(name)) staticUiNames.Add(name);
    }

    public (string[] Names, string[] StaticNames) GetRuntimeUiNames() => (runtimeUiNames.ToArray(), staticUiNames.ToArray());

    public bool IsRuntimeUiName(string name) => runtimeUiNames.Contains(name);

    // Set-OtterJsLoopPassNames
    public void SetLoopPassNames(IReadOnlyList<Stmt> statements)
    {
        loopPassStack.Clear();
        loopPassNames = LoopPasses.Names(statements);
    }

    // Enter-OtterJsHandlerRun / Exit-OtterJsHandlerRun
    public bool EnterHandlerRun(Node handler)
    {
        if (!loopPassNames.TryGetValue(handler, out var runNames) || runNames.Count == 0) return false;
        loopPassStack.Add(runNames);
        return true;
    }

    public void ExitHandlerRun(bool entered)
    {
        if (entered) loopPassStack.RemoveAt(loopPassStack.Count - 1);
    }

    // Get-OtterJsUiRef
    string UiRef(string name) =>
        runtimeUiNames.Contains(name) ? $"(typeof {name} !== 'undefined' && {name} && {name}.__otterUi ? {name} : '{name}')" : $"'{name}'";

    // Get-OtterJsUiRefForNode
    string UiRefForNode(Expr node)
    {
        if (node is VariableExpr variable && (runtimeUiNames.Contains(variable.Name) || staticUiNames.Contains(variable.Name))) return UiRef(variable.Name);
        return Expression(node);
    }

    static string Pad(int indent) => new(' ', 2 * indent);

    // `$LocalNames -and $LocalNames.Contains($name)`: an empty set is false.
    static bool IsLocal(HashSet<string>? localNames, string? name) =>
        localNames is { Count: > 0 } && name != null && localNames.Contains(name);

    static string SetVariable(string pad, string name, string valueJs) =>
        $"{pad}if (typeof otterSetState === 'function' && typeof otterState !== 'undefined' && ('{name}' in otterState)) {{ otterSetState('{name}', {valueJs}); }} else {{ window.{name} = {valueJs}; }}";

    // A write to a variable: a real local inside a function, otherwise the
    // page's state or window.
    static string Write(string pad, HashSet<string>? localNames, string name, string valueJs) =>
        IsLocal(localNames, name) ? $"{pad}{name} = {valueJs};" : SetVariable(pad, name, valueJs);

    static bool IsLoop(Node node) => node is ForEachStmt or CountStmt or RepeatStmt or WhileStmt;

    // `run ...`, `run command ... over ssh ...`: the hook's result into the
    // target when there is one.
    static string HookResult(string pad, HashSet<string>? localNames, string? target, string valueJs) =>
        string.IsNullOrEmpty(target) ? $"{pad}{valueJs};" : Write(pad, localNames, target, valueJs);

    // A hook's array of plain objects as a list of things (D70, D79).
    static string ThingList(string pad, string inner, HashSet<string>? localNames, string target, string callJs, string typeName) =>
        Lines(
            $"{pad}{{",
            $"{inner}const _raw = {callJs};",
            $"{inner}const _value = (Array.isArray(_raw) ? _raw : []).map((v) => {{ const props = {{}}; const order = []; if (v) {{ for (const k of Object.keys(v)) {{ props[k] = v[k]; order.push(k); }} }} return {{ __otterThing: true, typeName: '{typeName}', props: props, order: order }}; }});",
            Write(inner, localNames, target, "_value"),
            $"{pad}}}");

    // PowerActionStmt.Action as otterPS's parser writes it.
    static string PowerActionWord(PowerAction action) => action switch
    {
        PowerAction.Lock => "lock",
        PowerAction.SignOut => "signOut",
        PowerAction.Restart => "restart",
        _ => "shutDown",
    };

    // Get-OtterJsHttpOptionsSetup
    (List<string> Setup, string OptsVar, string? Teardown) HttpOptionsSetup(HttpOptions? options, string baseOptsJs, string inner)
    {
        var setup = new List<string>();
        if (options == null) return (setup, baseOptsJs, null);
        bool needsOptsVar = options.Headers.Count > 0 || options.WithCookies != null || options.FollowRedirects != null || options.TimeoutSeconds != null;
        if (!needsOptsVar) return (setup, baseOptsJs, null);
        string init = baseOptsJs.Length > 0 ? baseOptsJs : "{}";
        setup.Add($"{inner}const _opts = Object.assign({{}}, {init});");
        if (options.Headers.Count > 0)
        {
            var entries = options.Headers.Select(h => $"[String({Expression(h.Name)})]: String({Expression(h.Value)})");
            setup.Add($"{inner}_opts.headers = Object.assign({{}}, _opts.headers, {{ {string.Join(", ", entries)} }});");
        }
        if (options.WithCookies == true) setup.Add($"{inner}_opts.credentials = 'include';");
        else if (options.WithCookies == false) setup.Add($"{inner}_opts.credentials = 'omit';");
        if (options.FollowRedirects == false) setup.Add($"{inner}_opts.redirect = 'manual';");
        string? teardown = null;
        if (options.TimeoutSeconds != null)
        {
            string timeoutJs = Expression(options.TimeoutSeconds);
            setup.Add($"{inner}const _ac = new AbortController(); _opts.signal = _ac.signal;");
            setup.Add($"{inner}const _timer = setTimeout(() => _ac.abort(), Number({timeoutJs}) * 1000);");
            teardown = "clearTimeout(_timer);";
        }
        return (setup, "_opts", teardown);
    }

    // HttpGet / HttpPost / HttpPut / HttpDelete: a block of its own, the
    // response read into the target. Only `get` may have no options at all
    // (then `fetch(url)`).
    string HttpRequest(string pad, string inner, HttpOptions? options, string baseOptsJs, string urlJs, string? target, string readBody, bool allowNoOptions)
    {
        var (setup, optsVar, teardown) = HttpOptionsSetup(options, baseOptsJs, inner);
        var lines = new List<string> { $"{pad}{{" };
        lines.AddRange(setup);
        string fetchCall = optsVar.Length > 0 ? $"fetch({urlJs}, {optsVar})" : $"fetch({urlJs})";
        if (teardown != null)
        {
            lines.Add($"{inner}let res;");
            lines.Add($"{inner}try {{");
            lines.Add($"{inner}  res = await {fetchCall};");
            lines.Add($"{inner}}} catch (_err) {{");
            lines.Add($"{inner}  if (_err && _err.name === 'AbortError') {{ throw new Error('The HTTP request timed out.'); }}");
            lines.Add($"{inner}  throw _err;");
            lines.Add($"{inner}}} finally {{");
            lines.Add($"{inner}  {teardown}");
            lines.Add($"{inner}}}");
        }
        else
        {
            lines.Add($"{inner}const res = await {fetchCall};");
        }
        if (allowNoOptions || !string.IsNullOrEmpty(target))
        {
            lines.Add($"{inner}const {target} = await {readBody}; window.{target} = {target};");
        }
        lines.Add($"{pad}}}");
        return string.Join("\n", lines);
    }

    // HttpStart (D116B): the request object the page's handlers listen on.
    string HttpStart(HttpStartStmt start, string pad, string inner)
    {
        string url = Expression(start.Url);
        string dataJs = start.Data != null ? Expression(start.Data) : "null";
        string asJsonJs = start.AsJson ? "true" : "false";
        string method = start.Method;
        string target = start.TargetName;
        var (setup, optsVar, _) = HttpOptionsSetup(start.Options, $"{{ method: '{method}' }}", inner);
        var lines = new List<string> { $"{pad}{{" };
        lines.AddRange(setup);
        string optsArg = optsVar.Length > 0 ? optsVar : $"{{ method: '{method}' }}";
        lines.Add($"{inner}const {target} = ((_m, _u, _d, _j, _o) => {{");
        lines.Add($"{inner}  const _ctrl = new AbortController();");
        lines.Add($"{inner}  const _req = {{");
        lines.Add($"{inner}    __otterHttpRequest: true, method: _m, url: _u, state: 'pending', response: null, error: null, status: 0,");
        lines.Add($"{inner}    ctrl: _ctrl, handlers: [], retainedEvent: null, isTimeout: false,");
        lines.Add($"{inner}    cancel() {{");
        lines.Add($"{inner}      if (this.state !== 'pending') return;");
        lines.Add($"{inner}      this.state = 'cancelled';");
        lines.Add($"{inner}      try {{ this.ctrl.abort(); }} catch (_) {{}}");
        lines.Add($"{inner}      this._emit('cancel', {{}});");
        lines.Add($"{inner}    }},");
        lines.Add($"{inner}    _emit(kind, ctx) {{");
        lines.Add($"{inner}      this.retainedEvent = {{ kind, ctx }};");
        lines.Add($"{inner}      for (const h of this.handlers) {{ if (h.kind === kind && !h.fired) {{ h.fired = true; h.fn(ctx); }} }}");
        lines.Add($"{inner}    }},");
        lines.Add($"{inner}    on(kind, fn) {{");
        lines.Add($"{inner}      const h = {{ kind, fn, fired: false }};");
        lines.Add($"{inner}      this.handlers.push(h);");
        lines.Add($"{inner}      if (this.retainedEvent && this.retainedEvent.kind === kind) {{ h.fired = true; fn(this.retainedEvent.ctx); }}");
        lines.Add($"{inner}    }}");
        lines.Add($"{inner}  }};");
        lines.Add($"{inner}  const _fetchOpts = Object.assign({{}}, _o, {{ signal: _ctrl.signal }});");
        lines.Add($"{inner}  if (_m === 'POST' || _m === 'PUT') {{");
        lines.Add($"{inner}    if (_j || (_d && typeof _d === 'object')) {{");
        lines.Add($"{inner}      _fetchOpts.body = JSON.stringify(_d);");
        lines.Add($"{inner}      if (!_fetchOpts.headers) _fetchOpts.headers = {{}};");
        lines.Add($"{inner}      _fetchOpts.headers['Content-Type'] = 'application/json';");
        lines.Add($"{inner}    }} else {{");
        lines.Add($"{inner}      _fetchOpts.body = (_d != null ? String(_d) : '');");
        lines.Add($"{inner}    }}");
        lines.Add($"{inner}  }}");
        lines.Add($"{inner}  fetch(_u, _fetchOpts).then(async (res) => {{");
        lines.Add($"{inner}    if (_req.state === 'cancelled') return;");
        lines.Add($"{inner}    _req.status = res.status;");
        lines.Add($"{inner}    let body = _j ? await res.json() : await res.text();");
        lines.Add($"{inner}    _req.response = body;");
        lines.Add($"{inner}    _req.state = 'completed';");
        lines.Add($"{inner}    _req._emit('complete', {{ response: body }});");
        lines.Add($"{inner}  }}).catch((err) => {{");
        lines.Add($"{inner}    if (_req.state === 'cancelled') return;");
        lines.Add($"{inner}    _req.state = 'failed';");
        lines.Add($"{inner}    const msg = (err && err.name === 'AbortError') ? 'The HTTP request timed out.' : ((err && err.message) ? err.message : String(err));");
        lines.Add($"{inner}    _req.error = msg;");
        lines.Add($"{inner}    _req._emit('error', {{ error: msg, netError: msg }});");
        lines.Add($"{inner}  }});");
        lines.Add($"{inner}  return _req;");
        lines.Add($"{inner}}})('{method}', {url}, {dataJs}, {asJsonJs}, {optsArg}); window.{target} = {target};");
        lines.Add($"{pad}}}");
        return string.Join("\n", lines);
    }

    // WebSocketEvent (D106, D116B): a handler on a websocket or a started
    // request. TCP and UDP events have no web compilation.
    string NetworkEvent(NetworkEventStmt networkEvent, int indent, string pad, HashSet<string>? localNames)
    {
        if (networkEvent.EventKind is NetworkEventKind.Connect or NetworkEventKind.Data or NetworkEventKind.Connection)
        {
            throw WebError("TCP/UDP events (\"on connect of\", \"on data from\", \"on connection to\") are not supported on the web target. Use a websocket (\"on open of\", \"on message from\").", networkEvent.Line);
        }
        string socketJs = Expression(networkEvent.Socket);
        var bodyLines = new List<string>();
        foreach (Stmt s in networkEvent.Body)
        {
            string line = Statement(s, indent + 2, localNames);
            if (line.Length > 0) bodyLines.Add(line);
        }
        string bodyJs = string.Join("\n", bodyLines);
        const string Prev = "const _prev = (typeof _otterCurrentWsContext !== 'undefined' ? _otterCurrentWsContext : null);";
        return networkEvent.EventKind switch
        {
            NetworkEventKind.Open => $"{pad}((_s) => {{ if (!_s || typeof _s !== 'object' || !_s.__otterWebSocket) {{ throw new Error('I can only listen for a websocket event on a websocket, but this is something else.'); }} _s.ws.addEventListener('open', async () => {{\n{bodyJs}\n{pad}  }}); }})({socketJs});",
            NetworkEventKind.Message => $"{pad}((_s) => {{ if (!_s || typeof _s !== 'object' || !_s.__otterWebSocket) {{ throw new Error('I can only listen for a websocket event on a websocket, but this is something else.'); }} _s.ws.addEventListener('message', async (_evt) => {{ {Prev} const _msg = (_evt.data instanceof ArrayBuffer) ? {{ __otterBytes: true, value: new Uint8Array(_evt.data), toString() {{ return '<' + this.value.length + ' bytes>'; }} }} : _evt.data; _otterCurrentWsContext = {{ message: _msg }}; try {{\n{bodyJs}\n{pad}  }} finally {{ _otterCurrentWsContext = _prev; }} }}); }})({socketJs});",
            NetworkEventKind.Close => $"{pad}((_s) => {{ if (!_s || typeof _s !== 'object' || !_s.__otterWebSocket) {{ throw new Error('I can only listen for a websocket event on a websocket, but this is something else.'); }} _s.ws.addEventListener('close', async (_evt) => {{ {Prev} _otterCurrentWsContext = {{ closeCode: _evt.code, closeReason: _evt.reason || '', closeWasClean: _evt.wasClean }}; try {{\n{bodyJs}\n{pad}  }} finally {{ _otterCurrentWsContext = _prev; }} }}); }})({socketJs});",
            NetworkEventKind.Complete => $"{pad}((_s) => {{ if (!_s || typeof _s !== 'object' || !_s.__otterHttpRequest) {{ throw new Error('I can only listen for complete on an HTTP request, but this is something else.'); }} _s.on('complete', async (_ctx) => {{ {Prev} _otterCurrentWsContext = _ctx; try {{\n{bodyJs}\n{pad}  }} finally {{ _otterCurrentWsContext = _prev; }} }}); }})({socketJs});",
            NetworkEventKind.Cancel => $"{pad}((_s) => {{ if (!_s || typeof _s !== 'object' || !_s.__otterHttpRequest) {{ throw new Error('I can only listen for cancel on an HTTP request, but this is something else.'); }} _s.on('cancel', async (_ctx) => {{ {Prev} _otterCurrentWsContext = _ctx; try {{\n{bodyJs}\n{pad}  }} finally {{ _otterCurrentWsContext = _prev; }} }}); }})({socketJs});",
            _ => $"{pad}((_s) => {{ if (!_s || typeof _s !== 'object' || (!_s.__otterWebSocket && !_s.__otterHttpRequest)) {{ throw new Error('I can only listen for an error event on a websocket or http request, but this is something else.'); }} if (_s.__otterHttpRequest) {{ _s.on('error', async (_ctx) => {{ {Prev} _otterCurrentWsContext = _ctx; try {{\n{bodyJs}\n{pad}  }} finally {{ _otterCurrentWsContext = _prev; }} }}); return; }} _s.ws.addEventListener('error', async (_evt) => {{ {Prev} _otterCurrentWsContext = {{ error: 'WebSocket error' }}; try {{\n{bodyJs}\n{pad}  }} finally {{ _otterCurrentWsContext = _prev; }} }}); }})({socketJs});",
        };
    }


    // ConvertTo-OtterJsStatement
    public string Statement(Stmt stmt, int indent = 2, HashSet<string>? localNames = null, HashSet<string>? knownGlobals = null, bool skipRuntimeUi = false)
    {
        HashSet<string>? passNames = IsLoop(stmt) && loopPassNames.TryGetValue(stmt, out var names) ? names : null;
        bool pushed = false;
        if (passNames is { Count: > 0 })
        {
            loopPassStack.Add(passNames);
            pushed = true;
        }
        try { return StatementCore(stmt, indent, localNames, knownGlobals, skipRuntimeUi); }
        finally { if (pushed) loopPassStack.RemoveAt(loopPassStack.Count - 1); }
    }

    // A statement block, each statement on its own line(s).
    string Block(IEnumerable<Stmt>? body, int indent, HashSet<string>? localNames) =>
        body == null ? "" : string.Join("\n", body.Select(s => Statement(s, indent, localNames)));

    static string Lines(params string[] lines) => string.Join("\n", lines);

    // ConvertTo-OtterJsStatementCore
    string StatementCore(Stmt stmt, int indent, HashSet<string>? localNames, HashSet<string>? knownGlobals, bool skipRuntimeUi)
    {
        string pad = Pad(indent);
        string inner = Pad(indent + 1);
        switch (stmt)
        {
            case AwaitStmt awaitStmt:
                return $"{pad}await {Expression(awaitStmt.Expression)};";
            case AssignStmt { Target: PropertyAccessExpr access } assign:
                return AssignProperty(access, assign.Value, pad, inner);
            case AssignStmt assign:
            {
                string varName = assign.Target is VariableExpr variable ? variable.Name : "";
                return Write(pad, localNames, varName, Expression(assign.Value));
            }
            case MathIntoStmt mathInto:
                return Write(pad, localNames, mathInto.Target, Expression(mathInto.Expression));
            case SayStmt say:
            {
                if (say.Color != null) throw WebError("`say ... in color` is not supported on the web target yet.", say.Line);
                return $"{pad}otterSay({string.Join(", ", say.Parts.Select(Expression))});";
            }
            case AskStmt ask:
            {
                if (ask.Secret) throw WebError("`ask secretly` is not supported on the web target yet.", ask.Line);
                string promptJs = Expression(ask.Prompt);
                string coerced = $"(() => {{ const _raw = window.prompt(String({promptJs})); const _text = (_raw === null) ? '' : _raw; const _trimmed = _text.trim(); if (_trimmed === 'true') {{ return true; }} if (_trimmed === 'false') {{ return false; }} if (_trimmed.length > 0 && !Number.isNaN(Number(_trimmed))) {{ return Number(_trimmed); }} return _text; }})()";
                return Write(pad, localNames, ask.Name, coerced);
            }
            case IfStmt ifStmt:
            {
                var lines = new List<string>();
                bool first = true;
                foreach (IfBranch branch in ifStmt.Branches)
                {
                    string cond = Expression(branch.Condition);
                    lines.Add(first ? $"{pad}if ({cond}) {{" : $"{pad}else if ({cond}) {{");
                    foreach (Stmt s in branch.Body) lines.Add(Statement(s, indent + 1, localNames));
                    lines.Add($"{pad}}}");
                    first = false;
                }
                if (ifStmt.ElseBody is { Count: > 0 })
                {
                    lines.Add($"{pad}else {{");
                    foreach (Stmt s in ifStmt.ElseBody) lines.Add(Statement(s, indent + 1, localNames));
                    lines.Add($"{pad}}}");
                }
                return string.Join("\n", lines);
            }
            case WhileStmt whileStmt:
            {
                var lines = new List<string> { $"{pad}while ({Expression(whileStmt.Condition)}) {{" };
                foreach (Stmt s in whileStmt.Body) lines.Add(Statement(s, indent + 1, localNames));
                lines.Add($"{pad}}}");
                return string.Join("\n", lines);
            }
            case CountStmt count:
            {
                string fromJs = Expression(count.From);
                string toJs = Expression(count.To);
                string varName = count.VariableName;
                string bodyIndent = Pad(indent + 2);
                var lines = new List<string>
                {
                    $"{pad}{{",
                    $"{inner}const _from = Number({fromJs});",
                    $"{inner}const _to = Number({toJs});",
                    $"{inner}const _step = _from <= _to ? 1 : -1;",
                    $"{inner}for (let _n = _from; (_step > 0 && _n <= _to) || (_step < 0 && _n >= _to); _n += _step) {{",
                    Write(bodyIndent, localNames, varName, "_n"),
                };
                foreach (Stmt s in count.Body) lines.Add(Statement(s, indent + 2, localNames));
                lines.Add($"{inner}}}");
                lines.Add($"{pad}}}");
                return string.Join("\n", lines);
            }
            case ListDefStmt list:
            {
                var lines = new List<string> { $"{pad}{{", $"{inner}const _items = [];" };
                foreach (Expr item in list.Items) lines.Add($"{inner}_items.push({Expression(item)});");
                lines.Add(Write(inner, localNames, list.Name, "_items"));
                lines.Add($"{pad}}}");
                return string.Join("\n", lines);
            }
            case TypeDefStmt typeDef:
            {
                string fieldsJs = string.Join(", ", typeDef.FieldNames.Select(f => $"'{f}'"));
                string typeObj = $"{{ __otterType: true, typeName: '{typeDef.TypeName}', fieldNames: [{fieldsJs}] }}";
                return Write(pad, localNames, typeDef.TypeName, typeObj);
            }
            case ObjectDefStmt objectDef:
                return ObjectDef(objectDef, indent, localNames, knownGlobals, skipRuntimeUi);
            case SortStmt sort:
                return $"{pad}{sort.Target}.sort((_a, _b) => {{ const _an = (typeof _a === 'number') || (typeof _a === 'string' && _a.trim() !== '' && !Number.isNaN(Number(_a))); const _bn = (typeof _b === 'number') || (typeof _b === 'string' && _b.trim() !== '' && !Number.isNaN(Number(_b))); if (_an && _bn) {{ return Number(_a) - Number(_b); }} return String(_a) < String(_b) ? -1 : (String(_a) > String(_b) ? 1 : 0); }});";
            case ReverseStmt reverse:
                return $"{pad}{reverse.Target}.reverse();";
            case JoinStmt join:
            {
                string subjectJs = Expression(join.Subject);
                string separatorJs = Expression(join.Separator);
                return Lines(
                    $"{pad}{{",
                    $"{inner}const _subj = {subjectJs};",
                    $"{inner}const _sep = String({separatorJs});",
                    $"{inner}const _joined = Array.isArray(_subj) ? _subj.map((_x) => String(_x)).join(_sep) : String(_subj);",
                    Write(inner, localNames, join.Target, "_joined"),
                    $"{pad}}}");
            }
            case FindStmt find:
            {
                string collJs = Expression(find.Collection);
                string conditionJs = Expression(find.Condition);
                return Lines(
                    $"{pad}{{",
                    $"{inner}let _found = null;",
                    $"{inner}for (const {find.ItemName} of ({collJs} || [])) {{",
                    $"{inner}  if ({conditionJs}) {{ _found = {find.ItemName}; break; }}",
                    $"{inner}}}",
                    SetVariable(inner, find.Target, "_found"),
                    $"{pad}}}");
            }
            case AddToStmt add:
                return AddOrRemove(add.Target, Expression(add.Amount), true, pad, inner, localNames);
            case RemoveFromStmt remove:
                return AddOrRemove(remove.Target, Expression(remove.Amount), false, pad, inner, localNames);
            case RepeatStmt repeat:
            {
                var lines = new List<string> { $"{pad}for (let _i = 0; _i < {Expression(repeat.Count)}; _i++) {{" };
                foreach (Stmt s in repeat.Body) lines.Add(Statement(s, indent + 1, localNames));
                lines.Add($"{pad}}}");
                return string.Join("\n", lines);
            }
            case ForEachStmt forEach:
            {
                string collJs = Expression(forEach.Collection);
                string bodyIndent = Pad(indent + 2);
                var lines = new List<string>
                {
                    $"{pad}{{",
                    $"{inner}const _coll = ({collJs});",
                    $"{inner}if (!Array.isArray(_coll)) {{ throw new Error('I can only go through a list, but this is something else.'); }}",
                    $"{inner}const _snapshot = _coll.slice();",
                    $"{inner}for (const _item of _snapshot) {{",
                    Write(bodyIndent, localNames, forEach.VariableName, "_item"),
                };
                foreach (Stmt s in forEach.Body) lines.Add(Statement(s, indent + 2, localNames));
                lines.Add($"{inner}}}");
                lines.Add($"{pad}}}");
                return string.Join("\n", lines);
            }
            case TryStmt tryStmt:
            {
                var lines = new List<string> { $"{pad}try {{" };
                foreach (Stmt s in tryStmt.Body) lines.Add(Statement(s, indent + 1, localNames));
                lines.Add($"{pad}}} catch (_err) {{");
                if (!string.IsNullOrEmpty(tryStmt.ErrorTarget))
                {
                    const string reasonExpr = "(_err && _err.message) ? _err.message : String(_err)";
                    lines.Add(IsLocal(localNames, tryStmt.ErrorTarget)
                        ? $"{inner}{tryStmt.ErrorTarget} = {reasonExpr};"
                        : $"{inner}window.{tryStmt.ErrorTarget} = {reasonExpr};");
                }
                if (tryStmt.OtherwiseBody != null)
                {
                    foreach (Stmt s in tryStmt.OtherwiseBody) lines.Add(Statement(s, indent + 1, localNames));
                }
                lines.Add($"{pad}}}");
                return string.Join("\n", lines);
            }
            case FailStmt fail:
                return $"{pad}throw new Error(String({Expression(fail.Message)}));";
            case ReturnStmt ret:
                return ret.Value != null ? $"{pad}return {Expression(ret.Value)};" : $"{pad}return;";
            case ReadFileStmt read:
            {
                string pathJs = Expression(read.Path);
                return Write(pad, localNames, read.Target, $"await otterReadFile({pathJs})");
            }
            case WriteFileStmt write:
            {
                string contentJs = Expression(write.Content);
                string pathJs = Expression(write.Path);
                return $"{pad}await otterWriteFile({pathJs}, {contentJs}, {(write.Atomic ? "true" : "false")});";
            }
            case RunCommandStmt run:
            {
                string cmdJs = Expression(run.Target);
                if (!string.IsNullOrEmpty(run.ResultTarget)) return Write(pad, localNames, run.ResultTarget, $"await otterRunCommand({cmdJs})");
                return $"{pad}await otterRunCommand({cmdJs});";
            }
            case GetEntriesStmt entries:
            {
                string folderJs = Expression(entries.Folder);
                string subfoldersJs = entries.IncludeSubfolders ? "true" : "false";
                string hook = entries.Folders ? "otterGetFolders" : "otterGetFiles";
                return Write(pad, localNames, entries.Target, $"await {hook}({folderJs}, {subfoldersJs})");
            }
            case AppendFileStmt append:
            {
                string pathJs = Expression(append.Path);
                string contentJs = Expression(append.Content);
                return $"{pad}await otterAppendFile({pathJs}, {contentJs});";
            }
            case TransferStmt transfer:
            {
                string sourceJs = Expression(transfer.Source);
                string destinationJs = Expression(transfer.Destination);
                string hook = transfer.Action switch
                {
                    FileAction.CopyFile => "otterCopyFile",
                    FileAction.MoveFile => "otterMoveFile",
                    FileAction.CopyFolder => "otterCopyFolder",
                    FileAction.MoveFolder => "otterMoveFolder",
                    FileAction.ZipFolder => "otterZipFolder",
                    _ => "otterUnzipFile",
                };
                return $"{pad}await {hook}({sourceJs}, {destinationJs});";
            }
            case DeleteFileStmt deleteFile:
                return $"{pad}await otterDeleteFile({Expression(deleteFile.Path)});";
            case CreateFolderStmt createFolder:
                return $"{pad}await otterCreateFolder({Expression(createFolder.Path)});";
            case DeleteFolderStmt deleteFolder:
                return $"{pad}await otterDeleteFolder({Expression(deleteFolder.Path)});";
            case HashTextStmt hash:
            {
                string hashTextJs = Expression(hash.Text);
                string hashAlgoJs = Expression(hash.Algorithm);
                string hashKeyJs = hash.Key != null ? Expression(hash.Key) : "null";
                return Lines(
                    $"{pad}{{",
                    $"{inner}const _digest = await otterHashText({hashTextJs}, {hashAlgoJs}, {hashKeyJs});",
                    Write(inner, localNames, hash.ResultTarget, "_digest"),
                    $"{pad}}}");
            }
            case CipherTextStmt { Decrypt: false } encrypt:
            {
                string textJs = Expression(encrypt.Text);
                string keyJs = Expression(encrypt.Key);
                return Lines(
                    $"{pad}{{",
                    $"{inner}const _cipher = await otterEncryptText({textJs}, {keyJs});",
                    Write(inner, localNames, encrypt.ResultTarget, "_cipher"),
                    $"{pad}}}");
            }
            case CipherTextStmt decrypt:
            {
                string cipherJs = Expression(decrypt.Text);
                string keyJs = Expression(decrypt.Key);
                return Lines(
                    $"{pad}{{",
                    $"{inner}const _plain = await otterDecryptText({cipherJs}, {keyJs});",
                    Write(inner, localNames, decrypt.ResultTarget, "_plain"),
                    $"{pad}}}");
            }
            case GetEnvironmentVariableStmt getVariable:
            {
                string nameJs = Expression(getVariable.Name);
                return Lines(
                    $"{pad}{{",
                    $"{inner}const _val = await otterGetEnv(String({nameJs}));",
                    Write(inner, localNames, getVariable.Target, "_val"),
                    $"{pad}}}");
            }
            case GetSystemFolderStmt systemFolder:
            {
                string folderNameJs = Expression(systemFolder.FolderName);
                return Lines(
                    $"{pad}{{",
                    $"{inner}const _paths = await otterGetSystemPaths();",
                    $"{inner}const _nameOriginal = String({folderNameJs});",
                    $"{inner}const _name = _nameOriginal.toLowerCase();",
                    $"{inner}let _path;",
                    $"{inner}if (_name === 'temp') {{ _path = _paths.tempFolder; }}",
                    $"{inner}else if (_name === 'appdata') {{ _path = _paths.appDataFolder; }}",
                    $"{inner}else if (_name === 'user') {{ _path = _paths.userFolder; }}",
                    $"{inner}else if (_name === 'current') {{ _path = _paths.currentDirectory; }}",
                    $"{inner}else {{ throw new Error('I do not know a system folder called \"' + _nameOriginal + '\".'); }}",
                    Write(inner, localNames, systemFolder.Target, "_path"),
                    $"{pad}}}");
            }
            case SetCurrentDirectoryStmt setDirectory:
                return $"{pad}if (typeof process !== 'undefined' && process.chdir) {{ process.chdir({Expression(setDirectory.Path)}); }}";
            case SetEnvironmentVariableStmt setVariable:
            {
                string nameJs = Expression(setVariable.Name);
                string valJs = Expression(setVariable.Value);
                return $"{pad}if (typeof process !== 'undefined' && process.env) {{ process.env[{nameJs}] = {valJs}; }}";
            }
            case GetKeyStmt getKey:
            {
                string targetJs = Expression(getKey.Target);
                string keyJs = Expression(getKey.Key);
                return Lines(
                    $"{pad}{{",
                    $"{inner}const _owner = {targetJs};",
                    $"{inner}if (!_owner || typeof _owner !== 'object' || !_owner.__otterThing) {{ throw new Error('I can only read from a thing, but this is something else.'); }}",
                    $"{inner}if (_owner.typeName !== 'thing') {{ throw new Error('I can only read properties dynamically on a thing, but this is a ' + _owner.typeName + '.'); }}",
                    $"{inner}const _key = {keyJs};",
                    $"{inner}if (typeof _key !== 'string') {{ throw new Error('I need text for a dynamic key, but this is something else.'); }}",
                    $"{inner}const _val = (_key in _owner.props) ? _owner.props[_key] : null;",
                    Write(inner, localNames, getKey.ResultTarget, "_val"),
                    $"{pad}}}");
            }
            case SetKeyStmt setKey:
            {
                string targetJs = Expression(setKey.Target);
                string keyJs = Expression(setKey.Key);
                string valJs = Expression(setKey.Value);
                return Lines(
                    $"{pad}{{",
                    $"{inner}const _owner = {targetJs};",
                    $"{inner}if (!_owner || typeof _owner !== 'object' || !_owner.__otterThing) {{ throw new Error('I can only write to a thing, but this is something else.'); }}",
                    $"{inner}if (_owner.typeName !== 'thing') {{ throw new Error('I can only write properties dynamically on a thing, but this is a ' + _owner.typeName + '.'); }}",
                    $"{inner}const _key = {keyJs};",
                    $"{inner}if (typeof _key !== 'string') {{ throw new Error('I need text for a dynamic key, but this is something else.'); }}",
                    $"{inner}if (!(_key in _owner.props)) {{ _owner.order.push(_key); }}",
                    $"{inner}_owner.props[_key] = {valJs};",
                    $"{pad}}}");
            }
            case DateAdjustStmt adjust:
                return DateAdjust(adjust, pad, inner, localNames);
            case DateDifferenceStmt difference:
            {
                string startJs = Expression(difference.Start);
                string endJs = Expression(difference.End);
                string diffJs = DateDifferenceExpression($"'{difference.Unit}'", "_s.value", "_e.value");
                return Lines(
                    $"{pad}{{",
                    $"{inner}const _s = {startJs};",
                    $"{inner}const _e = {endJs};",
                    $"{inner}if (!_s || typeof _s !== 'object' || !_s.__otterDate) {{ throw new Error('I can only measure time between two dates, but the first one is something else.'); }}",
                    $"{inner}if (!_e || typeof _e !== 'object' || !_e.__otterDate) {{ throw new Error('I can only measure time between two dates, but the second one is something else.'); }}",
                    $"{inner}const _diff = {diffJs};",
                    Write(inner, localNames, difference.Target, "_diff"),
                    $"{pad}}}");
            }
            case FormatDateStmt format:
            {
                string subjectJs = Expression(format.Subject);
                string formatJs = Expression(format.Format);
                return Lines(
                    $"{pad}{{",
                    $"{inner}const _subject = {subjectJs};",
                    $"{inner}if (!_subject || typeof _subject !== 'object' || !_subject.__otterDate) {{ throw new Error('I can only format a date, but this is something else.'); }}",
                    $"{inner}const _pattern = String({formatJs});",
                    $"{inner}const _d = _subject.value;",
                    $"{inner}const _text = _pattern.replace(/yyyy|MM|dd|HH|mm|ss/g, (_tok) => {{ switch (_tok) {{ case 'yyyy': return String(_d.getFullYear()).padStart(4, '0'); case 'MM': return String(_d.getMonth() + 1).padStart(2, '0'); case 'dd': return String(_d.getDate()).padStart(2, '0'); case 'HH': return String(_d.getHours()).padStart(2, '0'); case 'mm': return String(_d.getMinutes()).padStart(2, '0'); case 'ss': return String(_d.getSeconds()).padStart(2, '0'); }} return _tok; }});",
                    Write(inner, localNames, format.Target, "_text"),
                    $"{pad}}}");
            }
            case DiagnosticStmt diagnostic:
            {
                string label = diagnostic.Level switch { DiagnosticLevel.Warning => "warn", DiagnosticLevel.Problem => "error", _ => "log" };
                string consoleFn = label switch { "warn" => "console.warn", "error" => "console.error", _ => "console.log" };
                if (diagnostic.Parts.Count == 0) return $"{pad}{consoleFn}('{label}: ');";
                string joined = string.Join(" + \" \" + ", diagnostic.Parts.Select(Expression));
                return $"{pad}{consoleFn}('{label}: ' + ({joined}));";
            }
            case RandomNumberStmt random:
            {
                string fromJs = Expression(random.From);
                string toJs = Expression(random.To);
                return Lines(
                    $"{pad}{{",
                    $"{inner}const _fromRaw = {fromJs};",
                    $"{inner}const _toRaw = {toJs};",
                    $"{inner}const _fromOk = typeof _fromRaw === 'number' || (typeof _fromRaw === 'string' && _fromRaw.trim() !== '' && !Number.isNaN(Number(_fromRaw)));",
                    $"{inner}if (!_fromOk) {{ throw new Error('I expected a number for the lowest number but got ' + JSON.stringify(_fromRaw) + '.'); }}",
                    $"{inner}const _toOk = typeof _toRaw === 'number' || (typeof _toRaw === 'string' && _toRaw.trim() !== '' && !Number.isNaN(Number(_toRaw)));",
                    $"{inner}if (!_toOk) {{ throw new Error('I expected a number for the highest number but got ' + JSON.stringify(_toRaw) + '.'); }}",
                    $"{inner}let _from = Math.floor(Number(_fromRaw));",
                    $"{inner}let _to = Math.floor(Number(_toRaw));",
                    $"{inner}if (_from > _to) {{ const _swap = _from; _from = _to; _to = _swap; }}",
                    $"{inner}const _picked = Math.floor(Math.random() * (_to - _from + 1)) + _from;",
                    Write(inner, localNames, random.Target, "_picked"),
                    $"{pad}}}");
            }
            case RandomItemStmt randomItem:
            {
                string collectionJs = Expression(randomItem.Collection);
                return Lines(
                    $"{pad}{{",
                    $"{inner}const _collection = {collectionJs};",
                    $"{inner}if (!Array.isArray(_collection)) {{ throw new Error('I can only pick from a list, but this is something else.'); }}",
                    $"{inner}const _picked = _collection.length > 0 ? _collection[Math.floor(Math.random() * _collection.length)] : null;",
                    Write(inner, localNames, randomItem.Target, "_picked"),
                    $"{pad}}}");
            }
            case ReadJsonStmt readJson:
            {
                string pathJs = Expression(readJson.Path);
                return Lines(
                    $"{pad}{{",
                    $"{inner}const _text = String(await otterReadFile({pathJs}));",
                    $"{inner}if (_text.trim() === '') {{ throw new Error('There is no JSON here to read.'); }}",
                    $"{inner}let _parsed;",
                    $"{inner}try {{ _parsed = JSON.parse(_text); }} catch (_e) {{ throw new Error('This is not valid JSON, so Otter could not read it.'); }}",
                    $"{inner}{Otterify}",
                    Write(inner, localNames, readJson.Target, "_value"),
                    $"{pad}}}");
            }
            case ConvertJsonStmt { ToJson: true } toJson:
            {
                string subjectJs = Expression(toJson.Subject);
                return Lines(
                    $"{pad}{{",
                    $"{inner}const _deotter = function _deotter(v) {{ if (v === null || v === undefined) return null; if (typeof v === 'function') {{ throw new Error('Otter cannot turn something it can do into JSON.'); }} if (Array.isArray(v)) return v.map(_deotter); if (typeof v === 'object') {{ if (!v.__otterThing) {{ throw new Error('Otter cannot turn something it can do into JSON.'); }} const out = {{}}; for (const k of v.order) {{ out[k] = _deotter(v.props[k]); }} return out; }} return v; }};",
                    $"{inner}const _value = JSON.stringify(_deotter({subjectJs}), null, 4);",
                    Write(inner, localNames, toJson.Target, "_value"),
                    $"{pad}}}");
            }
            case ConvertJsonStmt fromJson:
            {
                string subjectJs = Expression(fromJson.Subject);
                return Lines(
                    $"{pad}{{",
                    $"{inner}const _text = String({subjectJs});",
                    $"{inner}if (_text.trim() === '') {{ throw new Error('There is no JSON here to read.'); }}",
                    $"{inner}let _parsed;",
                    $"{inner}try {{ _parsed = JSON.parse(_text); }} catch (_e) {{ throw new Error('This is not valid JSON, so Otter could not read it.'); }}",
                    $"{inner}{Otterify}",
                    Write(inner, localNames, fromJson.Target, "_value"),
                    $"{pad}}}");
            }
            case ReadCsvStmt readCsv:
            {
                string pathJs = Expression(readCsv.Path);
                return Lines(
                    $"{pad}{{",
                    $"{inner}const _text = String(await otterReadFile({pathJs}));",
                    $"{inner}const _value = ({Templates.JsFromCsvFunc})(_text);",
                    Write(inner, localNames, readCsv.Target, "_value"),
                    $"{pad}}}");
            }
            case WriteCsvStmt writeCsv:
            {
                string rowsJs = Expression(writeCsv.Rows);
                string pathJs = Expression(writeCsv.Path);
                return Lines(
                    $"{pad}{{",
                    $"{inner}const _csv = ({Templates.JsToCsvFunc})({rowsJs});",
                    $"{inner}await otterWriteFile({pathJs}, _csv);",
                    $"{pad}}}");
            }
            case ConvertCsvStmt { ToCsv: true } toCsv:
            {
                string subjectJs = Expression(toCsv.Subject);
                return Lines(
                    $"{pad}{{",
                    $"{inner}const _value = ({Templates.JsToCsvFunc})({subjectJs});",
                    Write(inner, localNames, toCsv.Target, "_value"),
                    $"{pad}}}");
            }
            case ConvertCsvStmt fromCsv:
            {
                string subjectJs = Expression(fromCsv.Subject);
                return Lines(
                    $"{pad}{{",
                    $"{inner}const _text = String({subjectJs});",
                    $"{inner}const _value = ({Templates.JsFromCsvFunc})(_text);",
                    Write(inner, localNames, fromCsv.Target, "_value"),
                    $"{pad}}}");
            }
            case ReplaceStmt replace:
            {
                string findJs = Expression(replace.Find);
                string replacementJs = Expression(replace.Replacement);
                return Lines(
                    $"{pad}{{",
                    $"{inner}const _find = String({findJs});",
                    $"{inner}if (_find.length === 0) {{ throw new Error('I cannot replace empty text.'); }}",
                    $"{inner}const _replaced = String({replace.Target}).split(_find).join(String({replacementJs}));",
                    SetVariable(inner, replace.Target, "_replaced"),
                    $"{pad}}}");
            }
            case SplitStmt split:
            {
                string subjectJs = Expression(split.Subject);
                string separatorJs = Expression(split.Separator);
                return Lines(
                    $"{pad}{{",
                    $"{inner}const _sep = String({separatorJs});",
                    $"{inner}if (_sep.length === 0) {{ throw new Error('I need something to split by.'); }}",
                    $"{inner}const _pieces = String({subjectJs}).split(_sep);",
                    Write(inner, localNames, split.Target, "_pieces"),
                    $"{pad}}}");
            }
            case FunctionDefStmt function:
                return FunctionDef(function, indent, knownGlobals);
            case CallStmt call:
            {
                string callJs = $"{call.Call.Name}({string.Join(", ", call.Call.Arguments.Select(Expression))})";
                if (!string.IsNullOrEmpty(call.ResultTarget)) return Write(pad, localNames, call.ResultTarget, callJs);
                return $"{pad}{callJs};";
            }
            case SetCursorStmt:
                throw WebError("`set cursor to ...` is not supported on the web target yet.", stmt.Line);
            case ChooseFromListStmt:
                throw WebError("`choose from ... into ...` is not supported on the web target yet.", stmt.Line);
            case ShowProgressStmt:
                throw WebError("`show progress ...` is not supported on the web target yet.", stmt.Line);
            case WaitDelayStmt:
                throw WebError("`wait ...` is not supported on the web target yet.", stmt.Line);
            case SetRandomSeedStmt:
                throw WebError("`set random seed to ...` is not supported on the web target yet.", stmt.Line);
            case StartTimerStmt timer:
                return Write(pad, localNames, timer.Target, "performance.now()");
            case RouteStmt route:
                if (route.IsOtherwise) return $"{pad}window.otterRouter.registerOtherwise('{route.PageName}');";
                return $"{pad}window.otterRouter.register({Expression(route.Path!)}, '{route.PageName}');";
            case GoToRouteStmt goTo:
                return $"{pad}window.otterRouter.goTo({Expression(goTo.Path)});";
            case GoNavigateStmt navigate:
                return $"{pad}window.history.{(navigate.IsForward ? "forward" : "back")}();";
            case ReplaceRouteStmt replaceRoute:
                return $"{pad}window.otterRouter.replaceRoute({Expression(replaceRoute.Path)});";
            case RouteChangeStmt routeChange:
            {
                string asyncKw = AsyncAnalysis.BodyNeedsAsync(routeChange.Body) ? "async " : "";
                var lines = new List<string> { $"{pad}window.otterRouter.changeHandlers.push({asyncKw}() => {{" };
                foreach (Stmt s in routeChange.Body) lines.Add(Statement(s, indent + 1, localNames));
                lines.Add($"{pad}}});");
                return string.Join("\n", lines);
            }
            case XmlWriteFileStmt xmlWrite:
            {
                string xmlJs = Expression(xmlWrite.Xml);
                string pathJs = Expression(xmlWrite.Path);
                string guardJs = $"((_x) => {{ if (!_x || typeof _x !== 'object' || !_x.__otterXml) {{ throw new Error('I can only write xml, but this is something else.'); }} return _x; }})({xmlJs})";
                return $"{pad}await otterWriteFile({pathJs}, new XMLSerializer().serializeToString(({guardJs}).node), false);";
            }
            case XmlAddElementStmt addElement:
            {
                string nameJs = Expression(addElement.Name);
                string xmlJs = Expression(addElement.Xml);
                string textJs = addElement.Text != null ? Expression(addElement.Text) : "null";
                string addCallJs = $"((_x, _n, _t) => {{ if (!_x || typeof _x !== 'object' || !_x.__otterXml) {{ throw new Error('I can only add an element to xml, but this is something else.'); }} const _ownerDoc = _x.node.nodeType === 9 ? _x.node : _x.node.ownerDocument; const _el = _ownerDoc.createElement(String(_n)); if (_t !== null) {{ _el.textContent = String(_t); }} const _parent = _x.node.nodeType === 9 ? (_x.node.documentElement || _x.node) : _x.node; _parent.appendChild(_el); return _el; }})({xmlJs}, {nameJs}, {textJs})";
                if (!string.IsNullOrEmpty(addElement.Target)) return Write(pad, localNames, addElement.Target, XmlConstructor(addCallJs));
                return $"{pad}{addCallJs};";
            }
            case XmlRemoveElementStmt removeElement:
                return $"{pad}((_e) => {{ if (!_e || typeof _e !== 'object' || !_e.__otterXml) {{ throw new Error('I can only remove an xml element, but this is something else.'); }} if (_e.node.nodeType !== 1) {{ throw new Error('I can only remove on an xml element, but this is an xml document.'); }} if (!_e.node.parentNode) {{ throw new Error('This element has no parent to remove it from.'); }} _e.node.parentNode.removeChild(_e.node); }})({Expression(removeElement.Element)});";
            case XmlSetTextStmt setText:
            {
                string valueJs = Expression(setText.Value);
                if (setText.Element != null)
                {
                    string elemJs = Expression(setText.Element);
                    return $"{pad}((_e, _v) => {{ if (!_e || typeof _e !== 'object' || !_e.__otterXml) {{ throw new Error('I can only set the text of an xml element, but this is something else.'); }} if (_e.node.nodeType !== 1) {{ throw new Error('I can only set the text of on an xml element, but this is an xml document.'); }} _e.node.textContent = String(_v); }})({elemJs}, {valueJs});";
                }
                string nameJs = Expression(setText.NameIn!);
                string xmlInJs = Expression(setText.XmlIn!);
                return $"{pad}((_x, _n, _v) => {{ if (!_x || typeof _x !== 'object' || !_x.__otterXml) {{ throw new Error('I can only set text within xml, but this is something else.'); }} const _found = ({XmlFindByNameFunc})(_x.node, String(_n)); if (_found.length === 0) {{ throw new Error('I could not find an element called ' + String(_n) + ' to set the text of.'); }} _found[0].textContent = String(_v); }})({xmlInJs}, {nameJs}, {valueJs});";
            }
            case XmlSetAttributeStmt setAttribute:
            {
                string nameJs = Expression(setAttribute.Name);
                string elemJs = Expression(setAttribute.Element);
                string valueJs = Expression(setAttribute.Value);
                return $"{pad}((_e, _n, _v) => {{ if (!_e || typeof _e !== 'object' || !_e.__otterXml) {{ throw new Error('I can only set an attribute of an xml element, but this is something else.'); }} if (_e.node.nodeType !== 1) {{ throw new Error('I can only set an attribute of on an xml element, but this is an xml document.'); }} _e.node.setAttribute(String(_n), String(_v)); }})({elemJs}, {nameJs}, {valueJs});";
            }
            case XmlRemoveAttributeStmt removeAttribute:
            {
                string nameJs = Expression(removeAttribute.Name);
                string elemJs = Expression(removeAttribute.Element);
                return $"{pad}((_e, _n) => {{ if (!_e || typeof _e !== 'object' || !_e.__otterXml) {{ throw new Error('I can only remove an attribute of an xml element, but this is something else.'); }} if (_e.node.nodeType !== 1) {{ throw new Error('I can only remove an attribute of on an xml element, but this is an xml document.'); }} _e.node.removeAttribute(String(_n)); }})({elemJs}, {nameJs});";
            }
            case SetDragDataStmt dragData:
                return $"{pad}otterSetDragData(event, {Expression(dragData.Value)});";
            case GenerateKeyStmt generateKey:
                return Write(pad, localNames, generateKey.Target, "otterGenerateEncryptionKey()");
            case CryptoCipherStmt cipher:
            {
                string dataJs = Expression(cipher.Data);
                string keyJs = Expression(cipher.Key);
                string cipherCallJs = cipher.Decrypt ? $"await otterDecryptBytes({dataJs}, {keyJs})" : $"await otterEncryptBytes({dataJs}, {keyJs})";
                return Write(pad, localNames, cipher.Target, cipherCallJs);
            }
            case HashPasswordStmt hashPassword:
                return Write(pad, localNames, hashPassword.Target, $"await otterHashPassword({Expression(hashPassword.Password)})");
            case WriteBytesFileStmt:
                throw WebError("Binary file access is not supported on the web target.", stmt.Line);
            case CreateUiResourceStmt create:
            {
                string kind = create.TypeName.ToLowerInvariant();
                string variant = "";
                foreach (string v in new[] { "primary", "secondary", "danger" })
                {
                    if (kind.StartsWith(v + " ", StringComparison.Ordinal)) { variant = v; kind = kind.Substring(v.Length + 1); break; }
                }
                return Write(pad, localNames, create.Target, $"otterCreateUi('{kind}', '{variant}')");
            }
            case PutInStmt put:
                return $"{pad}otterPutIn({UiRefForNode(put.Item)}, {UiRefForNode(put.Container)});";
            case ShowStmt show:
                return $"{pad}otterUiAction({UiRefForNode(show.Target)}, 'show');";
            case UiActionStmt action:
                throw WebError($"'{action.Action}' is not supported in Otter 1.0.", action.Line);
            case WhenStmt whenStmt:
                return NestedWhen(whenStmt, indent, localNames, knownGlobals);
            case MemoDefStmt:
                throw WebError("'memo' is not supported in Otter 1.0.", stmt.Line);
            case LifecycleStmt lifecycle:
                throw WebError($"'on {lifecycle.Stage}' is not supported in Otter 1.0.", stmt.Line);
            case SharedStateStmt:
                throw WebError("'shared' is not supported in Otter 1.0.", stmt.Line);
            case UseModuleStmt:
                throw WebError("'use' is not supported in Otter 1.0.", stmt.Line);
            case StateDefStmt:
                throw WebError("A state declaration must be written at the top level of a web program, not inside a handler, function or block.", stmt.Line);
            case DeriveDefStmt:
                throw WebError("A derived value must be written at the top level of a web program, not inside a handler, function or block.", stmt.Line);
            case WatchStmt:
                throw WebError("A \"when ... changes\" watcher must be written at the top level of a web program, not inside a handler, function or block.", stmt.Line);
            case UiElementStmt:
                if (localNames != null) return "";
                throw WebError("A declarative UI element must be written at the top level of a web program or directly in a function, not inside a handler or block.", stmt.Line);
            case UiEventStmt:
                throw WebError("A UI event block must be written inside a declarative UI element at the top level of a web program.", stmt.Line);
            case UiAnimationBlock:
                // NodeKind UiAnimation reaches the default case in otterPS.
                throw WebError("This statement (UiAnimation) is not supported on the web target yet.", stmt.Line);
            // ---- system (D67, D69-D85), downloads (D96) ----
            case DownloadFileStmt download:
                return $"{pad}await otterDownloadFile({Expression(download.Url)}, {Expression(download.Path)});";
            case RunProgramStmt runProgram:
                return HookResult(pad, localNames, runProgram.ResultTarget, $"await otterRunCommand({Expression(runProgram.Target)})");
            case RunRemoteCommandStmt remote:
                return HookResult(pad, localNames, remote.ResultTarget, $"await otterRunRemoteCommand({Expression(remote.Command)}, {Expression(remote.HostName)}, {Expression(remote.CredentialName)})");
            case RunSshCommandStmt ssh:
                return HookResult(pad, localNames, ssh.ResultTarget, $"await otterRunSshCommand({Expression(ssh.Command)}, {Expression(ssh.HostName)})");
            case GetProcessesStmt processes:
                return ThingList(pad, inner, localNames, processes.Target, "await otterGetProcesses()", "process");
            case KillProcessStmt kill:
                return Lines(
                    $"{pad}{{",
                    $"{inner}const _p = {Expression(kill.Process)};",
                    $"{inner}if (!_p || typeof _p !== 'object' || !_p.__otterThing || !('id' in _p.props)) {{ throw new Error('I can only kill a real process handle, such as the one \"run ... into p\" or \"get processes into list\" gives you.'); }}",
                    $"{inner}await otterKillProcess(_p.props.id, {(kill.IncludeChildren ? "true" : "false")});",
                    $"{pad}}}");
            case SetProcessPriorityStmt priority:
            {
                string processJs = Expression(priority.Process);
                string priorityJs = Expression(priority.Priority);
                return Lines(
                    $"{pad}{{",
                    $"{inner}const _p = {processJs};",
                    $"{inner}if (!_p || typeof _p !== 'object' || !_p.__otterThing || !('id' in _p.props)) {{ throw new Error('I can only set the priority of a real process handle, such as the one \"run ... into p\" or \"get processes into list\" gives you.'); }}",
                    $"{inner}await otterSetProcessPriority(_p.props.id, String({priorityJs}));",
                    $"{pad}}}");
            }
            case WaitForProcessStmt waitFor:
            {
                string processJs = Expression(waitFor.Process);
                string secondsJs = Expression(waitFor.TimeoutSeconds);
                var lines = new List<string>
                {
                    $"{pad}{{",
                    $"{inner}const _p = {processJs};",
                    $"{inner}if (!_p || typeof _p !== 'object' || !_p.__otterThing || !('id' in _p.props)) {{ throw new Error('I can only wait for a real process handle, such as the one \"run ... into p\" or \"get processes into list\" gives you.'); }}",
                    $"{inner}const _finished = await otterWaitForProcess(_p.props.id, Number({secondsJs}));",
                };
                if (!string.IsNullOrEmpty(waitFor.Target)) lines.Add(Write(inner, localNames, waitFor.Target, "_finished"));
                lines.Add($"{pad}}}");
                return string.Join("\n", lines);
            }
            case CreateSymbolicLinkStmt createLink:
                return $"{pad}await otterCreateSymbolicLink({Expression(createLink.LinkPath)}, {Expression(createLink.TargetPath)});";
            case GetSymbolicLinkTargetStmt linkTarget:
                return Write(pad, localNames, linkTarget.Target, $"await otterGetSymbolicLinkTarget({Expression(linkTarget.LinkPath)})");
            case GetFileOwnerStmt owner:
                return Write(pad, localNames, owner.Target, $"await otterGetFileOwner({Expression(owner.Path)})");
            case SetFileReadOnlyStmt setReadOnly:
                return $"{pad}await otterSetFileReadOnly({Expression(setReadOnly.Path)}, {(setReadOnly.ReadOnly ? "true" : "false")});";
            case GetRegistryValueStmt getRegistry:
                return Write(pad, localNames, getRegistry.Target, $"await otterGetRegistryValue({Expression(getRegistry.ValueName)}, {Expression(getRegistry.KeyPath)})");
            case GetEventLogEntriesStmt eventLog:
                return ThingList(pad, inner, localNames, eventLog.Target,
                    $"await otterGetEventLogEntries({Expression(eventLog.LogName)}, Number({Expression(eventLog.MaxEntries)}))", "event log entry");
            case SetRegistryValueStmt setRegistry:
                return $"{pad}await otterSetRegistryValue({Expression(setRegistry.ValueName)}, {Expression(setRegistry.Value)}, {Expression(setRegistry.KeyPath)});";
            case DeleteRegistryValueStmt deleteRegistry:
                return $"{pad}await otterDeleteRegistryValue({Expression(deleteRegistry.ValueName)}, {Expression(deleteRegistry.KeyPath)});";
            case SetCredentialStmt setCredential:
                return $"{pad}await otterSetCredential({Expression(setCredential.Name)}, {Expression(setCredential.Secret)});";
            case GetCredentialStmt getCredential:
                return Write(pad, localNames, getCredential.Target, $"await otterGetCredential({Expression(getCredential.Name)})");
            case DeleteCredentialStmt deleteCredential:
                return $"{pad}await otterDeleteCredential({Expression(deleteCredential.Name)});";
            case PowerActionStmt power:
                return $"{pad}await otterPowerAction({Expression(new LiteralExpr(PowerActionWord(power.Action), power.Line))});";
            case PrintFileStmt print:
                return $"{pad}await otterPrintFile({Expression(print.Path)}, {Expression(print.PrinterName)});";
            case CopyToClipboardStmt copy:
                return $"{pad}await otterClipboard.copy(String({Expression(copy.Text)}));";
            case GetClipboardStmt getClipboard:
                return Lines(
                    $"{pad}{{",
                    $"{inner}const _raw = await otterClipboard.paste();",
                    $"{inner}const _val = (_raw === '' || _raw === null || _raw === undefined) ? null : _raw;",
                    Write(inner, localNames, getClipboard.Target, "_val"),
                    $"{pad}}}");
            case NotifyStmt notify:
                return $"{pad}await otterNotify(String({Expression(notify.Title)}), String({Expression(notify.Message)}));";
            case GetSystemInfoStmt systemInfo:
                return Lines(
                    $"{pad}{{",
                    $"{inner}const _kindOriginal = String({Expression(systemInfo.InfoKind)});",
                    $"{inner}const _kind = _kindOriginal.toLowerCase();",
                    $"{inner}const _typeNames = {{ os: 'operating system', cpu: 'cpu', memory: 'memory', disk: 'disk', network: 'network interface', user: 'user', software: 'software', tasks: 'scheduled task', printers: 'printer', services: 'service' }};",
                    $"{inner}const _listKinds = {{ network: true, groups: true, software: true, tasks: true, printers: true, services: true }};",
                    $"{inner}const _plainListKinds = {{ groups: true }};",
                    $"{inner}if (!(_kind in _typeNames) && !(_kind in _listKinds)) {{ throw new Error('I do not know a kind of system information called \"' + _kindOriginal + '\".'); }}",
                    $"{inner}const _raw = await otterGetSystemInfo(_kind);",
                    $"{inner}const _wrapOne = (v, tn) => {{ const props = {{}}; const order = []; if (v) {{ for (const k of Object.keys(v)) {{ props[k] = v[k]; order.push(k); }} }} return {{ __otterThing: true, typeName: tn, props: props, order: order }}; }};",
                    $"{inner}let _value;",
                    $"{inner}if (_kind in _listKinds) {{ const _arr = Array.isArray(_raw) ? _raw : []; _value = (_kind in _plainListKinds) ? _arr : _arr.map((x) => _wrapOne(x, _typeNames[_kind])); }} else {{ _value = _wrapOne(_raw, _typeNames[_kind]); }}",
                    Write(inner, localNames, systemInfo.Target, "_value"),
                    $"{pad}}}");
            case ChooseFileStmt choose:
            {
                string hook = choose.Dialog switch
                {
                    FileDialogKind.Folder => "otterChooseFolder()",
                    FileDialogKind.SaveFile => "otterSaveFileDialog()",
                    _ => "otterChooseFile()",
                };
                // Get-OtterJsDialogSnippet
                return Lines(
                    $"{pad}{{",
                    $"{inner}const _raw = await {hook};",
                    $"{inner}const _path = (_raw === '' || _raw === null || _raw === undefined) ? null : _raw;",
                    Write(inner, localNames, choose.Target, "_path"),
                    $"{pad}}}");
            }
            case StoreSecretStmt:
                throw WebError("The credential vault (store secret) is not supported on the web target. Browser storage is not a secure credential store.", stmt.Line);
            case DeleteSecretStmt:
                throw WebError("The credential vault (delete secret) is not supported on the web target. Browser storage is not a secure credential store.", stmt.Line);

            // ---- HTTP (D49, D60, D101, D116B) ----
            case HttpGetStmt get:
                return HttpRequest(pad, inner, get.Options, "", Expression(get.Url), get.Target, get.AsJson ? "res.json()" : "res.text()", allowNoOptions: true);
            case HttpSendStmt send:
            {
                string dataJs = Expression(send.Data);
                string urlJs = Expression(send.Url);
                string body = send.AsJson ? $"JSON.stringify({dataJs})" : $"(typeof {dataJs} === 'object' ? JSON.stringify({dataJs}) : String({dataJs}))";
                return HttpRequest(pad, inner, send.Options, $"{{ method: '{send.Method}', body: {body} }}", urlJs, send.Target, "res.text()", allowNoOptions: false);
            }
            case HttpDeleteStmt delete:
                return HttpRequest(pad, inner, delete.Options, "{ method: 'DELETE' }", Expression(delete.Url), delete.Target, "res.text()", allowNoOptions: false);
            case HttpStartStmt start:
                return HttpStart(start, pad, inner);
            case HttpCancelStmt cancel:
                return $"{pad}((_r) => {{ if (!_r || typeof _r !== 'object' || !_r.__otterHttpRequest) {{ throw new Error('cancel requires an HTTP request.'); }} _r.cancel(); }})({Expression(cancel.Request)});";

            // ---- file watching (D104): console and desktop only ----
            case FileWatchStmt or StopWatchingStmt or WatchEventStmt:
                throw WebError("File/folder watching is not supported on the web target.", stmt.Line);

            // ---- WebSockets (D106), TCP and UDP (D107, D108, D112, D113) ----
            case WebSocketConnectStmt connect:
            {
                string urlJs = Expression(connect.Url);
                string protocolJs = connect.Protocol != null ? Expression(connect.Protocol) : "null";
                string ctorJs = $"((_u, _p) => {{ const _url = String(_u).trim(); if (!_url.toLowerCase().startsWith('ws://') && !_url.toLowerCase().startsWith('wss://')) {{ throw new Error('I need a websocket URL starting with \\'ws://\\' or \\'wss://\\', but this was \\'' + _url + '\\'.'); }} const _ws = _p ? new WebSocket(_url, [_p]) : new WebSocket(_url); _ws.binaryType = 'arraybuffer'; return {{ __otterWebSocket: true, ws: _ws, url: _url, protocol: _p || '', toString() {{ return 'a websocket'; }} }}; }})({urlJs}, {protocolJs})";
                return Write(pad, localNames, connect.Target, ctorJs);
            }
            case WebSocketSendStmt wsSend:
            {
                string socketJs = Expression(wsSend.Socket);
                string msgJs = Expression(wsSend.Message);
                return $"{pad}((_s, _m) => {{ if (!_s || typeof _s !== 'object' || !_s.__otterWebSocket) {{ throw new Error('I can only send through a websocket, but this is something else.'); }} if (_s.ws.readyState !== 1) {{ throw new Error('I cannot send through a websocket that is not open (its state is \\'' + (_s.ws.readyState === 0 ? 'connecting' : _s.ws.readyState === 2 ? 'closing' : 'closed') + '\\').'); }} if (typeof _m === 'string') {{ _s.ws.send(_m); }} else if (_m && typeof _m === 'object' && _m.__otterBytes) {{ _s.ws.send(_m.value.buffer); }} else {{ throw new Error('I can only send text or bytes through a websocket, but this is something else.'); }} }})({socketJs}, {msgJs});";
            }
            case WebSocketCloseStmt wsClose:
            {
                string socketJs = Expression(wsClose.Socket);
                string codeJs = wsClose.Code != null ? Expression(wsClose.Code) : "1000";
                string reasonJs = wsClose.Reason != null ? Expression(wsClose.Reason) : "\"\"";
                return $"{pad}((_s, _c, _r) => {{ if (!_s || typeof _s !== 'object' || !_s.__otterWebSocket) {{ throw new Error('I can only close a websocket, but this is something else.'); }} if (_s.ws.readyState <= 1) {{ _s.ws.close(_c, _r); }} }})({socketJs}, {codeJs}, {reasonJs});";
            }
            case TcpConnectStmt:
                throw WebError("TCP is not supported on the web target. Browsers cannot open raw TCP sockets (use a websocket instead).", stmt.Line);
            case TcpListenStmt:
                throw WebError("TCP servers are not supported on the web target. Browsers cannot listen on TCP ports.", stmt.Line);
            case TcpStopStmt:
                throw WebError("TCP servers are not supported on the web target.", stmt.Line);
            case UdpOpenStmt or UdpSendStmt:
                throw WebError("UDP is not supported on the web target. Browsers cannot open raw UDP sockets.", stmt.Line);
            case NetCloseStmt netClose:
                throw WebError($"{netClose.Protocol.ToUpperInvariant()} is not supported on the web target. Browsers cannot open raw sockets.", stmt.Line);
            case NetworkEventStmt networkEvent:
                return NetworkEvent(networkEvent, indent, pad, localNames);

            // ---- databases and queries (D97-D99) ----
            case ConnectDbStmt or DisconnectDbStmt or DbQueryStmt or DbExecuteStmt or BeginTransactionStmt or CommitTransactionStmt
                or RollbackTransactionStmt or GetTablesStmt or GetColumnsStmt or QueryStmt or QueryAggregateStmt:
                throw WebError("Database providers are not supported on the web target. Browsers cannot connect directly to databases.", stmt.Line);

            // ---- console and server only ----
            case StartCommandStmt:
                throw WebError("Starting a command (\"start command ...\") is not supported on the web target. Browsers cannot run programs.", stmt.Line);
            case WebRouteStmt or RespondStmt or StartServerStmt or ListenServerStmt:
                throw WebError("Web server statements run with \"otter serve\", not inside a web page.", stmt.Line);
            default:
                throw new NotSupportedYet($"compiling {stmt.GetType().Name} for the web", stmt.Line);
        }
    }

    const string Otterify = "const _value = (function _otterify(v) { if (v === null) return null; if (Array.isArray(v)) return v.map(_otterify); if (typeof v === 'object') { const props = {}; const order = []; for (const k of Object.keys(v)) { props[k] = _otterify(v[k]); order.push(k); } return { __otterThing: true, typeName: 'thing', props: props, order: order }; } return v; })(_parsed);";

    // Assign to `prop of target` (D60 Phase 1F.2): a UI resource's property
    // when the name is an element, otherwise a thing's property.
    string AssignProperty(PropertyAccessExpr access, Expr value, string pad, string inner)
    {
        string propOriginal = access.Property;
        string prop = propOriginal.ToLowerInvariant();
        Expr target = access.Target;
        bool isVar = target is VariableExpr;
        string targetName = target is VariableExpr variable ? variable.Name : "";
        string targetJs = Expression(target);
        string valExpr = Expression(value);
        var lines = new List<string> { $"{pad}{{" };
        if (isVar && runtimeUiNames.Contains(targetName))
        {
            string uiRef = UiRef(targetName);
            lines.Add($"{inner}const _el = otterGetElement({uiRef});");
            lines.Add($"{inner}if (_el) {{ otterSetUiProp({uiRef}, '{prop}', {valExpr}); }}");
            lines.Add($"{inner}else {{");
            lines.Add($"{inner}  const _owner = {targetName};");
            lines.Add($"{inner}  if (!_owner || typeof _owner !== 'object' || !_owner.__otterThing) {{ throw new Error('I can only set properties on a thing, but this is something else.'); }}");
            lines.Add($"{inner}  if (!(('{propOriginal}') in _owner.props)) {{ _owner.order.push('{propOriginal}'); }}");
            lines.Add($"{inner}  _owner.props['{propOriginal}'] = {valExpr};");
            lines.Add($"{inner}}}");
        }
        else if (isVar)
        {
            string uiBranch = prop switch
            {
                "text" => $"otterSetText('{targetName}', {valExpr});",
                "value" => $"otterSetText('{targetName}', {valExpr});",
                "title" => $"otterSetTitle('{targetName}', {valExpr});",
                "background" => $"otterSetStyle('{targetName}', 'backgroundColor', {valExpr});",
                "foreground" => $"otterSetStyle('{targetName}', 'color', {valExpr});",
                "width" => $"otterSetStyle('{targetName}', 'width', typeof ({valExpr}) === 'number' ? ({valExpr} + 'px') : {valExpr});",
                "height" => $"otterSetStyle('{targetName}', 'height', typeof ({valExpr}) === 'number' ? ({valExpr} + 'px') : {valExpr});",
                _ => $"otterSetProperty('{targetName}', '{prop}', {valExpr});",
            };
            lines.Add($"{inner}const _el = otterGetElement('{targetName}');");
            lines.Add($"{inner}if (_el) {{ {uiBranch} }}");
            lines.Add($"{inner}else {{");
            lines.Add($"{inner}  const _owner = {targetName};");
            lines.Add($"{inner}  if (!_owner || typeof _owner !== 'object' || !_owner.__otterThing) {{ throw new Error('I can only set properties on a thing, but this is something else.'); }}");
            lines.Add($"{inner}  if (!(('{propOriginal}') in _owner.props)) {{ _owner.order.push('{propOriginal}'); }}");
            lines.Add($"{inner}  _owner.props['{propOriginal}'] = {valExpr};");
            lines.Add($"{inner}}}");
        }
        else
        {
            lines.Add($"{inner}const _owner = ({targetJs});");
            lines.Add($"{inner}if (!_owner || typeof _owner !== 'object' || !_owner.__otterThing) {{ throw new Error('I can only set properties on a thing, but this is something else.'); }}");
            lines.Add($"{inner}if (!(('{propOriginal}') in _owner.props)) {{ _owner.order.push('{propOriginal}'); }}");
            lines.Add($"{inner}_owner.props['{propOriginal}'] = {valExpr};");
        }
        lines.Add($"{pad}}}");
        return string.Join("\n", lines);
    }

    static readonly Regex JsIdentifier = new("^[A-Za-z_$][A-Za-z0-9_$]*$", RegexOptions.CultureInvariant);

    string ObjectDef(ObjectDefStmt objectDef, int indent, HashSet<string>? localNames, HashSet<string>? knownGlobals, bool skipRuntimeUi)
    {
        string pad = Pad(indent);
        string inner = Pad(indent + 1);
        // Runtime UI: `item has text task` after `create text into item`.
        if (!skipRuntimeUi && (runtimeUiNames.Contains(objectDef.Name) || staticUiNames.Contains(objectDef.Name)))
        {
            string uiRef = UiRef(objectDef.Name);
            var uiLines = new List<string> { $"{pad}if (otterGetElement({uiRef})) {{" };
            foreach (AssignStmt property in objectDef.Properties)
            {
                if (property.Target is not VariableExpr propertyName)
                {
                    throw WebError("A UI resource property name must be a plain name.", property.Line);
                }
                uiLines.Add($"{inner}otterSetUiProp({uiRef}, '{propertyName.Name.ToLowerInvariant()}', {Expression(property.Value)});");
            }
            uiLines.Add($"{pad}}} else {{");
            uiLines.Add(Statement(objectDef, indent + 1, localNames, knownGlobals, skipRuntimeUi: true));
            uiLines.Add($"{pad}}}");
            return string.Join("\n", uiLines);
        }
        string typeName = objectDef.TypeName;
        var lines = new List<string> { $"{pad}{{", $"{inner}const _props = {{}};", $"{inner}const _order = [];" };
        // PowerShell's -ne and -match: "thing" in any case, an identifier in any case.
        if (!string.Equals(typeName, "thing", StringComparison.OrdinalIgnoreCase) && JsIdentifierIgnoringCase(typeName))
        {
            lines.Add($"{inner}if (typeof {typeName} !== 'undefined' && {typeName} && typeof {typeName} === 'object' && {typeName}.__otterType) {{ for (const _f of {typeName}.fieldNames) {{ if (!(_f in _props)) {{ _order.push(_f); }} _props[_f] = null; }} }}");
        }
        foreach (AssignStmt property in objectDef.Properties)
        {
            if (property.Target is VariableExpr propertyName)
            {
                string name = propertyName.Name;
                lines.Add($"{inner}if (!(('{name}') in _props)) {{ _order.push('{name}'); }} _props['{name}'] = {Expression(property.Value)};");
            }
        }
        lines.Add($"{inner}const _thing = {{ __otterThing: true, typeName: '{typeName}', props: _props, order: _order }};");
        lines.Add(Write(inner, localNames, objectDef.Name, "_thing"));
        lines.Add($"{pad}}}");
        return string.Join("\n", lines);
    }

    // `-match '^[A-Za-z_$][A-Za-z0-9_$]*$'` ignores case, which changes
    // nothing for these character classes.
    static bool JsIdentifierIgnoringCase(string text) => JsIdentifier.IsMatch(text);

    string AddOrRemove(string target, string amountJs, bool add, string pad, string inner, HashSet<string>? localNames)
    {
        var lines = new List<string> { $"{pad}{{", $"{inner}const _amt = {amountJs};" };
        if (add) lines.Add($"{inner}if (Array.isArray({target})) {{ {target}.push(_amt); }}");
        else lines.Add($"{inner}if (Array.isArray({target})) {{ const _idx = {target}.indexOf(_amt); if (_idx !== -1) {{ {target}.splice(_idx, 1); }} }}");
        lines.Add($"{inner}else if (typeof {target} === 'number' || (typeof {target} === 'string' && {target}.trim() !== '' && !Number.isNaN(Number({target})))) {{");
        lines.Add($"{inner}  const _aOk = typeof _amt === 'number' || (typeof _amt === 'string' && _amt.trim() !== '' && !Number.isNaN(Number(_amt)));");
        if (add)
        {
            lines.Add($"{inner}  if (!_aOk) {{ throw new Error('I expected a number for the amount to add to \"{target}\"' + ' but got ' + JSON.stringify(_amt) + '.'); }}");
            lines.Add($"{inner}  const _sum = Number({target}) + Number(_amt);");
            lines.Add(Write(inner + "  ", localNames, target, "_sum"));
        }
        else
        {
            lines.Add($"{inner}  if (!_aOk) {{ throw new Error('I expected a number for the amount to remove from \"{target}\"' + ' but got ' + JSON.stringify(_amt) + '.'); }}");
            lines.Add($"{inner}  const _diff = Number({target}) - Number(_amt);");
            lines.Add(Write(inner + "  ", localNames, target, "_diff"));
        }
        lines.Add($"{inner}}}");
        lines.Add(add
            ? $"{inner}else {{ throw new Error('I can only add to a number or a list, but \"{target}\" holds something else.'); }}"
            : $"{inner}else {{ throw new Error('I can only remove from a number or a list, but \"{target}\" holds something else.'); }}");
        lines.Add($"{pad}}}");
        return string.Join("\n", lines);
    }

    string DateAdjust(DateAdjustStmt adjust, string pad, string inner, HashSet<string>? localNames)
    {
        string target = adjust.Target;
        string amountJs = Expression(adjust.Amount);
        string unit = adjust.Unit.ToString();
        int sign = adjust.IsRemoval ? -1 : 1;
        string unitLower = unit.ToLowerInvariant();
        int msPerUnit = unit switch { "Hour" => 3600000, "Minute" => 60000, "Second" => 1000, _ => 0 };
        var lines = new List<string>
        {
            $"{pad}{{",
            $"{inner}const _current = {target};",
            $"{inner}if (!_current || typeof _current !== 'object' || !_current.__otterDate) {{ throw new Error('I can only add time to a date, but \"{target}\" holds something else.'); }}",
            $"{inner}const _amountRaw = {amountJs};",
            $"{inner}const _whole = Math.trunc(Number(_amountRaw)) * ({sign});",
        };
        if (unit is "Year" or "Month")
        {
            string months = unit == "Year" ? "_whole * 12" : "_whole";
            lines.Add($"{inner}const _moved = {AddMonthsSnippet("_current.value", months)};");
        }
        else if (unit == "Day")
        {
            lines.Add($"{inner}const _moved = new Date(_current.value.getTime() + _whole * 86400000);");
        }
        else
        {
            lines.Add($"{inner}if (!_current.hasTime) {{ throw new Error('This is a date with no time of day, so it has no {unitLower}.'); }}");
            lines.Add($"{inner}const _moved = new Date(_current.value.getTime() + _whole * {msPerUnit});");
        }
        lines.Add($"{inner}const _next = {DateConstructor("_moved", "_current.hasTime")};");
        lines.Add(Write(inner, localNames, target, "_next"));
        lines.Add($"{pad}}}");
        return string.Join("\n", lines);
    }

    string FunctionDef(FunctionDefStmt function, int indent, HashSet<string>? knownGlobals)
    {
        string pad = Pad(indent);
        string fnName = function.Name;
        var paramSet = new HashSet<string>(function.Parameters, StringComparer.Ordinal);
        var bindings = Bindings.Of(function.Body);
        HashSet<string> globals = knownGlobals ?? new HashSet<string>(StringComparer.Ordinal);
        var allLocals = new HashSet<string>(StringComparer.Ordinal);
        foreach (string p in function.Parameters) allLocals.Add(p);
        foreach (string n in bindings.AlwaysLocal) allLocals.Add(n);
        foreach (string n in bindings.SetStyle) if (!globals.Contains(n)) allLocals.Add(n);
        var declOnly = allLocals.Where(n => !paramSet.Contains(n)).ToList();

        string asyncPrefix = AsyncAnalysis.BodyNeedsAsync(function.Body) ? "async " : "";
        string bodyIndent = Pad(indent + 1);
        var lines = new List<string> { $"{pad}const {fnName} = {asyncPrefix}function({string.Join(", ", function.Parameters)}) {{" };
        if (declOnly.Count > 0)
        {
            lines.Add($"{bodyIndent}let {string.Join(", ", PowerShellSort.Strings(declOnly))};");
        }
        foreach (Stmt s in function.Body) lines.Add(Statement(s, indent + 1, allLocals));
        lines.Add($"{bodyIndent}return null;");
        lines.Add($"{pad}}};");
        lines.Add($"{pad}if (typeof window !== 'undefined') {{ window.{fnName} = {fnName}; }}");
        return string.Join("\n", lines);
    }

    // `when item clicked` inside a handler, a function or a loop: registered
    // on whatever the variable holds then (D130/D133 captures).
    string NestedWhen(WhenStmt when, int indent, HashSet<string>? localNames, HashSet<string>? knownGlobals)
    {
        string pad = Pad(indent);
        if (IsDragEvent(when.EventName))
        {
            throw WebError("Drag and drop handlers (\"on drag of\", \"on drop on\", \"on files dropped on\") must be written at the top level of a web program, not inside another handler or a function.", when.Line);
        }
        string eventName = when.EventName.ToLowerInvariant() switch
        {
            "clicked" => "click",
            "changed" => "input",
            var other => other,
        };
        string targetRef = UiRefForNode(when.Target);
        var captured = new SortedSet<string>(StringComparer.Ordinal);
        foreach (HashSet<string> set in loopPassStack)
        {
            foreach (string n in set)
            {
                if (localNames != null && !localNames.Contains(n)) continue;
                if (PlainName.IsMatch(n)) captured.Add(n);
            }
        }
        bool pushedRun = false;
        if (loopPassNames.TryGetValue(when, out var runNames) && runNames.Count > 0)
        {
            loopPassStack.Add(runNames);
            pushedRun = true;
        }
        var bodyLines = new List<string>();
        try
        {
            foreach (Stmt b in when.Body) bodyLines.Add(Statement(b, indent + 1, localNames, knownGlobals));
        }
        finally
        {
            if (pushedRun) loopPassStack.RemoveAt(loopPassStack.Count - 1);
        }
        string registration = $"{pad}otterOnUi({targetRef}, '{eventName}', async (event) => {{\n{string.Join("\n", bodyLines)}\n{pad}}});";
        if (captured.Count == 0) return registration;
        string parameters = string.Join(", ", captured);
        string argList = string.Join(", ", captured.Select(n => $"typeof {n} === 'undefined' ? undefined : {n}"));
        return $"{pad}(({parameters}) => {{\n{registration}\n{pad}}})({argList});";
    }

    // `-in @('drag', 'drop', 'files dropped')` ignores case.
    static bool IsDragEvent(string eventName) =>
        new[] { "drag", "drop", "files dropped" }.Any(e => string.Equals(e, eventName, StringComparison.OrdinalIgnoreCase));

    // `$n -match '^[A-Za-z_][A-Za-z0-9_]*$'` (PowerShell's -match ignores case).
    static readonly Regex PlainName = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);
}
