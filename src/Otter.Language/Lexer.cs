using System.Globalization;
using System.Text;

namespace Otter.Language;

// Source text becomes tokens. A line-by-line port of otterPS's
// src/Otter.Lexer.psm1: the same token kinds, the same multi-word phrases,
// the same errors at the same columns.
//
// PowerShell compares strings case-insensitively (its hashtables, -eq, -in
// and switch all ignore case), so every word comparison here does too:
// `SAY "hi"` is the say statement, as it is in otterPS.
public static class Lexer
{
    static readonly StringComparer Words = StringComparer.OrdinalIgnoreCase;

    static readonly Dictionary<string, TokenKind> Keywords = new(Words)
    {
        ["say"] = TokenKind.Say, ["ask"] = TokenKind.Ask, ["if"] = TokenKind.If,
        ["while"] = TokenKind.While, ["repeat"] = TokenKind.Repeat, ["count"] = TokenKind.Count,
        ["as"] = TokenKind.As, ["in"] = TokenKind.In, ["return"] = TokenKind.Return,
        ["is"] = TokenKind.Is, ["or"] = TokenKind.Or, ["not"] = TokenKind.Not,
        ["and"] = TokenKind.And, ["plus"] = TokenKind.And, ["minus"] = TokenKind.Minus,
        ["times"] = TokenKind.Times,
        ["percent"] = TokenKind.Percent,   // D88: X percent of Y
        ["power"] = TokenKind.Power,       // D88: X power Y
        ["make"] = TokenKind.Make, ["makes"] = TokenKind.Make,
        ["add"] = TokenKind.Add, ["remove"] = TokenKind.Remove, ["to"] = TokenKind.To,
        ["from"] = TokenKind.From, ["are"] = TokenKind.Are, ["of"] = TokenKind.Of,
        ["when"] = TokenKind.When, ["put"] = TokenKind.Put, ["show"] = TokenKind.Show,
        ["gone"] = TokenKind.Gone, ["files"] = TokenKind.Files, ["folders"] = TokenKind.Folders,
        ["folder"] = TokenKind.Folder, ["subfolders"] = TokenKind.Subfolders,
        ["with"] = TokenKind.With, ["by"] = TokenKind.By, ["where"] = TokenKind.Where,
        // Reserved for later language versions, so a future keyword cannot
        // silently change an existing program's meaning.
        ["a"] = TokenKind.A, ["an"] = TokenKind.A, ["has"] = TokenKind.Has,
        ["file"] = TokenKind.File, ["into"] = TokenKind.Into, ["set"] = TokenKind.Set,
        ["command"] = TokenKind.Command, ["open"] = TokenKind.Open,
        ["true"] = TokenKind.True, ["false"] = TokenKind.False, ["at"] = TokenKind.At,
        ["await"] = TokenKind.Await,
        // D99: query language keywords
        ["distinct"] = TokenKind.Distinct, ["then"] = TokenKind.Then,
        ["ascending"] = TokenKind.Ascending, ["descending"] = TokenKind.Descending,
    };

