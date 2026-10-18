using System.Text.RegularExpressions;
using Otter.Language;

namespace Otter.Web;

// otterPS src/Otter.Compiler.JavaScript.psm1: the Otter syntax tree to
// JavaScript. Every string it emits is otterPS's, character for character;
// the comments that explain why each piece of JavaScript is shaped the way
// it is live in that file.
sealed partial class JsCompiler
{
    static readonly string XmlSearchRootFunc = "(node) => (node.nodeType === 9 ? node.documentElement : node)";
    static readonly string XmlChildElementsFunc = Templates.JsXmlChildElementsFuncTemplate(XmlSearchRootFunc);
    static readonly string XmlFindByNameFunc = Templates.JsXmlFindByNameFuncTemplate(XmlChildElementsFunc);

    const string DateToStringBody = "const y=this.value.getFullYear(); const mo=String(this.value.getMonth()+1).padStart(2,'0'); const da=String(this.value.getDate()).padStart(2,'0'); if (!this.hasTime) { return y+'-'+mo+'-'+da; } const h=String(this.value.getHours()).padStart(2,'0'); const mi=String(this.value.getMinutes()).padStart(2,'0'); const s=String(this.value.getSeconds()).padStart(2,'0'); return y+'-'+mo+'-'+da+' '+h+':'+mi+':'+s;";

    // Get-OtterJsDateConstructor
    static string DateConstructor(string dateExprJs, string hasTimeJs) =>
        $"{{ __otterDate: true, hasTime: {hasTimeJs}, value: ({dateExprJs}), toString() {{ {DateToStringBody} }} }}";

    // Get-OtterJsBytesConstructor
    static string BytesConstructor(string bytesExprJs) =>
        $"{{ __otterBytes: true, value: ({bytesExprJs}), toString() {{ return '<' + this.value.length + ' bytes>'; }} }}";

    // Get-OtterJsXmlConstructor
    static string XmlConstructor(string nodeExprJs) =>
        $"{{ __otterXml: true, node: ({nodeExprJs}), toString() {{ return new XMLSerializer().serializeToString(this.node); }} }}";

    // Get-OtterJsAddMonthsSnippet
    static string AddMonthsSnippet(string dateJs, string monthsJs) =>
        $"(() => {{ const _d = {dateJs}; const _tot = _d.getFullYear() * 12 + _d.getMonth() + ({monthsJs}); const _ny = Math.floor(_tot / 12); const _nm = (((_tot % 12) + 12) % 12); const _dim = new Date(_ny, _nm + 1, 0).getDate(); const _nd = Math.min(_d.getDate(), _dim); return new Date(_ny, _nm, _nd, _d.getHours(), _d.getMinutes(), _d.getSeconds(), _d.getMilliseconds()); }})()";

    // Get-OtterJsDateDifferenceExpression
    static string DateDifferenceExpression(string unitJs, string startJs, string endJs) =>
        $"(() => {{ const _from = {startJs}; const _to = {endJs}; const _unit = {unitJs}; if (_unit === 'Year' || _unit === 'Month') {{ let _months = (_to.getFullYear() - _from.getFullYear()) * 12 + (_to.getMonth() - _from.getMonth()); if (_months > 0 && _to.getDate() < _from.getDate()) {{ _months--; }} if (_months < 0 && _to.getDate() > _from.getDate()) {{ _months++; }} if (_unit === 'Month') {{ return _months; }} return Math.trunc(_months / 12); }} const _spanMs = _to.getTime() - _from.getTime(); if (_unit === 'Day') {{ return Math.trunc(_spanMs / 86400000); }} if (_unit === 'Hour') {{ return Math.trunc(_spanMs / 3600000); }} if (_unit === 'Minute') {{ return Math.trunc(_spanMs / 60000); }} return Math.trunc(_spanMs / 1000); }})()";

    static OtterError WebError(string message, int line) => new(message, line, "runtime");

    // A value the page's network runtime sets while a websocket or request
    // handler runs (_otterCurrentWsContext); `message` is the JavaScript
    // error text, already escaped for a double-quoted string.
    static string WsContextJs(string field, string message) =>
        $"((typeof _otterCurrentWsContext !== \"undefined\" && _otterCurrentWsContext && \"{field}\" in _otterCurrentWsContext) ? _otterCurrentWsContext.{field} : (() => {{ throw new Error(\"{message}\"); }})())";