    // D33 mechanism 2: these words are keywords only as the first word of a
    // line; anywhere else they start as ordinary identifiers.
    static readonly Dictionary<string, TokenKind> StatementHeadKeywords = new(Words)
    {
        ["copy"] = TokenKind.Copy, ["move"] = TokenKind.Move, ["delete"] = TokenKind.Delete,
        ["create"] = TokenKind.Create, ["read"] = TokenKind.Read, ["write"] = TokenKind.Write,
        ["append"] = TokenKind.Append, ["sort"] = TokenKind.Sort, ["reverse"] = TokenKind.Reverse,
        ["replace"] = TokenKind.Replace, ["split"] = TokenKind.Split, ["join"] = TokenKind.Join,
        ["find"] = TokenKind.Find, ["get"] = TokenKind.Get, ["try"] = TokenKind.Try,
        ["run"] = TokenKind.Run, ["log"] = TokenKind.Log, ["warn"] = TokenKind.Warn,
        ["error"] = TokenKind.Problem, ["random"] = TokenKind.Random, ["json"] = TokenKind.Json,
        ["convert"] = TokenKind.Convert, ["format"] = TokenKind.Format, ["today"] = TokenKind.Today,
        ["now"] = TokenKind.Now, ["between"] = TokenKind.Between, ["otherwise"] = TokenKind.Otherwise,
        ["increase"] = TokenKind.Add, ["decrease"] = TokenKind.Remove,
        ["each"] = TokenKind.ForEach, ["stop"] = TokenKind.Return,
        ["post"] = TokenKind.Post, ["respond"] = TokenKind.Respond, ["start"] = TokenKind.Start,
        ["listen"] = TokenKind.Listen,
        ["notify"] = TokenKind.Notify, ["choose"] = TokenKind.Choose,   // D67
        ["fail"] = TokenKind.Fail,                                      // D68
        ["kill"] = TokenKind.Kill,                                      // D70
        ["wait"] = TokenKind.Wait,                                      // D71
        ["lock"] = TokenKind.Lock, ["sign"] = TokenKind.Sign,           // D82
        ["restart"] = TokenKind.Restart, ["shut"] = TokenKind.Shut,
        ["print"] = TokenKind.Print,                                    // D83
        ["zip"] = TokenKind.Zip, ["unzip"] = TokenKind.Unzip,           // D87
        ["hash"] = TokenKind.Hash,                                      // D91
        ["encrypt"] = TokenKind.Encrypt, ["decrypt"] = TokenKind.Decrypt, // D92
        ["download"] = TokenKind.Download,                              // D96
        ["connect"] = TokenKind.Connect, ["disconnect"] = TokenKind.Disconnect,   // D97
        ["query"] = TokenKind.Query, ["execute"] = TokenKind.Execute,
        ["commit"] = TokenKind.Commit, ["rollback"] = TokenKind.Rollback,
        ["parameter"] = TokenKind.Parameter,
        ["sum"] = TokenKind.Sum, ["average"] = TokenKind.Average,       // D99
        ["minimum"] = TokenKind.Minimum, ["maximum"] = TokenKind.Maximum,
        ["layout"] = TokenKind.Layout, ["gap"] = TokenKind.Gap,         // D56
        ["state"] = TokenKind.State, ["derive"] = TokenKind.Derive,
        ["memo"] = TokenKind.Memo, ["on"] = TokenKind.On,
        ["await"] = TokenKind.Await, ["shared"] = TokenKind.Shared,
        ["use"] = TokenKind.Use, ["focus"] = TokenKind.Focus,
        ["hide"] = TokenKind.Hide, ["animate"] = TokenKind.Animate,
        ["motion"] = TokenKind.Motion,
    };

    // D32: singular and plural spell the same unit.
    static readonly Dictionary<string, TokenKind> TimeUnitWords = new(Words)
    {
        ["year"] = TokenKind.Year, ["years"] = TokenKind.Year,
        ["month"] = TokenKind.Month, ["months"] = TokenKind.Month,
        ["day"] = TokenKind.Day, ["days"] = TokenKind.Day,
        ["hour"] = TokenKind.Hour, ["hours"] = TokenKind.Hour,
        ["minute"] = TokenKind.Minute, ["minutes"] = TokenKind.Minute,
        ["second"] = TokenKind.Second, ["seconds"] = TokenKind.Second,
        ["millisecond"] = TokenKind.Millisecond, ["milliseconds"] = TokenKind.Millisecond,  // D101
    };

    static readonly HashSet<TokenKind> TimeUnitKinds = new(TimeUnitWords.Values);

    // Kinds after which `contains` / `exists` are the operator, not a name.
    static readonly HashSet<TokenKind> ValueLikeKinds = new()
    {
        TokenKind.Identifier, TokenKind.File, TokenKind.Files, TokenKind.Folder, TokenKind.Folders,
        TokenKind.String, TokenKind.Number, TokenKind.True, TokenKind.False, TokenKind.Gone,
    };