    static readonly string ReceivedResponseJs = WsContextJs("response", "\\\"received response\\\" is only available inside \\\"on complete of ...\\\".");

    static readonly Regex ScriptClose = new("(?i)</script", RegexOptions.CultureInvariant);

    // ConvertTo-OtterJsExpression
    public string Expression(Expr expr)
    {
        switch (expr)
        {
            case LiteralExpr literal:
            {
                object? val = literal.Value;
                if (val is null) return "null";
                if (val is bool flag) return flag ? "true" : "false";
                if (val is double number) return PowerShellText.Number(number);
                string escaped = PowerShellText.Of(val).Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
                escaped = ScriptClose.Replace(escaped, "<\\/script");
                return $"\"{escaped}\"";
            }
            case VariableExpr variable:
                if (string.Equals(variable.Name, "empty", StringComparison.OrdinalIgnoreCase)) return "\"\"";
                if (string.Equals(variable.Name, "gone", StringComparison.OrdinalIgnoreCase)) return "null";
                return variable.Name;
            case PropertyAccessExpr access:
                return PropertyAccess(access);
            case MathExpr math:
            {
                string left = Expression(math.Left);
                string right = Expression(math.Right);
                const string checks = "const _lOk = typeof _l === 'number' || (typeof _l === 'string' && _l.trim() !== '' && !Number.isNaN(Number(_l))); if (!_lOk) { throw new Error('I expected a number for the left side of this calculation but got ' + JSON.stringify(_l) + '.'); } const _rOk = typeof _r === 'number' || (typeof _r === 'string' && _r.trim() !== '' && !Number.isNaN(Number(_r))); if (!_rOk) { throw new Error('I expected a number for the right side of this calculation but got ' + JSON.stringify(_r) + '.'); }";
                return math.Op switch
                {
                    MathOp.Add => $"(() => {{ const _l = {left}; const _r = {right}; if (typeof _l === 'string' && typeof _r === 'string') {{ return _l + _r; }} if (typeof _l === 'boolean' || typeof _r === 'boolean') {{ throw new Error('A true/false value cannot be combined with \"and\" here. Boolean \"and\"/\"or\" only work inside an if or while condition.'); }} {checks} return Number(_l) + Number(_r); }})()",
                    MathOp.Subtract => $"(() => {{ const _l = {left}; const _r = {right}; {checks} return Number(_l) - Number(_r); }})()",
                    MathOp.Multiply => $"(() => {{ const _l = {left}; const _r = {right}; {checks} return Number(_l) * Number(_r); }})()",
                    MathOp.Divide => $"(() => {{ const _l = {left}; const _r = {right}; {checks} if (Number(_r) === 0) {{ throw new Error('I cannot divide by zero.'); }} return Number(_l) / Number(_r); }})()",
                    MathOp.Percent => $"(() => {{ const _l = {left}; const _r = {right}; {checks} return (Number(_l) / 100) * Number(_r); }})()",
                    _ => $"(() => {{ const _l = {left}; const _r = {right}; {checks} return Math.pow(Number(_l), Number(_r)); }})()",
                };
            }
            case ComparisonExpr comparison:
            {
                string left = Expression(comparison.Left);
                string right = Expression(comparison.Right);
                string dateGuard = $"const _lD = {left} !== null && typeof ({left}) === 'object' && ({left}).__otterDate === true; const _rD = {right} !== null && typeof ({right}) === 'object' && ({right}).__otterDate === true;";
                string bytesGuard = $"const _lB = {left} !== null && typeof ({left}) === 'object' && ({left}).__otterBytes === true; const _rB = {right} !== null && typeof ({right}) === 'object' && ({right}).__otterBytes === true; const _bytesEq = (a, b) => {{ if (a.length !== b.length) return false; for (let _i = 0; _i < a.length; _i++) {{ if (a[_i] !== b[_i]) return false; }} return true; }};";
                string Ordered(string op) =>
                    $"(() => {{ {dateGuard} if (_lD || _rD) {{ if (!_lD) {{ throw new Error('I expected a number for the left side of this comparison but got ' + ({left}) + '.'); }} if (!_rD) {{ throw new Error('I expected a number for the right side of this comparison but got ' + ({right}) + '.'); }} return (({left}).value.getTime() {op} ({right}).value.getTime()); }} return ({left} {op} {right}); }})()";
                return comparison.Op switch
                {
                    CompareOp.Equal => $"(() => {{ {dateGuard} {bytesGuard} if (_lD || _rD) {{ return (_lD && _rD) ? (({left}).value.getTime() === ({right}).value.getTime()) : false; }} if (_lB || _rB) {{ return (_lB && _rB) ? _bytesEq(({left}).value, ({right}).value) : false; }} return ({left} === {right}); }})()",
                    CompareOp.NotEqual => $"(() => {{ {dateGuard} {bytesGuard} if (_lD || _rD) {{ return !((_lD && _rD) && (({left}).value.getTime() === ({right}).value.getTime())); }} if (_lB || _rB) {{ return !((_lB && _rB) && _bytesEq(({left}).value, ({right}).value)); }} return ({left} !== {right}); }})()",
                    CompareOp.AtLeast => Ordered(">="),
                    CompareOp.AtMost => Ordered("<="),
                    CompareOp.GreaterThan => Ordered(">"),
                    _ => Ordered("<"),
                };
            }
            case LogicalExpr logical:
            {
                string left = Expression(logical.Left);
                string right = Expression(logical.Right);
                return logical.Op == LogicalOp.And ? $"({left} && {right})" : $"({left} || {right})";
            }
            case NotExpr not:
                return $"(!{Expression(not.Operand)})";
            case ContainsExpr contains:
            {
                string collection = Expression(contains.Collection);
                string item = Expression(contains.Item);
                return $"({collection} && {collection}.includes ? {collection}.includes({item}) : false)";
            }
            case TextMatchExpr textMatch:
            {
                string subjectJs = Expression(textMatch.Text);
                string valueJs = Expression(textMatch.Part);
                string method = textMatch.Match == TextMatch.StartsWith ? "startsWith" : "endsWith";
                return $"String({subjectJs}).{method}(String({valueJs}))";
            }
            case OfOperationExpr of:
            {
                string subjectJs = Expression(of.Subject);
                return of.Operation switch
                {
                    OfOperation.Uppercase => $"String({subjectJs}).toUpperCase()",
                    OfOperation.Lowercase => $"String({subjectJs}).toLowerCase()",
                    OfOperation.Length => $"((_s) => {{ if (Array.isArray(_s) || typeof _s === 'string') return _s.length; if (_s && typeof _s === 'object' && _s.__otterBytes) return _s.value.length; throw new Error('I can only measure the length of text, a list, or bytes, but this is something else.'); }})({subjectJs})",
                    OfOperation.First => $"((_s) => {{ if (!Array.isArray(_s)) throw new Error('Only a list has a first item, but this is something else.'); return _s.length > 0 ? _s[0] : null; }})({subjectJs})",
                    OfOperation.Last => $"((_s) => {{ if (!Array.isArray(_s)) throw new Error('Only a list has a last item, but this is something else.'); return _s.length > 0 ? _s[_s.length - 1] : null; }})({subjectJs})",
                    OfOperation.AbsoluteValue => $"Math.abs(Number({subjectJs}))",
                    OfOperation.SquareRoot => $"Math.sqrt(Number({subjectJs}))",
                    OfOperation.Round => $"(Number({subjectJs}) < 0 ? -Math.round(-Number({subjectJs})) : Math.round(Number({subjectJs})))",
                    OfOperation.RoundUp => $"Math.ceil(Number({subjectJs}))",
                    OfOperation.RoundDown => $"Math.floor(Number({subjectJs}))",
                    OfOperation.Sine => $"Math.sin(Number({subjectJs}) * Math.PI / 180)",
                    OfOperation.Cosine => $"Math.cos(Number({subjectJs}) * Math.PI / 180)",
                    OfOperation.Tangent => $"Math.tan(Number({subjectJs}) * Math.PI / 180)",
                    OfOperation.LogTen => $"Math.log10(Number({subjectJs}))",
                    OfOperation.NaturalLog => $"Math.log(Number({subjectJs}))",
                    OfOperation.ElapsedTime => $"((_t) => {{ if (typeof _t !== 'number') throw new Error('I can only measure elapsed time of a timer, but this is something else.'); return (performance.now() - _t) / 1000; }})({subjectJs})",
                    OfOperation.ElapsedMilliseconds => $"((_t) => {{ if (typeof _t !== 'number') throw new Error('I can only measure elapsed time of a timer, but this is something else.'); return performance.now() - _t; }})({subjectJs})",
                    _ => "null",
                };
            }
            case MinMaxExpr minMax:
            {
                string leftJs = Expression(minMax.Left);
                string rightJs = Expression(minMax.Right);
                string fn = minMax.IsMax ? "Math.max" : "Math.min";
                return $"{fn}(Number({leftJs}), Number({rightJs}))";
            }
            case ConsoleInteractiveExpr:
                throw WebError("`console is interactive` is not supported on the web target yet.", expr.Line);
            case ClockExpr clock:
                if (clock.Clock == ClockKind.Today)
                {
                    return DateConstructor("(() => { const _n = new Date(); return new Date(_n.getFullYear(), _n.getMonth(), _n.getDate(), 0, 0, 0, 0); })()", "false");
                }
                return DateConstructor("new Date()", "true");
            case DateFromTextExpr fromText:
            {
                string sourceJs = Expression(fromText.Source);
                string parseJs;
                if (fromText.Format != null)
                {
                    string formatJs = Expression(fromText.Format);
                    parseJs = $"({Templates.JsParseExactDateFunc})(String({sourceJs}), String({formatJs}))";
                }
                else
                {
                    parseJs = $"(() => {{ const _s = String({sourceJs}); const _iso = /^(\\d{{4}})-(\\d{{2}})-(\\d{{2}})$/.exec(_s); if (_iso) {{ return new Date(Number(_iso[1]), Number(_iso[2]) - 1, Number(_iso[3])); }} const _d = new Date(_s); if (isNaN(_d.getTime())) {{ throw new Error('I could not understand ' + _s + ' as a date.'); }} return _d; }})()";
                }
                return $"((_d) => ({{ __otterDate: true, hasTime: (_d.getHours() !== 0 || _d.getMinutes() !== 0 || _d.getSeconds() !== 0), value: _d, toString() {{ {DateToStringBody} }} }}))({parseJs})";
            }
            case BytesExpr bytes:
                switch (bytes.Op)
                {
                    case BytesOp.Empty: return BytesConstructor("new Uint8Array(0)");
                    case BytesOp.FromText: return BytesConstructor($"new TextEncoder().encode(String({Expression(bytes.Source!)}))");
                    case BytesOp.FromHex: return BytesConstructor($"({Templates.JsBytesFromHexFunc})(String({Expression(bytes.Source!)}))");
                    case BytesOp.FromBase64: return BytesConstructor($"({Templates.JsBytesFromBase64Func})(String({Expression(bytes.Source!)}))");
                    case BytesOp.ToText: return $"((_b) => {{ if (!_b || typeof _b !== 'object' || !_b.__otterBytes) {{ throw new Error('I can only read text from bytes, but this is something else.'); }} return ({Templates.JsBytesToTextFunc})(_b.value); }})({Expression(bytes.Source!)})";
                    case BytesOp.ToHex: return $"((_b) => {{ if (!_b || typeof _b !== 'object' || !_b.__otterBytes) {{ throw new Error('I can only read hex from bytes, but this is something else.'); }} return ({Templates.JsBytesToHexFunc})(_b.value); }})({Expression(bytes.Source!)})";
                    default: return $"((_b) => {{ if (!_b || typeof _b !== 'object' || !_b.__otterBytes) {{ throw new Error('I can only read base64 from bytes, but this is something else.'); }} return ({Templates.JsBytesToBase64Func})(_b.value); }})({Expression(bytes.Source!)})";
                }
            case AwaitExpr awaitExpr:
                return $"(await {Expression(awaitExpr.Expression)})";
            case CurrentRouteExpr:
                return "window.location.pathname";
            case RouteParameterExpr routeParameter:
                return $"(window.otterRouter.param(String({Expression(routeParameter.Name)})))";
            case QueryParameterExpr queryParameter:
                return $"(window.otterRouter.queryParam(String({Expression(queryParameter.Name)})))";
            case XmlFromExpr xmlFrom:
                switch (xmlFrom.Source)
                {
                    case XmlSourceKind.Text:
                    {
                        string textJs = Expression(xmlFrom.Value);
                        return XmlConstructor($"((_t) => {{ const _d = new DOMParser().parseFromString(_t, 'application/xml'); const _pe = _d.getElementsByTagName('parsererror')[0]; if (_pe) {{ throw new Error('This is not valid XML, so Otter could not read it: ' + _pe.textContent); }} return _d; }})(String({textJs}))");
                    }
                    case XmlSourceKind.File:
                    {
                        string pathJs = Expression(xmlFrom.Value);
                        return XmlConstructor($"((_t) => {{ const _d = new DOMParser().parseFromString(_t, 'application/xml'); const _pe = _d.getElementsByTagName('parsererror')[0]; if (_pe) {{ throw new Error('This is not valid XML, so Otter could not read it: ' + _pe.textContent); }} return _d; }})(await otterReadFile({pathJs}))");
                    }
                    default:
                    {
                        string nameJs = Expression(xmlFrom.Value);
                        return XmlConstructor($"((_n) => {{ const _d = document.implementation.createDocument(null, _n, null); return _d; }})(String({nameJs}))");
                    }
                }
            case XmlSelectExpr select:
            {
                string xmlJs = Expression(select.Xml);
                string guard = $"((_x) => {{ if (!_x || typeof _x !== 'object' || !_x.__otterXml) {{ throw new Error('I can only look for elements in xml, but this is something else.'); }} return _x; }})({xmlJs})";
                switch (select.Kind)
                {
                    case XmlSelectKind.Element:
                    {
                        string nameJs = Expression(select.Selector);
                        return $"((_x, _n) => {{ const _found = ({XmlFindByNameFunc})(_x.node, String(_n)); return _found.length > 0 ? ({XmlConstructor("_found[0]")}) : null; }})({guard}, {nameJs})";
                    }
                    case XmlSelectKind.Elements:
                    {
                        string nameJs = Expression(select.Selector);
                        return $"((_x, _n) => (({XmlFindByNameFunc})(_x.node, String(_n))).map((_e) => ({XmlConstructor("_e")})))({guard}, {nameJs})";
                    }
                    case XmlSelectKind.Child:
                    {
                        string idxJs = Expression(select.Selector);
                        return $"((_x, _i) => {{ const _kids = ({XmlChildElementsFunc})(_x.node); const _ii = Number(_i); return (_ii >= 0 && _ii < _kids.length) ? ({XmlConstructor("_kids[_ii]")}) : null; }})({guard}, {idxJs})";
                    }
                    default:
                        return $"((_x) => (({XmlChildElementsFunc})(_x.node)).map((_e) => ({XmlConstructor("_e")})))({guard})";
                }
            }
            case XmlAttributeExpr attribute:
            {
                string nameJs = Expression(attribute.Name);
                string elemJs = Expression(attribute.Element);
                return $"((_e, _n) => {{ if (!_e || typeof _e !== 'object' || !_e.__otterXml) {{ throw new Error('I can only read an attribute of an xml element, but this is something else.'); }} return _e.node.hasAttribute(String(_n)) ? _e.node.getAttribute(String(_n)) : null; }})({elemJs}, {nameJs})";
            }
            case XmlTextOfNameInExpr textOf:
            {
                string nameJs = Expression(textOf.Name);
                string xmlJs = Expression(textOf.Xml);
                return $"((_x, _n) => {{ if (!_x || typeof _x !== 'object' || !_x.__otterXml) {{ throw new Error('I can only look for elements in xml, but this is something else.'); }} const _found = ({XmlFindByNameFunc})(_x.node, String(_n)); if (_found.length === 0) {{ throw new Error('I could not find an element called ' + String(_n) + '.'); }} return _found[0].textContent; }})({xmlJs}, {nameJs})";
            }
            case XmlElementExistsExpr exists:
            {
                string xmlJs = Expression(exists.Xml);
                string nameJs = Expression(exists.Selector);
                return $"((_x, _n) => {{ if (!_x || typeof _x !== 'object' || !_x.__otterXml) {{ throw new Error('I can only look for elements in xml, but this is something else.'); }} return (({XmlFindByNameFunc})(_x.node, String(_n))).length > 0; }})({xmlJs}, {nameJs})";
            }
            case XmlHasAttributeExpr hasAttribute:
            {
                string elemJs = Expression(hasAttribute.Element);
                string nameJs = Expression(hasAttribute.Name);
                return $"((_e, _n) => {{ if (!_e || typeof _e !== 'object' || !_e.__otterXml) {{ throw new Error('I can only ask whether an xml element has an attribute, but this is something else.'); }} return _e.node.hasAttribute(String(_n)); }})({elemJs}, {nameJs})";
            }
            case XmlToTextExpr toText:
            {
                string xmlJs = Expression(toText.Xml);
                string prettyJs = toText.Pretty ? "true" : "false";
                return $"((_x) => {{ if (!_x || typeof _x !== 'object' || !_x.__otterXml) {{ throw new Error('I can only read text from xml, but this is something else.'); }} return {prettyJs} ? (({Templates.JsXmlPrettyFunc})(_x.node, _x.node.nodeType === 9)) : new XMLSerializer().serializeToString(_x.node); }})({xmlJs})";
            }
            case DragContextExpr drag:
                return $"otterDragField('{drag.Field}')";
            case SecureRandomBytesExpr random:
                return $"otterSecureRandomBytes({Expression(random.Count)})";
            case CryptoHashExpr hash:
            {
                string algoJs = $"'{hash.Algorithm.ToLowerInvariant()}'";
                string dataJs = Expression(hash.Data);
                return $"(await otterCryptoHash({algoJs}, {dataJs}))";
            }
            case CryptoHmacExpr hmac:
            {
                string algoJs = $"'{hmac.Algorithm.ToLowerInvariant()}'";
                string dataJs = Expression(hmac.Data);
                string keyJs = Expression(hmac.Key);
                return $"(await otterCryptoHmac({algoJs}, {dataJs}, {keyJs}))";
            }
            case PasswordMatchesExpr matches:
            {
                string pwdJs = Expression(matches.Password);
                string hashJs = Expression(matches.Hash);
                return $"(await otterPasswordMatches({pwdJs}, {hashJs}))";
            }
            case SecurelyEqualsExpr securely:
            {
                string leftJs = Expression(securely.Left);
                string rightJs = Expression(securely.Right);
                return $"otterSecurelyEquals({leftJs}, {rightJs})";
            }
            case DateDifferenceExpr difference:
            {
                string startJs = Expression(difference.Start);
                string endJs = Expression(difference.End);
                string diffJs = DateDifferenceExpression($"'{difference.Unit}'", "_s.value", "_e.value");
                return $"(() => {{ const _s = {startJs}; const _e = {endJs}; const _sOk = _s && typeof _s === 'object' && _s.__otterDate; const _eOk = _e && typeof _e === 'object' && _e.__otterDate; if (!_sOk) {{ throw new Error('I can only measure time between two dates, but the first one is something else.'); }} if (!_eOk) {{ throw new Error('I can only measure time between two dates, but the second one is something else.'); }} return {diffJs}; }})()";
            }
            case CallExpr call:
                return $"{call.Name}({string.Join(", ", call.Arguments.Select(Expression))})";
            case FileExistsExpr fileExists:
                return $"(await otterFileExists({Expression(fileExists.Path)}))";
            case FileLockedExpr fileLocked:
                return $"(await otterFileLocked({Expression(fileLocked.Path)}))";
            // D104: file watching is console and desktop only.
            case IsWatchingExpr or ChangedPathExpr or ChangedFileNameExpr or ChangeKindExpr or OldPathExpr:
                throw WebError("File/folder watching is not supported on the web target.", expr.Line);
            // D111: the credential vault is OS-backed and console-only.
            case SecretReadExpr:
                throw WebError("The credential vault (secret) is not supported on the web target. Browser storage is not a secure credential store.", expr.Line);
            case SecretExistsExpr:
                throw WebError("The credential vault (secret exists) is not supported on the web target. Browser storage is not a secure credential store.", expr.Line);
            // D106-D108, D112, D113, D116B: network contexts and states.
            case NetContextExpr netContext:
                if (string.Equals(netContext.Field, "response", StringComparison.OrdinalIgnoreCase)) return ReceivedResponseJs;
                throw WebError("TCP/UDP is not supported on the web target.", expr.Line);
            case ConnectionIsSecureExpr:
                throw WebError("TCP/TLS is not supported on the web target. Browsers cannot open raw TCP or TLS sockets.", expr.Line);
            case TcpServerIsStateExpr:
                throw WebError("TCP servers are not supported on the web target.", expr.Line);
            case WebSocketIsStateExpr wsState:
            {
                string targetJs = Expression(wsState.Socket);
                string stateStr = wsState.State.ToString().ToLowerInvariant();
                return $"((_s) => {{ if (!_s || typeof _s !== 'object' || !_s.__otterWebSocket) {{ throw new Error('I can only check the state of a websocket, but this is something else.'); }} const _st = (_s.ws.readyState === 0 ? 'connecting' : _s.ws.readyState === 1 ? 'open' : _s.ws.readyState === 2 ? 'closing' : 'closed'); return _st === '{stateStr}'; }})({targetJs})";
            }
            case HttpRequestIsStateExpr requestState:
            {
                string targetJs = Expression(requestState.Request);
                string stateStr = requestState.State switch
                {
                    HttpRequestState.Pending => "pending",
                    HttpRequestState.Completed => "completed",
                    HttpRequestState.Failed => "failed",
                    _ => "cancelled",
                };
                return $"((_s) => {{ if (!_s || typeof _s !== 'object' || !_s.__otterHttpRequest) {{ throw new Error('I can only check the state of an HTTP request, but got ' + (typeof _s)); }} return _s.state === '{stateStr}'; }})({targetJs})";
            }
            case ReceivedResponseExpr:
                return ReceivedResponseJs;
            case ReceivedMessageExpr:
                return WsContextJs("message", "\\\"received message\\\" is only available inside \\\"on message from ...\\\".");
            case CloseCodeExpr:
                return WsContextJs("closeCode", "\\\"close code\\\" is only available inside \\\"on close of ...\\\".");
            case CloseReasonExpr:
                return WsContextJs("closeReason", "\\\"close reason\\\" is only available inside \\\"on close of ...\\\".");
            case CloseWasCleanExpr:
                return WsContextJs("closeWasClean", "\\\"close was clean\\\" is only available inside \\\"on close of ...\\\".");
            case WebSocketErrorExpr:
                return WsContextJs("error", "\\\"websocket error\\\" is only available inside \\\"on error of ...\\\".");
            // D119-R2: a command job's context has no web compilation.
            case JobContextExpr:
                return "null";
            // D73, D74, D78: runtime hooks a host provides.
            case FileIsSymbolicLinkExpr symbolicLink:
                return $"(await otterFileIsSymbolicLink({Expression(symbolicLink.Path)}))";
            case FileIsReadOnlyExpr readOnly:
                return $"(await otterFileIsReadOnly({Expression(readOnly.Path)}))";
            case RegistryKeyExistsExpr keyExists:
                return $"(await otterRegistryKeyExists({Expression(keyExists.KeyPath)}))";
            // D99: queries run against a database.
            case QueryBetweenExpr or QueryInExpr:
                throw WebError("Database queries are not supported on the web target.", expr.Line);
            case BytesFromFileExpr:
                throw WebError("Binary file access is not supported on the web target.", expr.Line);
            default:
                throw new NotSupportedYet($"compiling {expr.GetType().Name} for the web", expr.Line);
        }
    }

    string PropertyAccess(PropertyAccessExpr access)
    {
        string propOriginal = access.Property;
        string prop = propOriginal.ToLowerInvariant();
        Expr target = access.Target;
        bool isVar = target is VariableExpr;
        string targetName = target is VariableExpr variable ? variable.Name : "";
        string targetJs = Expression(target);
        string uiId = isVar ? UiRef(targetName) : $"'{targetName}'";
        string uiBranch = prop switch
        {
            "text" => $"otterGetText({uiId})",
            "value" => $"otterGetText({uiId})",
            "title" => $"otterGetTitle({uiId})",
            "width" => $"otterGetStyle({uiId}, 'width')",
            "height" => $"otterGetStyle({uiId}, 'height')",
            _ => $"otterGetProperty({uiId}, '{prop}')",
        };
        string dateBranch = prop switch
        {
            "year" => "_owner.value.getFullYear()",
            "month" => "(_owner.value.getMonth() + 1)",
            "day" => "_owner.value.getDate()",
            "hour" => "(_owner.hasTime ? _owner.value.getHours() : (() => { throw new Error('This is a date with no time of day, so it has no hour.'); })())",
            "minute" => "(_owner.hasTime ? _owner.value.getMinutes() : (() => { throw new Error('This is a date with no time of day, so it has no minute.'); })())",
            "second" => "(_owner.hasTime ? _owner.value.getSeconds() : (() => { throw new Error('This is a date with no time of day, so it has no second.'); })())",
            _ => $"(() => {{ throw new Error('A date has no part called \"{propOriginal}\".'); }})()",
        };
        string xmlBranch = prop switch
        {
            "name" => "(_owner.node.nodeType === 1 ? _owner.node.nodeName : (() => { throw new Error('I can only read the name of on an xml element, but this is an xml document.'); })())",
            "root" => $"(_owner.node.nodeType === 9 ? (_owner.node.documentElement ? ({XmlConstructor("_owner.node.documentElement")}) : null) : (() => {{ throw new Error('\"root of\" only makes sense on an xml document, not an element.'); }})())",
            "text" => "_owner.node.textContent",
            "attributes" => "(_owner.node.nodeType === 1 ? Array.prototype.map.call(_owner.node.attributes, (a) => a.name) : (() => { throw new Error('I can only read the attributes of on an xml element, but this is an xml document.'); })())",
            _ => $"(() => {{ throw new Error('This xml value has no property called \"{propOriginal}\". Try name, root, text, or attributes.'); }})()",
        };
        string httpBranch = $"if ('{propOriginal}' === 'state') return _owner.state; if ('{propOriginal}' === 'response') {{ if (_owner.state === 'pending') throw new Error('The HTTP request has not completed yet.'); if (_owner.state === 'cancelled') throw new Error('The HTTP request was cancelled.'); if (_owner.state === 'failed') throw new Error('The HTTP request failed: ' + _owner.error); return _owner.response; }} if ('{propOriginal}' === 'error') {{ return (_owner.state === 'failed' ? _owner.error : null); }} if ('{propOriginal}' === 'status') {{ if (_owner.state === 'pending') throw new Error('The HTTP request has not completed yet.'); return _owner.status; }} throw new Error('This http request has no property called \"{propOriginal}\". Try state, response, error, or status.');";
        string rest = $"if (_owner && typeof _owner === 'object' && _owner.__otterHttpRequest) {{ {httpBranch} }} if (_owner && typeof _owner === 'object' && _owner.__otterXml) {{ return {xmlBranch}; }} if (_owner && typeof _owner === 'object' && _owner.__otterDate) {{ return {dateBranch}; }} if (_owner && typeof _owner === 'object' && _owner.__otterWebSocket) {{ if ('{propOriginal}' === 'state') return (_owner.ws.readyState === 0 ? 'connecting' : _owner.ws.readyState === 1 ? 'open' : _owner.ws.readyState === 2 ? 'closing' : 'closed'); if ('{propOriginal}' === 'url') return _owner.url; if ('{propOriginal}' === 'protocol') return _owner.ws.protocol || _owner.protocol || ''; throw new Error('A websocket has no property called \"{propOriginal}\".'); }} if (!_owner || typeof _owner !== 'object' || !_owner.__otterThing) {{ throw new Error('I can only read properties of a thing, but this is something else.'); }} if (!(('{propOriginal}') in _owner.props)) {{ throw new Error('This ' + (_owner.typeName || 'thing') + ' has no property called \"{propOriginal}\".'); }} return _owner.props['{propOriginal}']; }})())";
        if (isVar)
        {
            return $"(otterGetElement({uiId}) ? ({uiBranch}) : (() => {{ const _owner = {targetName}; " + rest;
        }
        return $"((() => {{ const _owner = ({targetJs}); " + rest;
    }
}