    static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<Token> Tokenize(string source)
    {
        var all = new List<Token>();
        int indentLevel = 0;
        // PowerShell: $Source -split "`r?`n"
        string[] lines = source.Replace("\r\n", "\n").Split('\n');
        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            int lineNumber = index + 1;
            var (level, characters) = IndentLevel(line, lineNumber);
            string content = line.Substring(characters);
            if (IsBlankOrComment(content)) continue;

            if (level > indentLevel + 1)
            {
                throw new OtterError("Indentation cannot jump more than one level at a time.", lineNumber, "lexer", 1, line, "Indent one level at a time.");
            }
            while (indentLevel > level) { all.Add(new Token(TokenKind.Dedent, "", null, lineNumber, 1)); indentLevel--; }
            while (indentLevel < level) { all.Add(new Token(TokenKind.Indent, "", null, lineNumber, 1)); indentLevel++; }

            if (content.Trim() == ".")
            {
                all.Add(new Token(TokenKind.BlockEnd, ".", null, lineNumber, characters + 1));
            }
            else
            {
                all.AddRange(LineTokens(content, lineNumber, characters));
            }
            all.Add(new Token(TokenKind.Newline, "", null, lineNumber, line.Length + 1));
        }
        while (indentLevel > 0) { all.Add(new Token(TokenKind.Dedent, "", null, lines.Length + 1, 1)); indentLevel--; }
        all.Add(new Token(TokenKind.EndOfFile, "", null, lines.Length + 1, 1));
        return all;
    }

    // PowerShell: $content -match '^\s*(#.*)?$'
    static bool IsBlankOrComment(string content)
    {
        int i = 0;
        while (i < content.Length && char.IsWhiteSpace(content[i])) i++;
        return i == content.Length || content[i] == '#';
    }

    static (int Level, int Characters) IndentLevel(string line, int lineNumber)
    {
        int index = 0, level = 0;
        while (index < line.Length)
        {
            if (line[index] == '\t') { index++; level++; continue; }
            if (line[index] != ' ') break;
            int spaceStart = index;
            while (index < line.Length && line[index] == ' ') index++;
            int spaceCount = index - spaceStart;
            if (spaceCount % 4 != 0)
            {
                throw new OtterError("Indentation must use groups of 4 spaces or tabs.", lineNumber, "lexer",
                    spaceStart + 1, line, "Use 4 spaces for each indentation level.");
            }
            level += spaceCount / 4;
        }
        return (level, index);
    }

    static List<Token> LineTokens(string text, int lineNumber, int columnOffset)
    {
        var tokens = new List<Token>();
        int index = 0;
        bool isStatementHead = true;
        while (index < text.Length)
        {
            char character = text[index];
            int column = columnOffset + index + 1;
            if (char.IsWhiteSpace(character)) { index++; continue; }
            if (character == '#') break;

            if (character == '"')
            {
                int start = index;
                index++;
                var value = new StringBuilder();
                bool closed = false;
                while (index < text.Length)
                {
                    if (text[index] == '"') { index++; closed = true; break; }
                    if (text[index] == '\\' && index + 1 < text.Length)
                    {
                        index++;
                        switch (text[index])
                        {
                            case 'n': value.Append('\n'); break;
                            case 't': value.Append('\t'); break;
                            case '"': value.Append('"'); break;
                            case '\\': value.Append('\\'); break;
                            default: value.Append('\\').Append(text[index]); break;
                        }
                        index++;
                        continue;
                    }
                    value.Append(text[index]);
                    index++;
                }
                if (!closed)
                {
                    throw new OtterError("This string never closes.", lineNumber, "lexer", column, text, "Add a closing quote.");
                }
                tokens.Add(new Token(TokenKind.String, text.Substring(start, index - start), value.ToString(), lineNumber, column));
                continue;
            }

            if (char.IsDigit(character))
            {
                int start = index;
                while (index < text.Length && char.IsDigit(text[index])) index++;
                if (index < text.Length && text[index] == '.' && index + 1 < text.Length && char.IsDigit(text[index + 1]))
                {
                    index++;
                    while (index < text.Length && char.IsDigit(text[index])) index++;
                }
                string numberText = text.Substring(start, index - start);
                double number = double.Parse(numberText, CultureInfo.InvariantCulture);
                tokens.Add(new Token(TokenKind.Number, numberText, number, lineNumber, column));
                continue;
            }

            if (char.IsLetter(character) || character == '_')
            {
                int start = index;
                while (index < text.Length && (char.IsLetterOrDigit(text[index]) || text[index] == '_')) index++;
                string word = text.Substring(start, index - start);
                TokenKind kind;
                if (isStatementHead && StatementHeadKeywords.TryGetValue(word, out var headKind)) kind = headKind;
                else if (Keywords.TryGetValue(word, out var keywordKind)) kind = keywordKind;
                else kind = TokenKind.Identifier;
                object? tokenValue = kind switch
                {
                    TokenKind.True => true,
                    TokenKind.False => false,
                    _ => word,
                };
                tokens.Add(new Token(kind, word, tokenValue, lineNumber, column));
                isStatementHead = false;
                continue;
            }

            if (character == '.')
            {
                throw new OtterError("Otter does not use periods to access properties.", lineNumber, "lexer", column, text,
                    "Use \"name of person\" instead of \"person.name\".");
            }

            // Commas are punctuation for the multi-item `put` form, carried as
            // an identifier whose text is "," (the frozen contract's choice).
            if (character == ',') { tokens.Add(new Token(TokenKind.Identifier, ",", ",", lineNumber, column)); index++; isStatementHead = false; continue; }
            if (character == '+') { tokens.Add(new Token(TokenKind.And, "+", null, lineNumber, column)); index++; isStatementHead = false; continue; }
            if (character == '*') { tokens.Add(new Token(TokenKind.Times, "*", null, lineNumber, column)); index++; isStatementHead = false; continue; }
            if (character == '-') { tokens.Add(new Token(TokenKind.Minus, "-", null, lineNumber, column)); index++; isStatementHead = false; continue; }
            if (character == '/') { tokens.Add(new Token(TokenKind.DividedBy, "/", null, lineNumber, column)); index++; isStatementHead = false; continue; }

            if (character == '=')
            {
                string suggestion = "Otter uses 'is' to assign values.";
                var stateMatch = System.Text.RegularExpressions.Regex.Match(text, @"^\s*state\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (stateMatch.Success)
                {
                    string right = stateMatch.Groups[2].Value.Trim().Length > 0 ? stateMatch.Groups[2].Value.Trim() : "0";
                    suggestion = $"Otter uses 'is' to assign values.\n\nTry:\nstate {stateMatch.Groups[1].Value} is {right}";
                }
                throw new OtterError("Otter does not use '=' to assign values.", lineNumber, "lexer", column, text, suggestion);
            }

            throw new OtterError($"I don't understand '{character}'.", lineNumber, "lexer", column, text, "Use Otter words such as say or if.");
        }

        return CombinePhrases(tokens);
    }

    // Multi-word language phrases each become one token, each only in its
    // own context, in the same order of rules as otterPS.
    static List<Token> CombinePhrases(List<Token> tokens)
    {
        var combined = new List<Token>();
        for (int i = 0; i < tokens.Count; i++)
        {
            Token token = tokens[i];
            Token? At(int offset) => i + offset < tokens.Count ? tokens[i + offset] : null;
            void Add(TokenKind kind, string text) => combined.Add(new Token(kind, text, null, token.Line, token.Column));
            bool IsIdentifier(string word) => token.Kind == TokenKind.Identifier && Same(token.Text, word);

            if (IsIdentifier("ease") && At(2) is { } easeWord && At(1)!.Kind == TokenKind.Minus &&
                (Same(easeWord.Text, "out") || Same(easeWord.Text, "in")))
            {
                string phrase = "ease-" + easeWord.Text;
                combined.Add(new Token(TokenKind.Identifier, phrase, phrase, token.Line, token.Column));
                i += 2;
                continue;
            }
            if (token.Kind == TokenKind.Is && At(2) is { } least && Same(At(1)!.Text, "at") && Same(least.Text, "least"))
            {
                Add(TokenKind.IsAtLeast, "is at least"); i += 2; continue;
            }
            Token? previous = combined.Count > 0 ? combined[^1] : null;
            if (token.Kind == TokenKind.Is && At(1) is { } next)
            {
                if (Same(next.Text, "in")) { Add(TokenKind.IsIn, "is in"); i++; continue; }
                if (Same(next.Text, "not") && At(2) is { } inWord && Same(inWord.Text, "in")) { Add(TokenKind.IsNotIn, "is not in"); i += 2; continue; }
                if (Same(next.Text, "between")) { Add(TokenKind.IsBetween, "is between"); i++; continue; }
                if (Same(next.Text, "not")) { Add(TokenKind.IsNot, "is not"); i++; continue; }
                if (At(2) is { } third)
                {
                    if (Same(next.Text, "at") && Same(third.Text, "most")) { Add(TokenKind.IsAtMost, "is at most"); i += 2; continue; }
                    if (Same(next.Text, "greater") && Same(third.Text, "than")) { Add(TokenKind.IsGreaterThan, "is greater than"); i += 2; continue; }
                    if (Same(next.Text, "less") && Same(third.Text, "than")) { Add(TokenKind.IsLessThan, "is less than"); i += 2; continue; }
                }
            }
            if (IsIdentifier("order") && At(1) is { } byWord && Same(byWord.Text, "by")) { Add(TokenKind.OrderBy, "order by"); i++; continue; }   // D99
            if (IsIdentifier("for") && At(1) is { } eachWord && Same(eachWord.Text, "each")) { Add(TokenKind.ForEach, "for each"); i++; continue; }
            // D100: `console is interactive` is one token; "console" stays a name elsewhere.
            if (IsIdentifier("console") && At(2) is { } interactive && At(1)!.Kind == TokenKind.Is && Same(interactive.Text, "interactive"))
            {
                Add(TokenKind.ConsoleInteractive, "console is interactive"); i += 2; continue;
            }
            if (IsIdentifier("begin") && At(1) is { } transaction && Same(transaction.Text, "transaction")) { Add(TokenKind.BeginTransaction, "begin transaction"); i++; continue; }   // D97
            // D33 mechanism 1: structural only in their fixed neighbouring phrase.
            if (IsIdentifier("empty") && previous is { Kind: TokenKind.Are }) { Add(TokenKind.Empty, "empty"); continue; }
            if (IsIdentifier("contains") && previous != null && ValueLikeKinds.Contains(previous.Kind)) { Add(TokenKind.Contains, "contains"); continue; }
            if (IsIdentifier("exists") && previous != null && ValueLikeKinds.Contains(previous.Kind)) { Add(TokenKind.Exists, "exists"); continue; }
            if (IsIdentifier("call") && previous is { Kind: TokenKind.And } && At(1) is { } itWord && Same(itWord.Text, "it")) { Add(TokenKind.Call, "call"); continue; }
            if (IsIdentifier("it") && previous is { Kind: TokenKind.Call } && combined.Count >= 2 && combined[^2].Kind == TokenKind.And) { Add(TokenKind.It, "it"); continue; }
            // D29: json is structural in its own productions, a free name elsewhere.
            if (IsIdentifier("json") && previous != null &&
                previous.Kind is TokenKind.Read or TokenKind.To or TokenKind.From or TokenKind.Get or TokenKind.As)
            {
                Add(TokenKind.Json, "json"); continue;
            }
            // D95: csv only in the four CSV statement forms.
            if (IsIdentifier("csv") && previous != null &&
                previous.Kind is TokenKind.Read or TokenKind.Write or TokenKind.To or TokenKind.From)
            {
                Add(TokenKind.Csv, "csv"); continue;
            }
            if (IsIdentifier("receives")) { Add(TokenKind.Receives, "receives"); continue; }   // D51

            // Derived operations only right before `of`: `first is "Jeff"` keeps
            // first as a name; `first of games` is the operation.
            if (token.Kind == TokenKind.Identifier && At(1) is { Kind: TokenKind.Of })
            {
                TokenKind? operation = token.Text.ToLowerInvariant() switch
                {
                    "length" => TokenKind.Length,
                    "uppercase" => TokenKind.Uppercase,
                    "lowercase" => TokenKind.Lowercase,
                    "first" => TokenKind.First,
                    "last" => TokenKind.Last,
                    "round" => TokenKind.Round,       // D89
                    "larger" => TokenKind.Larger,     // D89
                    "smaller" => TokenKind.Smaller,   // D89
                    "sine" => TokenKind.Sine,         // D90
                    "cosine" => TokenKind.Cosine,
                    "tangent" => TokenKind.Tangent,
                    "log" => TokenKind.LogTen,
                    _ => null,
                };
                if (operation is { } op) { combined.Add(new Token(op, token.Text, null, token.Line, token.Column)); continue; }
            }
            // D89: two-word operation names, also only right before `of`.
            if (token.Kind == TokenKind.Identifier && At(2) is { Kind: TokenKind.Of } && At(1) is { Kind: TokenKind.Identifier } second)
            {
                TokenKind? twoWord = (token.Text + " " + second.Text).ToLowerInvariant() switch
                {
                    "absolute value" => TokenKind.AbsoluteValue,
                    "square root" => TokenKind.SquareRoot,
                    "round up" => TokenKind.RoundUp,
                    "round down" => TokenKind.RoundDown,
                    "natural log" => TokenKind.NaturalLog,                    // D90
                    "elapsed time" => TokenKind.ElapsedTime,                  // D101
                    "elapsed milliseconds" => TokenKind.ElapsedMilliseconds,  // D101
                    _ => null,
                };
                if (twoWord is { } two)
                {
                    combined.Add(new Token(two, token.Text + " " + second.Text, null, token.Line, token.Column));
                    i++;
                    continue;
                }
            }
            if (token.Kind == TokenKind.Identifier && At(1) is { Kind: TokenKind.With })
            {
                if (Same(token.Text, "starts")) { Add(TokenKind.StartsWith, "starts with"); i++; continue; }
                if (Same(token.Text, "ends")) { Add(TokenKind.EndsWith, "ends with"); i++; continue; }
            }
            // A time unit only where a unit can appear (D32): after a number,
            // before "between", or as the unit in `add amount days to date`.
            if (token.Kind == TokenKind.Identifier && TimeUnitWords.TryGetValue(token.Text, out var unitKind))
            {
                Token? before = combined.Count > 0 ? combined[^1] : null;
                Token? after = At(1);
                bool afterNumber = before is { Kind: TokenKind.Number };
                bool beforeBetween = after != null && Same(after.Text, "between");
                Token? lineStart = combined.Count > 0 ? combined[0] : null;
                bool adjustWithAmount = lineStart is { Kind: TokenKind.Add or TokenKind.Remove } && combined.Count >= 2;
                bool beforeToOrFrom = adjustWithAmount && after != null &&
                    (after.Kind is TokenKind.To or TokenKind.From || Same(after.Text, "to") || Same(after.Text, "from"));
                if (afterNumber || beforeBetween || beforeToOrFrom)
                {
                    combined.Add(new Token(unitKind, token.Text, null, token.Line, token.Column));
                    continue;
                }
            }
            // D32: `days between ...` keeps the date-difference meaning.
            if (IsIdentifier("between") && previous != null && TimeUnitKinds.Contains(previous.Kind)) { Add(TokenKind.Between, "between"); continue; }
            if (IsIdentifier("divided") && At(1) is { } byAfter && Same(byAfter.Text, "by")) { Add(TokenKind.DividedBy, "divided by"); i++; continue; }

            combined.Add(token);
        }
        return combined;
    }
}
