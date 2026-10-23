# Otter 1.0.1: syntax coverage inventory

Status: Phase 0 audit, 2026-10-22 (branch `web/phase0-audit`). Read-only audit: nothing in either repository was changed.

## Sources and method

- **Authoritative (otterPS 1.0.1, frozen PowerShell reference):** `otterPS-1.0.1`. `docs/GRAMMAR.md` (EBNF, cited as GRAMMAR.md l.N), `docs/SPECIFICATION.md`, `docs/SEMANTICS.md`, `docs/OTTER_1_0_RESERVED_WORDS.md`, `docs/OTTER_1_0_LANGUAGE_AND_RUNTIME_INVENTORY.md` ("INVENTORY"/"INV"), `docs/STANDARD_LIBRARY.md`, `SPEC-DECISIONS.md` ("SD", cited by D-number and heading line), `rules*.md`, `conformance/` (manifest.json and .ot cases), `tests/*.Tests.ps1` (grep only).
- **otter-net (C# port, byte-compatible):** `otter-net`. Lexer/parser/AST in `src/Otter.Language` (`Lexer.cs`, `Parser.cs`, `Parser.{Data,Network,System,Ui}.cs`, `Syntax*.cs`), interpreter in `src/Otter.Runtime/Interpreter*.cs`, module resolution in `src/Otter.Cli/Modules.cs`, open questions in `docs/SPEC-QUESTIONS.md` ("SQ"). Test names without a folder are programs in `tests/compat/` (each `.ot` program is a test).
- **Binary used for verification:** Native AOT otter-net `otter` ("Otter 1.0.1") (the otter-net 1.0.1 Native AOT binary, macOS arm64), macOS. Every call was time-limited: `perl -e 'alarm 20; exec @ARGV' otter check|run FILE`. Samples live under `samples/syntax/` next to this file. Programs that would start servers, open windows/dialogs, touch the network, clipboard, registry, credentials, printers, power state or processes were only `check`ed, never run.
- **Rules for the Syntax column:** every form is copied from the grammar, a spec/decision document, a parser keyword check or error suggestion, or a test program. UPPER-case words and `X`/`N`/`P` are placeholders; `[...]` is optional, `a|b` is a choice.
- Sections 1-13 were audited directly; sections 14-17 were audited per area in parallel and use the abbreviations given at the top of each section.

## Summary

| # | Category | Constructs (table rows) | Samples (check passed / total working samples) |
|---|---|---|---|
| 1-13 | Core language: lexical, values, operators, conditions, control flow, functions, collections/things, text, math, dates, console I/O, errors, modules | 85 | 7 / 7 (all also run OK) |
| 14 | Files, folders and data formats (files, folders, ZIP, bytes, XML, JSON, CSV) | 65 | 3 / 3 (all also run OK) |
| 15 | System, processes, security and console UX (env, processes, run/remote/ssh, Windows admin, console UX, timing, random, crypto) | 60 | 2 / 2 (1 also run OK; 1 check only) |
| 16 | Network, events and concurrency (HTTP, web server, WebSocket, TCP/TLS, UDP, file watching, command jobs, event loop) | 71 | 4 / 4 (check only) |
| 17 | UI, reactivity, routing; databases and queries | 57 | 3 / 3 (database sample also run OK; UI check only) |
| | **Total** | **338** | **19 / 19 working samples pass `check`** (plus intentionally failing negative samples and ~70 one-line probes) |

Row counts include a few rows that group alternate spellings or sub-forms (e.g. the four power actions are one row).

## 1. Lexical structure and layout

| Construct | Syntax | Semantics | Reference | otter-net | Tests |
|---|---|---|---|---|---|
| Comment | `# text` (whole line or after code) | Ignored to end of line; comment-only and blank lines never affect indentation | GRAMMAR.md §1.1 l.21; SPECIFICATION.md §2.3; SPEC-DECISIONS D10 | Lexer.cs `IsBlankOrComment`, `LineTokens` (`#` breaks) | most compat programs (e.g. functions, modules/main); conformance negative/*.ot |
| Indentation blocks | one level = 4 spaces or 1 tab; Indent/Dedent tokens | Indentation opens/closes blocks; a jump of more than one level or a non-multiple of 4 spaces is a lexer error | GRAMMAR.md §1.2 l.27-28; SPEC-DECISIONS D7 | Lexer.cs `IndentLevel`, `Tokenize` | syn_indent, c4_lex_tab_spaces, c4_lex_two_spaces, deep_nesting |
| Block terminator `.` | a line containing only `.` | Optionally closes the block it follows (dedent alone also closes it); a stray `.` is an error "There is no open block for this period to close." | GRAMMAR.md l.29, l.158 (`[ BlockEnd ]`); SPEC-DECISIONS D4, D18 | Lexer.cs (`BlockEnd`); Parser.cs `ReadBlock`, `Parse` (UnexpectedPeriod) | syn_blocks, syn_trailing, ~98 compat programs use it |
| Number literal | `Digits [ "." Digits ]` e.g. `42`, `3.5` | IEEE-754 double; no sign, exponent or grouping; `-5` is a syntax error (write `0 minus 5`) | GRAMMAR.md §1.3 l.38, l.48-50; SPEC-DECISIONS D5; SEMANTICS.md §9 | Lexer.cs `LineTokens` (digits) -> `LiteralExpr` | numbers, math_ops, syn_lexer |
| Text literal and escapes | `"..."` with `\n` `\t` `\\` `\"` | One-line double-quoted text; other backslash pairs kept verbatim; otter-net (like otterPS) also accepts `\N`/`\T` | GRAMMAR.md §1.3 l.39-41, l.52-57 | Lexer.cs `LineTokens` (string branch) | escape_capitals, escapes_case, syn_lexer, syn_lexer2 |
| Boolean literals | `true` / `false` | Boolean values | GRAMMAR.md l.42 | Lexer.cs keyword table -> `LiteralExpr` | conditions, conformance core/variables_math_control_flow |
| `gone` | `gone` | The absence of a value (distinct from false/0/""/empty list) | GRAMMAR.md l.43; SPEC-DECISIONS D22 | Parser.cs `ReadValue` (`TokenKind.Gone`) | 23 programs (e.g. json, folders, csv) |
| Identifiers | `Letter { Letter \| Digit }` (letter includes `_`) | Names; reserved words rejected per OTTER_1_0_RESERVED_WORDS.md (D124); `a`/`an` cannot be a name | GRAMMAR.md §1.4 l.61-65; OTTER_1_0_RESERVED_WORDS.md; SPEC-DECISIONS D33, D124 | Lexer.cs; ReservedWords.cs; Parser.cs `ReadVariableName` | syn_reserved, common_names, net_syn_reserved, environment_names |
| Built-in literal names | `today`, `now`, `pi` | Always the built-in value; rejected as variable/function names | RESERVED_WORDS.md (built-in value); INVENTORY A1 | Parser.cs `ReadStatement`, `ReadVariableName`, `ReadValue` | sys_syntax_errors; conformance negative/reserved_literal_variable.ot |
| Forbidden punctuation | `.` as member access, `=`, `(`, `'` | Lexer errors: "Otter does not use periods to access properties.", "Otter does not use '=' to assign values.", "I don't understand '('." | SPEC-DECISIONS D4, D15; GRAMMAR.md §2.4 l.189-190 | Lexer.cs `LineTokens` | sys_syntax_errors, c4_lex_equals, c4_lex_state_equals |
| `the` readability article | `length of the name of person` | Optional `the` before a property target / some statement targets | SPEC-DECISIONS D52 (contextual `the`) | Parser.Data.cs `SkipReadabilityArticle`; Parser.cs `ReadValue(propertyTarget)` | data_the, data_syn_the, c4_core, colors |

## 2. Values, variables and assignment

| Construct | Syntax | Semantics | Reference | otter-net | Tests |
|---|---|---|---|---|---|
| Assignment | `Identifier is Expression` | Binds or updates a variable (outer scope variable is updated; new name created locally) | GRAMMAR.md §2.3 l.110; SEMANTICS.md §4; SPEC-DECISIONS D2, D28 | Parser.cs `ReadIdentifierStatement` -> `AssignStmt`; Interpreter.cs l.158 | nearly every program; conformance core/variables_math_control_flow |
| Negated assignment | `x is not value` | Assigns `not value` (parses as `AssignStmt` with `NotExpr`) | INVENTORY A3 (`not` is a general expression) | Parser.cs `ReadIdentifierStatement` (`TokenKind.IsNot`) | conditions |
| Property assignment | `PropertyChain is Expression`, e.g. `age of person is 30` | Sets (or creates) a property on the referenced thing | GRAMMAR.md l.111; SEMANTICS.md §5.3 | Parser.cs `ReadIdentifierStatement` (Of branch) -> `AssignStmt(PropertyAccessExpr)` | things, err_setprop, json, csv, data_the |
| Math-into (legacy) | `Expression make Identifier` (also `makes`), e.g. `5 plus 6 make total` | Evaluates arithmetic and stores it | GRAMMAR.md l.113; SPEC-DECISIONS D3, D11 | Parser.cs `ReadStatement` (Number case), `ReadIdentifierStatement` -> `MathIntoStmt` | dates, functions, c4_interpreter_dotnet (`makes`) |
| `not` value | `not X` | Boolean negation usable in any value position | INVENTORY A3; GRAMMAR.md l.229 | Parser.cs `ReadValue` -> `NotExpr` | conditions, crypto, dates; conformance core/variables_math_control_flow |
| Truthiness | any value in `if`/`while` | false, 0, "", gone and empty list are falsy (D9 says an undefined variable is a runtime error) | SPEC-DECISIONS D9; SEMANTICS.md §2; SPECIFICATION.md §10.1 | Interpreter.cs condition evaluation | conditions, bytes (`if nothing`) |
| Scope | (rule) | Functions read outer variables; locals and parameters do not escape; `if`/loops/`try` blocks create no scope | SPEC-DECISIONS D28; SEMANTICS.md §4 | Scope.cs; Interpreter.cs | functions |
| Command-line arguments | `arguments`; `get arguments into args` | List of the strings after the script path | SPEC-DECISIONS D94 | Parser.System.cs `ReadSystemGet` ("arguments" -> `AssignStmt`) | sys_ssh_arguments; otterPS CliArguments.Tests.ps1 |

## 3. Operators and expressions

| Construct | Syntax | Semantics | Reference | otter-net | Tests |
|---|---|---|---|---|---|
| Addition / join | `a plus b`, `a and b`, `a + b` | Adds numbers or joins text; `and` is addition only outside conditions; boolean operands are a runtime error | GRAMMAR.md §2.4 l.197, l.222; SPEC-DECISIONS D11, D34 | Lexer.cs (`plus`/`+` -> `And`); Parser.cs `ReadMathExpression` -> `MathExpr(Add)` | 32 programs use `plus` (e.g. ask, c4_core); text_ops, err_booland (`and`); data_db (`+`); conformance negative/boolean_operators_outside_conditions |
| Subtraction | `a minus b`, `a - b` | Subtracts | GRAMMAR.md l.198 | `MathExpr(Subtract)` | c4_core, text_ops, functions |
| Multiplication | `a times b`, `a * b` | Multiplies | GRAMMAR.md l.199 | `MathExpr(Multiply)` | functions, data_db (`*`) |
| Division | `a divided by b`, `a / b` | Divides; division by zero is a friendly runtime error | GRAMMAR.md l.200 | Lexer.cs (`divided by` phrase); `MathExpr(Divide)` | math_ops, json, conditions, err_divide, try |
| Percentage | `a percent of b` | `a`% of `b` | GRAMMAR.md l.201; SPEC-DECISIONS D88 | Parser.cs `ReadMathExpression` (Percent + Of) | numbers; otterPS ConditionArithmetic.Tests.ps1 |
| Power | `a power b` | Exponentiation | GRAMMAR.md l.202; SPEC-DECISIONS D88 | `MathExpr(Power)` | numbers, sys_windows |
| Evaluation order | (rule) | No precedence and no parentheses: strictly left to right (`2 plus 3 times 4` is 20) | GRAMMAR.md §2.4 l.187-191; INVENTORY A2 | Parser.cs `ReadMathExpression` (left fold) | math_ops, numbers |
| Property access | `Identifier of PrimaryExpr`, nested `city of address of user` | Reads a property, right-recursive; a missing property is a runtime error | GRAMMAR.md l.253; SEMANTICS.md §5; SPEC-DECISIONS D15, D19, D24 | Parser.cs `ReadValue` -> `PropertyAccessExpr`; Interpreter.cs l.904/1499 | things, err_noprop, err_readprop, json |
| Call expression | `name arg1 [and] arg2` in value position | Calls a declared function with exactly its declared number of arguments; each argument is one value | GRAMMAR.md l.161-171; SEMANTICS.md §6.3; INVENTORY A7 | Parser.cs `ReadFunctionCallExpression` -> `CallExpr` | functions, err_arity, c4_core; conformance core/function_return_expression |

## 4. Conditions

| Construct | Syntax | Semantics | Reference | otter-net | Tests |
|---|---|---|---|---|---|
| Equality | `a is b`, `a is not b` | Equality/inequality (text is case-sensitive) | GRAMMAR.md l.234; SPEC-DECISIONS D2 | Lexer.cs (`is not` phrase); Parser.cs `ReadConditionPrimary` -> `ComparisonExpr` | conditions, err_compare |
| Ordering | `is greater than`, `is less than`, `is at least`, `is at most` | Numeric/text ordering | GRAMMAR.md l.235-236 | Lexer.cs `CombinePhrases`; `ComparisonExpr` | loops, dates, conditions, functions, c4_core |
| Contains | `a contains b` | Substring or list membership | GRAMMAR.md l.237; SPEC-DECISIONS D13, D25 | Lexer.cs (contextual `contains`); `ContainsExpr` | lists, c4_core, common_names; conformance strings/collections_and_text |
| Starts / ends with | `a starts with b`, `a ends with b` | Text prefix/suffix test | GRAMMAR.md l.237; SPEC-DECISIONS D25 | Lexer.cs; `TextMatchExpr` | conditions, c4_processes; conformance strings/collections_and_text |
| Logical | `not C`, `C and C`, `C or C` | `not` > `and` > `or`; short-circuit left to right; `or` only inside conditions (syntax error elsewhere) | GRAMMAR.md l.217-218, l.226-229; SEMANTICS.md §3 | Parser.cs `ReadCondition`, `ReadAndCondition` -> `LogicalExpr`, `NotExpr` | conditions, c4_core, output_before_hang; conformance negative/or_outside_condition |
| Arithmetic in conditions | `if price plus tax is at least 100` | Each side may be a left-to-right calculation; inside a condition only `plus`/`+` add, `and` stays logical | GRAMMAR.md l.204-215, l.232-233; SPEC-DECISIONS D123 | Parser.cs `ReadConditionOperand`, `ConditionArithmeticOperatorAhead` | conditions; otterPS ConditionArithmetic.Tests.ps1 |
| Bare value condition | `if word`, `if not ""` | Truthiness test (D9) | GRAMMAR.md l.231 (`Comparison` without `CompareOp`) | `ReadConditionPrimary` (returns the operand) | bytes (`if nothing`), conditions |
| Continued condition | `if a is 1 and` / indented `b is 2` | A header condition may continue on an indented next line after `and`/`or` | SPEC-DECISIONS D38B | Parser.cs `ReadConditionContinuation`, `ReadConditionBlock` | none found in tests/compat; otterPS Parser.Tests.ps1 |

## 5. Control flow

| Construct | Syntax | Semantics | Reference | otter-net | Tests |
|---|---|---|---|---|---|
| If / otherwise if / otherwise | `if C` block `{ otherwise if C block }` `[ otherwise block ]` | First true branch runs | GRAMMAR.md l.122-124; SPECIFICATION.md §7.1 | Parser.cs `ReadStatement` (If) -> `IfStmt`/`IfBranch` | conditions, syn_otherwise, net_http_requests (otherwise if) |
| While | `while C` block | Pretest loop | GRAMMAR.md l.126; SPECIFICATION.md §7.4 | `WhileStmt`; LoopPasses.cs | loops, deep_nesting, output_before_hang |
| Repeat | `repeat N times` block | Runs the block N times (count is one value, no loop variable) | GRAMMAR.md l.128 | `RepeatStmt` | loops, syn_blocks, err_repeattext |
| Count loop | `count from A to B as name` block | Inclusive counting loop; bounds are expressions | GRAMMAR.md l.130; SPEC-DECISIONS D5 | `CountStmt` | loops, random_seeded, deep_nesting |
| For each | `for each x in collection` block; `each x in collection` block | Iterates a list (bare `each` is the newer spelling) | GRAMMAR.md l.132; SPEC-DECISIONS D36 | Lexer.cs (`for each` phrase, `each`); `ForEachStmt` | loops (both forms), try, err_foreach, 25 programs |
| Stop | `stop` | Ends the nearest function (or handler) with no value; it does not break a loop | GRAMMAR.md l.137; SEMANTICS.md §6.2; SPEC-DECISIONS D37 | Lexer.cs (`stop` -> Return kind); Parser.cs -> `ReturnStmt(null)` | err_stop |
| No `break`/`continue`/`skip` | (none) | Loop-scoped break is deferred; `break`/`skip` parse as calls to undefined functions | SPEC-DECISIONS D37, l.838 | (falls to `ReadIdentifierStatement`) | none |

## 6. Functions

| Construct | Syntax | Semantics | Reference | otter-net | Tests |
|---|---|---|---|---|---|
| Function definition | `to name { [and] param }` block | Declares a function visible from its own body onward (recursion); must be defined before the calling line | GRAMMAR.md l.134; SEMANTICS.md §6.1, §6.4 | Parser.cs `ReadStatement` (To) -> `FunctionDefStmt` | functions, c4_core, deep_nesting, err_depth; conformance core/* |
| Return | `return Expression` | Ends the function with a value; no `return` gives `gone` | GRAMMAR.md l.136; SEMANTICS.md §6.2 | `ReturnStmt` | functions, c4_core, err_depth_math |
| Call statement | `name { arg }` | Calls the function and discards its result | GRAMMAR.md l.160 | Parser.cs `ReadIdentifierStatement` -> `CallStmt` | functions, modules/main |
| Call with capture | `name args make result`; `name args into result` | Calls and stores the result (`make` is the documented legacy form) | GRAMMAR.md l.160; SEMANTICS.md §6.3; INVENTORY A7 | Parser.cs `ReadCallResultTarget` | functions, dates, ui_run_await_make (make); `into` form: none found as a user-function call in tests/compat |
| Arity / argument rule | (rule) | A call takes exactly the declared number of arguments, each one value: `fact n minus 1` is `(fact n) minus 1` | GRAMMAR.md l.168-176 | `ReadFunctionCallExpression` | err_arity, functions |
| Recursion limit | (rule) | Unbounded recursion stops with "Call depth limit exceeded (possible infinite recursion)." | SEMANTICS.md §8.2 | Interpreter.cs | err_depth, err_depth_math |

## 7. Collections and things

| Construct | Syntax | Semantics | Reference | otter-net | Tests |
|---|---|---|---|---|---|
| Empty list | `name are empty` | Creates an empty list | GRAMMAR.md l.139; SPEC-DECISIONS D13 | Parser.cs `ReadIdentifierStatement` (Are) -> `ListDefStmt` | lists, text_ops, c4_core, data_query |
| List literal | `name are` + one indented value per line [+ `.`] | Creates a list (not comma-separated on one line) | GRAMMAR.md l.140; SPEC-DECISIONS D13; INVENTORY A6 | `ReadListItems` -> `ListDefStmt` | 40 programs (e.g. lists, csv, console) |
| Add / remove | `add X to name`; `remove X from name` | Dispatch on the target's runtime type: append/remove item for a list, add/subtract for a number | GRAMMAR.md l.119-120; SPEC-DECISIONS D1, D12 | Parser.cs `ReadAdd`/`ReadRemove` -> `AddToStmt`/`RemoveFromStmt` | lists, loops, c4_core, err_addtext; conformance core/variables_math_control_flow |
| Increase / decrease | `increase x [by N]`; `decrease x [by N]` | Numeric add/subtract (default 1); statements only | SPEC-DECISIONS D35 | Lexer.cs (`increase`->Add, `decrease`->Remove); `ReadAdd`/`ReadRemove` | conditions, t3_system_tasks_printers |
| Thing (inline) | `name has prop value, prop is value, ...` | Untyped object; `is` optional; a bare property means `true` | SEMANTICS.md §5; SPEC-DECISIONS D40, D54 | `ReadIdentifierStatement` (Has) + `ReadInlineObjectProperties` -> `ObjectDefStmt("thing")` | things, dynamic_keys, err_noprop, data_db; conformance objects/things_and_dynamic_keys |
| Thing (block) | `name has` + indented `prop value` lines | Same, one property per line | SPEC-DECISIONS D40; INVENTORY A8 | `ReadObjectBlockProperties` | csv, json, json_edge, xml_words, text_word |
| `is a thing` (legacy) | `name is a thing` + indented properties | Legacy spelling of a thing | GRAMMAR.md l.143; SPEC-DECISIONS D40 | `ReadIdentifierStatement` (A) -> `ObjectDefStmt` | things, dynamic_keys, c4_core |
| Type declaration | `a TypeName has` + one indented field name per line | Declares a type; the one-line `a T has x and y` form is rejected | GRAMMAR.md l.145; SEMANTICS.md §5.4; SPEC-DECISIONS D39/D40 | `ReadStatement` (A) + `ReadTypeFields` -> `TypeDefStmt` | things, err_typeprop, err_settyped, c4_core |
| Typed instance | `x is a TypeName with p v, p v`; `x is a TypeName` | Creates an instance; undeclared fields are `gone`; an indented initializer for a declared type is a syntax error | SEMANTICS.md §5.4; INVENTORY A8 | `ReadIdentifierStatement` -> `ObjectDefStmt(typeName)` | things, err_settyped, sys_processes; conformance objects/*, negative/typed_object_indented_initializer |
| Dynamic get | `get "key" from thing into x` | Reads a property by computed name | SPEC-DECISIONS D41; INVENTORY A9 | Parser.cs `ReadGet` -> `GetKeyStmt` | dynamic_keys, err_getnum, p2_parser_words |
| Dynamic set | `set "key" to value in thing` | Writes a property by computed name | SPEC-DECISIONS D41 | Parser.cs `ReadSet` -> `SetKeyStmt` | dynamic_keys, err_setnumkey, p2_case_json |

## 8. Text and collection operations

| Construct | Syntax | Semantics | Reference | otter-net | Tests |
|---|---|---|---|---|---|
| Of-operations | `length of X`, `uppercase of X`, `lowercase of X`, `first of X`, `last of X` | Operations on text and lists (first/last of an empty list is `gone`) | GRAMMAR.md l.254; SPEC-DECISIONS D24, D25 | Lexer.cs (word before `of`); Parser.cs `ReadOfOperation` -> `OfOperationExpr` | lists, conditions, c4_core, err_lengthnum, err_firstnum, err_upper_fn |
| Sort / reverse | `sort name`; `reverse name` | Sorts (numbers as numbers) / reverses a list in place | SPEC-DECISIONS D25 | `SortStmt`, `ReverseStmt` | text_ops, err_sortnum |
| Replace | `replace X with Y in name [into result]` | Plain-text replace; mutates `name` unless `into` is given | SPEC-DECISIONS D25, D27 | Parser.cs (Replace case) -> `ReplaceStmt` | text_ops, c4_core, err_replaceempty, err_replace_thing |
| Split | `split X by SEP into list` | Splits text into a list | SPEC-DECISIONS D25 | `SplitStmt` | text_ops, err_splitempty |
| Join | `join LIST with SEP into text` | Joins a list into text | SPEC-DECISIONS D25 | `JoinStmt` | text_ops, err_jointext |
| Find | `find item in collection where CONDITION into result` | First match or `gone`; `item` is bound only for the condition | SPEC-DECISIONS D26, D28 | `FindStmt` | text_ops, err_findnum, sys_processes, data_in_conditions |

## 9. Math operations

| Construct | Syntax | Semantics | Reference | otter-net | Tests |
|---|---|---|---|---|---|
| Absolute value / square root | `absolute value of X`, `square root of X` | Abs and sqrt (negative sqrt is an error) | SPEC-DECISIONS D89 | Lexer.cs two-word phrases; `OfOperationExpr` | conditions, math_ops, err_sqrtneg |
| Rounding | `round of X`, `round up of X`, `round down of X` | Round to nearest (`round of 2.5` printed 3 in the sample); round up; round down | SPEC-DECISIONS D89 | `OfOperationExpr` | conditions |
| Larger / smaller | `larger of X and Y`, `smaller of X and Y` | Maximum / minimum of two values | SPEC-DECISIONS D89 | Parser.cs `ReadValue` -> `MinMaxExpr` | math_ops; conformance strings/collections_and_text |
| Trigonometry | `sine of X`, `cosine of X`, `tangent of X` | Degrees | SPEC-DECISIONS D90 | `OfOperationExpr` | math_ops |
| Logarithms | `log of X` (base 10), `natural log of X` | Logarithms | SPEC-DECISIONS D90 | `OfOperationExpr` (LogTen, NaturalLog) | math_ops |
| Pi | `pi` | 3.14159... | SPEC-DECISIONS D90 | Parser.cs `ReadValue` -> `LiteralExpr(Math.PI)` | json, dates |

## 10. Dates and time

| Construct | Syntax | Semantics | Reference | otter-net | Tests |
|---|---|---|---|---|---|
| Clock values | `today`, `now` | Today's date (no time of day) / current date and time | GRAMMAR.md l.247; SPEC-DECISIONS D32.1 | `ClockExpr` | dates, err_between; conformance dates/dates_and_time |
| Date from text | `date from "2024-01-15" [using "MM/dd/yyyy"]` | Parses text into a date | SPEC-DECISIONS D101 | Parser.cs `ReadValue` -> `DateFromTextExpr` | dates, c4_core, err_dateparse, json |
| Date parts | `year of d`, `month of d`, `day of d`, `hour of d`, `minute of d`, `second of d` | Ordinary property reads; hour/minute/second fail on a date with no time ("Try: started is now") | SPEC-DECISIONS D32.2 | `PropertyAccessExpr` | dates, err_datepart, err_datehour |
| Adjust a date | `add N unit to d`; `remove N unit from d` (unit: year(s), month(s), day(s), hour(s), minute(s), second(s)) | Mutates the date variable in place | SPEC-DECISIONS D32.3 | Parser.cs `ReadAdd`/`ReadRemove` -> `DateAdjustStmt` | dates, c4_core, err_adddays |
| Difference statement | `days between a and b make x` (any unit) | Whole units between two dates | GRAMMAR.md l.248-249; SPEC-DECISIONS D32 | Parser.cs `ReadStatement` -> `DateDifferenceStmt` | dates |
| Difference expression | `x is days between a and b`, `say months between a and b` | Same as a value | SPEC-DECISIONS D42 | Parser.cs `ReadValue` -> `DateDifferenceExpr` | dates, c4_core, err_between |
| Format a date | `format d as "pattern" into text` | Formats with a .NET-style pattern (statement, not an expression) | GRAMMAR.md l.154; SPEC-DECISIONS D32 | Parser.cs (Format case) -> `FormatDateStmt` | dates, c4_core |

## 11. Console I/O and diagnostics

| Construct | Syntax | Semantics | Reference | otter-net | Tests |
|---|---|---|---|---|---|
| Say | `say { Expression }`; `say ... in color "NAME"` | Prints parts joined by one space; lists as `a, b`; no parts prints a blank line; no interpolation | GRAMMAR.md l.115; SPEC-DECISIONS D8; D100 (color) | Parser.cs (Say case) -> `SayStmt` | hello and nearly every program; colors, c4_interpreter_dotnet (color) |
| Ask | `ask Expression and call it name`; `ask secretly "PROMPT" and call it name` | Reads a line, converting numbers/booleans automatically; `secretly` hides typing | GRAMMAR.md l.117; SPEC-DECISIONS D6, D100 | Parser.cs (Ask case) -> `AskStmt` | ask (+ ask.ot.input), colors (secretly) |
| Log / warn / error | `log X`, `warn X`, `error X` (several parts like `say`) | Diagnostics on a separate writer with `log: `, `warn: `, `error: ` prefixes | GRAMMAR.md l.156; SPEC-DECISIONS D31 | Lexer.cs (`error` -> Problem); Parser.cs `ReadDiagnostic` -> `DiagnosticStmt` | logging, console, err_progress_then_error; conformance json_random_diagnostics |

## 12. Errors

| Construct | Syntax | Semantics | Reference | otter-net | Tests |
|---|---|---|---|---|---|
| Try / otherwise | `try` block [ `otherwise [into name]` block ] | Runs the fallback on any error; `into name` captures the message; without `otherwise` the error is swallowed | GRAMMAR.md l.147-148, l.165-166; SEMANTICS.md §7; SPEC-DECISIONS D23, D23a, D68 | Parser.cs (Try case) -> `TryStmt` | try, 51 programs use `otherwise into`; conformance errors/try_fail |
| Fail with | `fail with "message"` | Raises a catchable user error | SPEC-DECISIONS D68; SPECIFICATION.md §11.2 | Parser.cs (Fail case) -> `FailStmt` | err_fail, err_failgone, try, ui_run_watch_try; conformance errors/try_fail |

## 13. Modules

| Construct | Syntax | Semantics | Reference | otter-net | Tests |
|---|---|---|---|---|---|
| Use a file | `use "relative/path.ot"` | Splices the file in before lexing, relative to the importing file; each file once; cycles are an error; paths case-sensitive | GRAMMAR.md l.150, l.178-182; OTTER_1_0_MODULE_STATUS.md; SPEC-DECISIONS D122 | src/Otter.Cli/Modules.cs `Modules.Resolve` (regex `^\s*use\s+"..."`); a `use` left in the AST (`Parser.Ui.cs` -> `UseModuleStmt`) is a runtime error | modules/main, modules/syn_module, modules/err_in_module, modules/err_after_use, ui_run_use; otterPS Module.Tests.ps1, UseModuleProduction.Tests.ps1 |


## 14. Files, folders and data formats


Scope: text-file I/O, file conditions, folders and discovery, file/folder objects, current directory, ZIP archives, bytes, XML, JSON, CSV, and other file statements that came up (links/read-only/owner, file watching). Paths are relative to the repo roots:

- otterPS = `otterPS-1.0.1`
- otter-net = `otter-net`

In the tables, `SD` means otterPS `SPEC-DECISIONS.md` (with the line where the D-section header sits), "Inventory" means otterPS `docs/OTTER_1_0_LANGUAGE_AND_RUNTIME_INVENTORY.md`, and `P.cs` means otter-net `src/Otter.Language/Parser.cs`. AST nodes live in `src/Otter.Language/Syntax.cs` unless another file is named. Test names are programs in otter-net `tests/compat/`. otterPS Pester suites are listed as `PS:<file>`, found by grep only.

Placeholders: `P` is a path expression (text, or a file/folder object from `get files`/`get folders`, per D20). `X` is any expression. `N` is a variable name.

### 14.1 Files (text I/O and file conditions)

| Construct | Syntax | Semantics (one line) | Reference (otterPS) | otter-net | Tests |
|---|---|---|---|---|---|
| Read text file | `read P into N` | Reads the whole file as UTF-8 text. A missing file is an error ("I could not find a file called ...") | rules.md "# Files" (l.507); SD D20 (l.587); Inventory B1 (l.289) | `P.cs` ReadRead (l.710) -> `ReadFileStmt`; runtime `OtterFiles.Read` (Files.cs l.147) | files, files_errors, files_fatal, zip, bytes, csv; conformance `file-json-command` (indirectly, via read json); PS:Library.Tests |
| Write text file | `write X to P` | Creates or overwrites the file as UTF-8 without a BOM. Missing parent folders are created. A list value is written as text | rules.md l.507; Inventory B1 | `P.cs` ReadWrite (l.737) -> `WriteFileStmt(Atomic=false)`; Files.cs `Write` l.157 | files, folders, zip, json, csv; conformance `file-json-command` |
| Atomic write | `write X to P atomically` | Writes a temp file `.otter-tmp-*` in the same folder, then `File.Replace` or `File.Move` onto P. A read-only target is an error | SD D72 (l.5547); Inventory B1 | `P.cs` ReadWrite l.779-786 (trailing identifier `atomically`) -> `WriteFileStmt(Atomic=true)` | files; PS:Part3.Tests, PS:Parser.Tests |
| Append | `append X to P` | Appends with no implicit newline. Creates the file and its parent folders if needed | SD D61 (l.4705); Inventory B1 | `P.cs` statement switch, `TokenKind.Append` (l.593) -> `AppendFileStmt`; Files.cs `Append` l.245 | files, files_errors; PS:Library.Tests, PS:StandardLibrary.Tests |
| Copy file | `copy P to P2` | Copies with overwrite. If P2 is an existing folder, the file keeps its name inside it. Parent folders are created | rules.md l.507; SD D20; Inventory B1 | `P.cs` ReadTransfer (l.791) -> `TransferStmt(FileAction.CopyFile)` (otterPS node: `CopyFileStmt`) | files, folders (copies a file object), files_errors |
| Move file | `move P to P2` | Moves, replacing an existing target. Into a folder if P2 is a folder | rules.md l.507; SD D20 (`move file to "Pictures"`); Inventory B1 | `P.cs` ReadTransfer -> `TransferStmt(FileAction.MoveFile)` (otterPS: `MoveFileStmt`) | files, folders, files_errors |
| Delete file | `delete file P` | Deletes a file only; a folder or a missing file is an error. Without the word `file` it is a syntax error ("I expected \"file\" after delete.") | rules.md l.507; Inventory B1 (says `file` is required) | `P.cs` ReadDelete (l.813) -> `DeleteFileStmt` | files, zip, files_errors, sys_links; conformance `file-json-command` |
| File exists (condition) | `file P exists` | True only for an existing *file*. A folder or empty text gives false | GRAMMAR.md `FileExistsExpr` (l.251); SPECIFICATION.md §13 (l.369); Inventory B1 | `Parser.System.cs` TryReadSystemCondition (l.116-125) -> `FileExistsExpr` | files, folders, zip, environment, files_errors, sys_links |
| File is locked (condition) | `file P is locked` | Tries to open the file with exclusive access (`FileShare.None`). False if the file is free or missing | SD D72 (l.5547); Inventory B1 | `Parser.System.cs` l.126-132 -> `FileLockedExpr`; Files.cs `IsLocked` l.323 | files (negative case only); PS:Part3.Tests |
| File is read only (condition)* | `file P is read only` | The read-only attribute of a file | SD D74 (l.5695); Inventory B3 (l.331) | `Parser.System.cs` l.133-138 -> `FileIsReadOnlyExpr` (Syntax.System.cs) | sys_links, data_db; PS:Part3.Tests |
| Set read only / writable* | `set file P to read only` / `set file P to writable` | Sets or clears the read-only attribute (files only) | SD D74; Inventory B3 | `Parser.System.cs` ReadSystemSet (l.248-274) -> `SetFileReadOnlyStmt` | sys_links, sys_syntax_errors |
| File is a symbolic link (condition)* | `file P is a symbolic link` | True if P is a reparse point / symbolic link | SD D73 (l.5625); Inventory B3 | `Parser.System.cs` l.139-142 -> `FileIsSymbolicLinkExpr` | sys_links |
| Create symbolic link* | `create symbolic link P pointing to T` | Creates a link. Whether it is a file or folder link is detected from T, which must exist | SD D73; Inventory B3 | `Parser.System.cs` ReadCreateSymbolicLink (l.358) -> `CreateSymbolicLinkStmt` | sys_links, sys_syntax_errors |
| Symbolic link target* | `get symbolic link target of P into N` | Reads what the link points to | SD D73; Inventory B3 | `Parser.System.cs` ReadSystemGet (l.195-203) -> `GetSymbolicLinkTargetStmt` | sys_links |
| File owner* | `get owner of P into N` | The owner account of a file or folder | SD D74; Inventory B3 | `Parser.System.cs` ReadSystemGet (l.204-211) -> `GetFileOwnerStmt` | sys_windows, sys_syntax_errors; PS:Part3.Tests, PS:PlatformBoundaries.Tests |

\* These D73/D74 rows are file attributes but are parsed in the system area (Parser.System.cs). They may overlap with the system auditor's section.

### 14.2 Folders, discovery, file and folder objects, current directory

| Construct | Syntax | Semantics (one line) | Reference (otterPS) | otter-net | Tests |
|---|---|---|---|---|---|
| Create folder | `create folder P` | Creates the folder and its parents. Does nothing if it already exists (also when a file has that name) | SD D21 (l.630); rules3.md §11 (l.323); Inventory B2 (l.303) | `P.cs` statement switch, `TokenKind.Create` (l.607) -> `CreateFolderStmt` | files, folders, zip, environment, files_errors |
| Delete folder | `delete folder P` | Deletes an empty folder only. A non-empty folder is an error that gives the count ("still has N things in it") | SD D21; rules3.md §14 (l.361); Inventory B2 | `P.cs` ReadDelete (l.813-820) -> `DeleteFolderStmt` | folders, files_errors, p2_parser_words; PS:Part3.Tests |
| Copy folder | `copy folder P to P2` | Copies the whole tree. If P2 exists, the copy goes inside it; missing parents are created | SD D21; rules3.md §12 (l.331); Inventory B2 | `P.cs` ReadTransfer (l.797-805) -> `TransferStmt(FileAction.CopyFolder)` (otterPS: `CopyFolderStmt`) | folders, files_errors; PS:Part3.Tests |
| Move folder | `move folder P to P2` | Moves the folder. Into P2 if P2 exists | SD D21; rules3.md §13 (l.347); Inventory B2 | `P.cs` ReadTransfer -> `TransferStmt(FileAction.MoveFolder)` (otterPS: `MoveFolderStmt`) | folders, files_errors; PS:Parser.Tests |
| List files | `get files in P into N` | A list of file objects directly inside P. Hidden items are skipped | SD D20 (l.587); rules3.md §4 (l.131); Inventory B2 | `P.cs` ReadGet (l.878-897) -> `GetEntriesStmt(Folders=false)` (otterPS: `GetFilesStmt`) | folders, zip, files_errors, p2_parser_syntax |
| List folders | `get folders in P into N` | A list of folder objects directly inside P | SD D20; rules3.md §5 (l.153); Inventory B2 | same -> `GetEntriesStmt(Folders=true)` (otterPS: `GetFoldersStmt`) | folders, zip; PS:Part3.Tests |
| Recursive discovery | `get files in P and subfolders into N` / `get folders in P and subfolders into N` | Recursive listing; each folder's own entries come before its subfolders' entries. `and subfolders` is the only recursion switch | SD D21; rules3.md §6-7 (l.205) | `P.cs` ReadGet l.884-890 (`And` + `TokenKind.Subfolders`); `into` may move to an indented next line (SoftContinuation) | folders, zip; PS:FilesystemAdversarial.Tests, PS:Part3.Tests |
| File object properties | `name of F`, `extension of F`, `path of F`, `size of F`, `created of F`, `modified of F` | name, extension with its dot, full path, size in bytes (number), and created/modified as text `yyyy-MM-dd HH:mm:ss` | SD D20 (lists name/extension/size/path); rules3.md §8 (l.233, adds created/modified); rules.md l.507; Inventory B2 | Runtime `OtterFiles.FileObject` (Files.cs l.99), read through the ordinary `PropertyAccessExpr` | folders, zip |
| Folder object properties | `name of D`, `path of D`, `created of D`, `modified of D` | A folder object has no `size`, on purpose | SD D20; rules3.md §9 (l.276) | `OtterFiles.FolderObject` (Files.cs l.122) | folders, zip |
| Objects as path arguments | e.g. `copy file to "Jpegs"`, `move first to "moved.jpg"` | Every file/folder operation accepts typed text or a file/folder object | SD D20; rules3.md §8; rules.md l.507 example `move file to "Pictures"` | Runtime `OtterFiles.PathOf` (Files.cs l.75) | folders |
| Get current directory | `get current directory into N` / `get current folder into N` | The process working directory; same as `get system folder "current"` | SD D94 (l.6881) | `P.cs` ReadGet (l.866-872) -> `GetSystemFolderStmt(Literal "current")` | environment; PS:Batch1CliArgumentsAndEnvironment.Tests |
| Set current directory | `set current directory to P` / `set current folder to P` | Changes the working directory; relative paths resolve from it afterwards. A missing folder is an error | SD D94 | `P.cs` ReadSet (l.1125-1135) -> `SetCurrentDirectoryStmt` | environment; PS:Batch1CliArgumentsAndEnvironment.Tests |
| System folder** | `get system folder X into N` | A named OS folder (e.g. `"temp"`, `"current"`) | SD D67 (l.5111) | `P.cs` ReadGet (l.873-878) -> `GetSystemFolderStmt` | environment, c4_core |

\*\* Probably the system auditor's area; listed only because `get current directory` reuses the same node.

### 14.3 Archives (ZIP)

| Construct | Syntax | Semantics (one line) | Reference (otterPS) | otter-net | Tests |
|---|---|---|---|---|---|
| Zip a folder | `zip folder P into A` | `ZipFile.CreateFromDirectory`. Refuses to overwrite an existing archive ("... already exists - I will not overwrite an existing archive."). Creates missing parent folders | SD D87 (l.6440); Inventory C10 (l.511) | `P.cs` statement switch `TokenKind.Zip` (l.626-635; `folder` is required: "I expected \"folder\" after \"zip\".") -> `TransferStmt(FileAction.ZipFolder)` (otterPS: `ZipFolderStmt`) | zip; PS:Part3.Tests |
| Unzip | `unzip A into P` | `ZipFile.ExtractToDirectory`. Creates the destination. A missing archive, a non-zip file, or files that already exist are errors | SD D87; Inventory C10 | `P.cs` `TokenKind.Unzip` (l.636-644) -> `TransferStmt(FileAction.Unzip)` (otterPS: `UnzipFileStmt`) | zip; PS:Part3.Tests |

### 14.4 Bytes

| Construct | Syntax | Semantics (one line) | Reference (otterPS) | otter-net | Tests |
|---|---|---|---|---|---|
| Empty bytes | `empty bytes` | A zero-length bytes value (still truthy) | SD D102 (l.7442) | `P.cs` ReadValue (l.1818-1822) -> `BytesExpr(BytesOp.Empty)` | bytes, c4_bytes, crypto_bytes; PS:Bytes.Tests |
| Bytes from text, hex or base64 | `bytes from text X` / `bytes from hex X` / `bytes from base64 X` | UTF-8 encoding of text, or decoding of hex (pairs of hex digits, surrounding spaces trimmed) or base64. Malformed input is an Otter error | SD D102 | `P.cs` ReadValue (l.1792-1816) -> `BytesExpr(FromText/FromHex/FromBase64)`. Any other word: "I expected \"file\", \"text\", \"hex\", or \"base64\" after \"bytes from\"." | bytes, c4_bytes, syn_bytes (bad `octal`), crypto_bytes, net_* |
| Text, hex or base64 from bytes | `text from bytes X` / `hex from bytes X` / `base64 from bytes X` | Decodes bytes as UTF-8 text, or encodes them as hex or base64. Text is never converted to bytes implicitly | SD D102 | `P.cs` ReadValue (l.1823-1828) -> `BytesExpr(ToText/ToHex/ToBase64)` | bytes, c4_bytes, crypto_bytes |
| Length and equality | `length of X`; `if a is b` | Byte count; equality compares content. Bytes never equal text | SD D102 | ordinary `OfOperationExpr` / `ComparisonExpr` on `OtterBytes` | bytes |
| Read binary file | `bytes from file P` | Reads the whole file as bytes with no decoding. A folder or missing file is an error | SD D115 (l.7186) | `P.cs` ReadValue (l.1797-1801) -> `BytesFromFileExpr`; Files.cs `ReadBytes` l.197 | bytes; PS:Bytes.Tests |
| Write binary file | `write bytes X to file P` [`atomically`] | Truncates and writes the file, or writes atomically (temp file + replace). The value must be bytes ("Binary file writes require bytes, but this is ...") | SD D115 | `P.cs` ReadWrite (l.760-776; needs `to` then `file`) -> `WriteBytesFileStmt` | bytes (both forms), syn_bytes (`write bytes x to "f.bin"` rejected); PS:Bytes.Tests |

### 14.5 XML (D105)

| Construct | Syntax | Semantics (one line) | Reference (otterPS) | otter-net | Tests |
|---|---|---|---|---|---|
| Parse text | `xml from text X` | `XmlDocument.LoadXml`. Malformed XML is an Otter error ("This is not valid XML, ...") | SD D105 (l.7485) | `P.cs` ReadValue (l.1830-1840) -> `XmlFromExpr(XmlSourceKind.Text)` | xml, xml_adapter, xml_errors, data_the, c4_core; PS:Xml.Tests |
| Load file | `xml from file P` | Reads the file as text, then parses it | SD D105 | -> `XmlFromExpr(File)` (`file` is required: "I expected \"text\" or \"file\" after \"xml from\".") | xml, xml_errors |
| New document | `xml with root X` | A new document whose root element is named X | SD D105 | `P.cs` l.1842-1843 -> `XmlFromExpr(Root)` | xml, c4_xml |
| Select by tag | `element X in Y` / `elements X in Y` | The first, or all, *direct* child elements named X (a document searches its root). No match gives `gone` or an empty list | SD D105 | `P.cs` ReadValue (l.1876-1897) -> `XmlSelectExpr(Element/Elements)` | xml, xml_adapter, xml_errors, data_the |
| Select by position | `child N in Y` | The N-th child element, counting from 0. Out of range gives `gone` | SD D105 | -> `XmlSelectExpr(Child)` | xml, c4_xml |
| All children | `children N in Y` | **All** child elements. N is required by the grammar but ignored (the same in otterPS, Otter.Interpreter.psm1 l.5822) | SD D105 ("child/children N in X (by position)") | -> `XmlSelectExpr(Children)` | xml (`children 0 in parsed`), xml_adapter |
| Attribute value | `attribute X of E` | The attribute's value, or `gone`. E must be an element, not a document | SD D105 | -> `XmlAttributeExpr` | xml, c4_xml, xml_errors, data_the |
| Child element text | `text of X in Y` | Inner text of the first direct child named X. No such child is an error | SD D105 | `P.cs` ReadValue (l.1863-1870) -> `XmlTextOfNameInExpr` | xml, c4_xml, xml_errors |
| Properties | `name of E`, `root of D`, `text of X`, `attributes of E` | Tag name; root element of a document; inner text; list of attribute names. Uses PowerShell's XML adapter lookup (an attribute or child with that name wins) | SD D105 | `PropertyAccessExpr` -> Interpreter.cs `XmlProperty` (l.1013); PowerShellXml.cs | xml, xml_adapter, xml_words |
| Element exists (condition) | `element X exists in Y` | True if a direct child named X exists | SD D105 | `P.cs` ReadConditionPrimary (l.2026-2038) -> `XmlElementExistsExpr` | xml, c4_xml, xml_adapter, data_the |
| Has attribute (condition) | `E has attribute X` | True if element E has that attribute | SD D105 | `P.cs` ReadConditionPrimary (l.2073-2079) -> `XmlHasAttributeExpr` | xml, c4_xml |
| Serialize | `text from xml X` / `pretty text from xml X` | OuterXml; or indented (2 spaces), with the XML declaration only for a document | SD D105 | `P.cs` ReadValue (l.1845-1861) -> `XmlToTextExpr(Pretty)` | xml, c4_xml |
| Set text | `set text of E to V` / `set text of X in Y to V` | Replaces the inner text of an element, or of the first child named X | SD D105 | `P.cs` ReadSet (l.1083-1100) -> `XmlSetTextStmt` | xml, c4_xml, xml_errors, data_the |
| Set attribute | `set attribute X of E to V` | Sets the attribute (V is formatted as text) | SD D105 | `P.cs` ReadSet (l.1101-1112) -> `XmlSetAttributeStmt` | xml, c4_xml, xml_errors, syn_xml (missing `of` rejected) |
| Remove attribute | `remove attribute X from E` | Removes the attribute | SD D105 | `P.cs` ReadRemove (l.1253-1271) -> `XmlRemoveAttributeStmt` | xml, c4_xml |
| Add element | `add element X [with text T] to Y [and call it N]` | Appends a new child element. Adding to a document adds to its root | SD D105 | `P.cs` ReadAdd (l.1168-1200) -> `XmlAddElementStmt` | xml, c4_xml, xml_errors |
| Remove element | `remove element E` | Detaches E from its parent | SD D105 | `P.cs` ReadRemove (l.1238-1252) -> `XmlRemoveElementStmt` | xml, c4_xml, xml_adapter, xml_errors |
| Save to file | `write xml X to file P` | Writes OuterXml as UTF-8 text (never atomic). Needs `to file` | SD D105 | `P.cs` ReadWrite (l.749-759) -> `XmlWriteFileStmt` | xml, xml_errors, syn_xml (`write xml d to "f.xml"` rejected) |

### 14.6 JSON (D29)

| Construct | Syntax | Semantics (one line) | Reference (otterPS) | otter-net | Tests |
|---|---|---|---|---|---|
| Read JSON file | `read json from P into N` | Reads the file and converts it: object -> thing, array -> list, number -> number (double), `null` -> `gone` | SD D29 (l.934); rules3.md §35 (l.860); Inventory B5 (l.354) | `P.cs` ReadRead (l.725-735) -> `ReadJsonStmt` (Syntax.cs l.693) | json; conformance `file-json-command`; PS:Grammar.Tests, PS:Data.Tests |
| Value to JSON text | `convert X to json into N` | Windows PowerShell 5.1 `ConvertTo-Json` layout (4-space indent, two spaces after a colon). `gone` gives nothing | SD D29; rules3.md §35; Inventory B5; STANDARD_LIBRARY.md "Data" | `P.cs` ReadConvert (l.831-851) -> `ConvertJsonStmt(ToJson=true)` (otterPS: `ConvertToJsonStmt`); runtime Json.cs | json, json_edge, p2_case_json, c4_core, bytes, text_word; conformance `file-json-command` |
| JSON text to value | `convert X from json into N` | Lenient JavaScriptSerializer-style parse. Invalid JSON is an Otter error | SD D29; rules3.md §35-36; Inventory B5 | -> `ConvertJsonStmt(ToJson=false)` (otterPS: `ConvertFromJsonStmt`) | json, json_edge, t3_json_date_overflow, text_word |
| JSON navigation | `name of user`, `city of address of user` | No JSON-specific syntax; ordinary property access | SD D29; rules3.md §36 | ordinary `PropertyAccessExpr` | json, json_edge |

### 14.7 CSV (D95)

| Construct | Syntax | Semantics (one line) | Reference (otterPS) | otter-net | Tests |
|---|---|---|---|---|---|
| Read CSV file | `read csv from P into N` | A list of things. The header row names the properties. Every cell is text (`00123` stays text). Blank, duplicate or missing headers and ragged rows are errors | SD D95 (l.6909) | `P.cs` ReadRead (l.713-723) -> `ReadCsvStmt` | csv; PS:Csv.Tests |
| Write CSV file | `write csv X to P` | X must be a list of things with the same properties. Column order comes from the first thing. Cells are quoted only when needed (RFC 4180). `gone` becomes an empty cell | SD D95 | `P.cs` ReadWrite (l.740-748) -> `WriteCsvStmt` | csv; PS:Csv.Tests |
| Things to CSV text | `convert X to csv into N` | Same rules as `write csv`, giving text | SD D95 | `P.cs` ReadConvert -> `ConvertCsvStmt(ToCsv=true)` (otterPS: `ConvertToCsvStmt`) | csv |
| CSV text to things | `convert X from csv into N` | Same rules as `read csv`, from text | SD D95 | -> `ConvertCsvStmt(ToCsv=false)` (otterPS: `ConvertFromCsvStmt`) | csv |

### 14.8 Other file statements (boundary; probably covered by the network/event auditor)

| Construct | Syntax | Semantics (one line) | Reference (otterPS) | otter-net | Tests |
|---|---|---|---|---|---|
| Watch file or folder | `watch file P and call it N` / `watch folder P [recursively] and call it N` | Starts an OS file watcher; `recursively` is allowed for folders only | SD D104 (l.7469) | `Parser.Network.cs` ReadFileWatch (l.128) -> `FileWatchStmt` (Syntax.Network.cs l.226) | net_watch, net_syn_forms, net_loop_passes, net_check_runs; PS:FileWatching.Tests |
| Watch handlers and values | `on change of N` / `on create in N` / `on delete in N` / `on rename in N`; `changed path`, `changed file name`, `change kind`, `old path`; `N is watching`; `stop watching N` | Event handlers and the values available inside them | SD D104 | `Parser.Network.cs` ReadOnHandler (l.343-347), TryReadNetworkValue (l.896-900), `IsWatchingExpr` (l.963), `StopWatchingStmt` (l.107-118) | net_watch |
| Download (HTTP) | `download file from <url> to <path>` | Out of scope (network); listed only because it writes a file | SD D96 (l.6976) | Parser.Network.cs | (network auditor) |


## 15. System, processes, security and console


Abbreviations: **SD** = otterPS-1.0.1 `SPEC-DECISIONS.md` (line of the D-heading); **INV** = otterPS `docs/OTTER_1_0_LANGUAGE_AND_RUNTIME_INVENTORY.md`; **SL** = otterPS `docs/STANDARD_LIBRARY.md`; **GR** = otterPS `docs/GRAMMAR.md`; **D100doc** = otterPS `docs/D100-CONSOLE-UX-PRIMITIVES-DESIGN.md`; **PS** = otter-net `src/Otter.Language/Parser.System.cs`; **P** = otter-net `src/Otter.Language/Parser.cs`. AST node classes live in `Syntax.System.cs` (system nodes) or `Syntax.cs` (the rest). Tests are otter-net `tests/compat/*.ot` unless marked "conf:" (otterPS `conformance/`) or "Pester:" (otterPS `tests/*.Tests.ps1`, found by grep). `sys_syntax_errors.ot` holds the negative (syntax error) cases for the system statements.

Every **Syntax** cell below is copied from a spec or decision doc, a parser comment or error suggestion, or a test program. Words in CAPITALS are placeholders.

### 15.1 Environment & system info

| Construct | Syntax | Semantics (one line) | Reference | otter-net | Tests |
|---|---|---|---|---|---|
| Get environment variable | `get environment variable "PATH" into value` | Reads a process environment variable. An unset one gives `gone`. | SD D67 (l.5111, syntax block l.5148); INV C2; SL l.37 | P `ReadGet` -> `GetEnvironmentVariableStmt` | environment.ot, environment_names.ot (plus most sys_*.ot); conf: system_integration/clipboard_env_notify.ot (not in manifest.json); Pester: Batch1CliArgumentsAndEnvironment, StandardLibrary |
| Set environment variable | `set environment variable "NAME" to "VALUE"` | Sets it for this process and its children. Setting it to `gone` removes it. A name with `=` or an empty name is an error. | SD D94 (l.6881) | P `ReadSet` -> `SetEnvironmentVariableStmt` | environment.ot, err_setenv_equals.ot, err_setenv_empty.ot; Pester: Batch1CliArgumentsAndEnvironment |
| System folder | `get system folder "temp" into path` (also `"appdata"`, `"user"`, `"current"`) | Gives a well-known folder path. The name is a text value, matched case-insensitively. Any other name is an error. | SD D67 l.5149-5159; INV C2 | P `ReadGet` -> `GetSystemFolderStmt`; names in `Interpreter.cs` ~l.570 | environment.ot (incl. `"desktop"` error), c4_core.ot; conf: clipboard_env_notify.ot; Pester: Part3 |
| Current directory (get) | `get current directory into folder` (also `get current folder into ...`) | Alias for system folder `"current"`. | SD D94 | P `ReadGet` -> `GetSystemFolderStmt("current")` | environment.ot |
| Current directory (set) | `set current directory to "Projects"` | Changes the working folder. Relative paths then follow it. A missing folder is an error. | SD D94 | P `ReadSet` -> `SetCurrentDirectoryStmt` | environment.ot |
| CLI arguments | `get arguments into args` (also the value `arguments`) | Gives a list of the command-line arguments. | SD D94 | PS `ReadSystemGet` "arguments" -> `AssignStmt` from `VariableExpr("arguments")` | sys_ssh_arguments.ot; Pester: CliArguments, Batch1CliArgumentsAndEnvironment |
| System information | `get system information "os" into info`. Kinds: `"os"`/`"cpu"`/`"memory"`/`"disk"`/`"network"`/`"user"`/`"groups"`/`"software"`/`"tasks"`/`"printers"`/`"services"` | Gives a thing or list that describes the machine. The kind is a text value, case-insensitive. An unknown kind is an error. | SD D69 l.5307, D76 l.5805, D77 l.5842, D80 l.6004, D83 l.6181, D86 l.6405; INV C3 | PS `ReadSystemGet` "system" -> `GetSystemInfoStmt`; runtime `SystemInfo.cs` `OtterSystemInfo.Get` | sys_system_info.ot, sys_desktop.ot, t3_system_tasks_printers.ot; Pester: Part3, StandardLibrary |

### 15.2 Processes & commands

| Construct | Syntax | Semantics (one line) | Reference | otter-net | Tests |
|---|---|---|---|---|---|
| Run program (non-blocking) | `run "notepad.exe"` / `run sleeper into p` | Starts a process and does not wait for it. `into` gives a **process handle** (`id`, `name`, `memoryBytes`, `cpuSeconds`, `startTime`), not a command result. | GR l.152 `RunStmt = "run" , [ "command" ] , Expression , [ "into" , Identifier ]`; SD D70 l.5381, D75 l.5760; INV B4; rules.md "Running Programs" | PS `ReadSystemRun` -> `RunProgramStmt`; handle in `Processes.cs` | sys_processes.ot, c4_processes.ot; Pester: Part3, ProcessAdversarial |
| Run command (blocking) | `run command "git status"` / `run command "git status" into result` | Runs it to completion with no shell in between and captures the result. `.ps1`/`.cmd`/`.bat` scripts are dispatched to their interpreter (D119-R1). | GR l.152; rules.md "Running Programs" (l.~574-580); SD D65 l.5044, D119 l.7639; INV B4 | PS `ReadSystemRun` -> `RunCommandStmt` (Syntax.cs); `Commands.cs` | commands.ot, sys_syntax_errors.ot; conf: release/file_json_command.ot (manifest "file-json-command"), misc/file_exists_run_command_json_roundtrip.ot (not in manifest); Pester: CommandDispatch, ProcessAdversarial, Conformance, Part3 |
| Command result properties | `output of result` / `error output of result` / `exit code of result` | Stdout text, stderr text, and the numeric exit code of a `run command ... into` result. | INV B4; SD D65 | Ordinary property access; properties written in `Commands.cs` l.45-47 | commands.ot; conf: file_json_command.ot |
| List processes | `get processes into list` | Gives a list of process handles. | SD D70; INV C3 | PS `ReadSystemGet` "processes" -> `GetProcessesStmt` | sys_processes.ot; Pester: StandardLibrary, Part3 |
| Kill process | `kill process p` / `kill process p and its children` | Ends the process, or its whole tree. A process that is already gone is not an error. A non-handle is refused. | SD D70; INV C3 | PS `ReadKill` -> `KillProcessStmt(IncludeChildren)` | sys_processes.ot, c4_processes.ot, sys_syntax_errors.ot (`kill foo`, `... and children`); Pester: Part3 |
| Process priority | `set priority of process p to "high"` | Levels (`Processes.cs` `TryPriorityClass`): `"low"`, `"below normal"`, `"normal"`, `"above normal"`, `"high"`, `"realtime"`. Anything else is an error. | SD D71 l.5480; INV C3 | PS `ReadSystemSet` "priority" -> `SetProcessPriorityStmt` | sys_processes.ot, c4_processes.ot; Pester: none found by grep |
| Wait for process | `wait for process p up to 5 seconds [into finished]` | Waits up to N **seconds** (other units are a syntax error). `into` gives true/false for whether it finished. | SD D71; INV C3 | PS `ReadWaitForProcess` -> `WaitForProcessStmt` | sys_processes.ot, c4_processes.ot, sys_syntax_errors.ot (`up to 5 minutes`, `soon`); Pester: Part3 |
| Remote command (WinRM) | `run command "..." on remote "host" using credential "n" [into result]` | Runs over PowerShell remoting with a stored D81 credential. | SD D84 l.6262; INV C9 | PS `ReadSystemRun` (`on remote`) -> `RunRemoteCommandStmt`; `RemoteCommands.cs`/`Psrp.cs` | p2_remote_command.ot, sys_syntax_errors.ot (`with credential`); Pester: Part3 |
| SSH command | `run command "..." over ssh to "user@host" [into result]` | Runs through the ssh client with key-based auth only. Hosts that ssh could misread are refused. | SD D85 l.6330; INV C9 | PS `ReadSystemRun` (`over ssh`) -> `RunSshCommandStmt` | sys_ssh_arguments.ot, sys_syntax_errors.ot (`over sh`); Pester: HostPortability |

### 15.3 Windows admin & desktop integration

| Construct | Syntax | Semantics (one line) | Reference | otter-net | Tests |
|---|---|---|---|---|---|
| Copy to clipboard | `copy "text" to clipboard` | Puts the formatted text of the value on the clipboard. | SD D67; INV C2; SL l.50 | PS `TryReadCopyToClipboard` -> `CopyToClipboardStmt` | sys_clipboard.ot, sys_syntax_errors.ot; conf: clipboard_env_notify.ot; Pester: PlatformBoundaries, StandardLibrary |
| Read clipboard | `get clipboard into text` | Gives the clipboard text, or `gone`. | SD D67; INV C2 | PS `ReadSystemGet` "clipboard" -> `GetClipboardStmt` | sys_clipboard.ot |
| Notification | `notify "Title" with "Message"` | Shows an OS toast. Windows-only in otter-net (`NotOnWindows("Notifications (notify)")`). | SD D67; INV C2 | `TryReadSystemStatement` (Notify) -> `NotifyStmt` | sys_print_notify.ot, sys_desktop.ot, sys_syntax_errors.ot; conf: clipboard_env_notify.ot; Pester: none found |
| File/folder dialogs | `choose file into path` / `choose folder into path` / `choose file to save into path` | Opens a blocking OS picker and gives the chosen path. Windows-only. | SD D67; INV C2 ("PARSES BUT RUNTIME UNVERIFIED") | PS `ReadChooseDialog` -> `ChooseFileStmt(FileDialogKind)` | sys_desktop.ot, sys_print_notify.ot (off-Windows error path only), sys_syntax_errors.ot; Pester: PlatformBoundaries |
| Registry read | `get registry value "n" from "path" into t` | Reads a value from `HKCU:\...`/`HKLM:\...`. A missing value gives `gone`. | SD D78 l.5890; INV C4 | PS `ReadSystemGet` "registry" -> `GetRegistryValueStmt` | sys_windows.ot, sys_syntax_errors.ot; Pester: Part3, PlatformBoundaries |
| Registry write | `set registry value "n" to "d" in "path"` | Creates or sets the value. | SD D78; INV C4 | PS `ReadSystemSet` "registry" -> `SetRegistryValueStmt` | sys_windows.ot, sys_syntax_errors.ot |
| Registry delete | `delete registry value "n" from "path"` | Removes the value. Deleting it twice is not an error. | SD D78; INV C4 | PS `ReadSystemDelete` -> `DeleteRegistryValueStmt` | sys_windows.ot, sys_syntax_errors.ot (`in "k"`) |
| Registry key exists | `if registry key "HKCU:\Software\MyApp" exists` | A condition. | SD D78; INV C4 | PS `TryReadSystemCondition` -> `RegistryKeyExistsExpr` | sys_windows.ot, sys_syntax_errors.ot (`if registry "k" exists`) |
| Event log | `get event log entries from "System" up to 20 into entries` | Gives the newest N entries (`source`, `level`, `time`, `message`). | SD D79 l.5960; INV C5 | PS `ReadSystemGet` "event" -> `GetEventLogEntriesStmt`; `EventLog.cs` | sys_desktop.ot, sys_syntax_errors.ot; Pester: Part3, PlatformBoundaries |
| Credentials (DPAPI) | `set credential "n" to "secret"` / `get credential "n" into secret` / `delete credential "n"` | Encrypted per-user local store. A missing credential gives `gone`. Deleting one twice is fine. | SD D81 l.6052; INV C6; SL l.51 | PS `ReadSystemSet`/`ReadSystemGet`/`ReadSystemDelete` -> `SetCredentialStmt`/`GetCredentialStmt`/`DeleteCredentialStmt` | sys_windows.ot, p2_remote_command.ot, sys_syntax_errors.ot (`as "s"`, missing `into`); Pester: PlatformBoundaries, Part3 |
| Secret vault: store | `store secret "api-token" with value token` | Puts text or bytes in the OS credential store, scoped to this program. Storing again replaces it. | SD D111 l.7577 | PS `ReadSecretStatement` -> `StoreSecretStmt` | sys_windows.ot, sys_syntax_errors.ot (`value "y"`, `with "y"`); Pester: Vault, Parser, PlatformBoundaries |
| Secret vault: delete | `delete secret "NAME"` | Deleting a missing secret is an error. | SD D111 | PS `ReadSecretStatement` -> `DeleteSecretStmt` | sys_windows.ot; Pester: Vault |
| Secret read / exists | `secret "NAME"` (expression), `secret "NAME" exists` (condition) | Reads the secret (missing is an error) or tests for it. `secret` is an ordinary name unless a text follows, or a name at end of line or before `exists`. | SD D111 | PS `TryReadSystemValue` -> `SecretReadExpr` / `SecretExistsExpr` | sys_windows.ot, syn_crypto.ot, sys_syntax_errors.ot; Pester: Vault |
| Power actions | `lock the computer` / `sign out` / `restart the computer` / `shut down the computer` | Session or power actions, Windows-only. `the` is optional (`restart computer` parses). | SD D82 l.6105; INV C7; SL l.51 | PS `ReadPowerAction` (+`ReadOptionalTheBeforeName`) -> `PowerActionStmt(PowerAction)` | sys_windows.ot (only off Windows: `lock the computer`, `sign out`, `restart computer`, `shut down the computer`), sys_syntax_errors.ot; Pester: PlatformBoundaries, Part3 (lock) |
| Print | `print "file.txt" to "PrinterName"` | Sends a text file to a named printer. Windows-only. | SD D83 l.6181; INV C8 | `TryReadSystemStatement` (Print) -> `PrintFileStmt` | sys_print_notify.ot, sys_desktop.ot (missing printer/file only), sys_syntax_errors.ot (`on "P"`); Pester: none found |
| Symbolic link create | `create symbolic link "l" pointing to "t"` | Makes a file or folder link, creating parent folders. Making it again replaces it. A missing target is an error. | SD D73 l.5625; INV B3 | PS `ReadCreateSymbolicLink` -> `CreateSymbolicLinkStmt` | sys_links.ot, sys_syntax_errors.ot (`at "b"`); Pester: Part3, ProjectBuild |
| Is symbolic link | `if file "l" is a symbolic link` | A condition. A missing file gives false. | SD D73; INV B3 | PS `TryReadSystemCondition` -> `FileIsSymbolicLinkExpr` | sys_links.ot, sys_syntax_errors.ot (`is a shortcut`) |
| Link target | `get symbolic link target of "l" into t` | Gives the target path. A non-link is an error. | SD D73; INV B3 | PS `ReadSystemGet` "symbolic" -> `GetSymbolicLinkTargetStmt` | sys_links.ot, sys_syntax_errors.ot |
| File owner | `get owner of "x" into owner` | Gives the owner account name. Windows-only in otter-net. | SD D74 l.5695; INV B3 | PS `ReadSystemGet` "owner" -> `GetFileOwnerStmt` | sys_windows.ot, sys_syntax_errors.ot (`get owner "x"`); Pester: PlatformBoundaries, Part3 |
| Read-only test | `if file "x" is read only` | A condition. A folder or a missing file gives false. | SD D74; INV B3 | PS `TryReadSystemCondition` -> `FileIsReadOnlyExpr` | sys_links.ot, sys_syntax_errors.ot (`is writable`, `is read`) |
| Set read-only/writable | `set file "x" to read only` / `set file "x" to writable` | Sets or clears the read-only attribute. A missing file is an error. | SD D74; INV B3 | PS `ReadSystemSet` (File) -> `SetFileReadOnlyStmt(ReadOnly)` | sys_links.ot, sys_syntax_errors.ot (`to readable`, `to read`); Pester: Part3 |

### 15.4 Console UX (D100)

| Construct | Syntax | Semantics (one line) | Reference | otter-net | Tests |
|---|---|---|---|---|---|
| Colored output | `say "Error!" in color "red"` | Colors: red, green, yellow, blue, cyan, magenta, white, gray, darkgray, darkred, darkgreen, darkyellow, darkblue, darkcyan, darkmagenta, black. Case-insensitive and trimmed. An unknown color or a non-text is an error. No color is applied when output is redirected. Web target refuses it. | D100doc §1; SD D100 l.7411 | P statement switch `Say` -> `SayStmt(parts, color)`; list in `Interpreter.cs` l.963 | colors.ot, syn_colors.ot (`in colour`, bare `in`), c4_interpreter_dotnet.ot; tests/cli ui.psd1 (web build refuses it); Pester: ConsoleUxPrimitives |
| Cursor position | `set cursor to row 5 column 10` | 1-based. Row or column below 1 is an error. | D100doc §2; SD D100 | P `ReadSet` -> `SetCursorStmt` (D100doc names it `SetCursorPositionStmt`) | console.ot, syn_console.ot; Pester: ConsoleUxPrimitives |
| Menu | `choose from options into choice` | Prints a numbered list, re-prompts until a valid number is given, and gives the item itself. An empty list is an error. | D100doc §3; SD D100 | P statement switch (`Choose` + `From`) -> `ChooseFromListStmt` | console.ot (+ console.ot.input), syn_console.ot, c4_core.ot; Pester: ConsoleUxPrimitives |
| Progress bar | `show progress 50 percent` | Redraws a 20-character bar in place with `\r`. Values outside 0-100 are an error. | D100doc §4; SD D100 | P statement switch (`Show` + `progress`) -> `ShowProgressStmt` | console.ot, err_progress_then_error.ot, syn_console.ot, c4_core.ot; Pester: ConsoleUxPrimitives |
| Secret input | `ask secretly "Password:" and call it pw` | Masks typed characters with `*`. Behaves like `ask` when input is redirected. | D100doc §5; SD D100 | P statement switch `Ask` -> `AskStmt(Secret)` | colors.ot (+ colors.ot.input); Pester: ConsoleUxPrimitives |
| TTY detection | `if console is interactive` | True only if neither stdin nor stdout is redirected. `console` stays an ordinary name elsewhere. | D100doc §6; SD D100 | Lexer.cs l.333 makes one token `ConsoleInteractive`; P primary -> `ConsoleInteractiveExpr` | console.ot; Pester: ConsoleUxPrimitives |

### 15.5 Timing

| Construct | Syntax | Semantics (one line) | Reference | otter-net | Tests |
|---|---|---|---|---|---|
| Wait | `wait 5 seconds`. Units, singular or plural: `milliseconds`, `seconds`, `minutes`, `hours`, `days`, `months`, `years` | Pauses while still servicing event sources (D121). A negative or non-number duration is an error. A unit is required. Not supported on the web target. | SD D101 l.7425, D121 l.7677; SL l.49 | P `ReadWait` -> `WaitDelayStmt(duration, TimeUnit)` | timers.ot, syn_wait.ot (`wait 5`, `wait 5 parsecs`), c4_core.ot and others; Pester: TierOneRuntime |
| Start timer | `start timer workTimer` | Creates a monotonic stopwatch value. Arithmetic on it is an error. | SD D101 | P statement switch (`Start` + `timer`) -> `StartTimerStmt` | timers.ot, syn_wait.ot (`start timer` with no name); Pester: TierOneRuntime |
| Elapsed time | `elapsed time of workTimer` (seconds) / `elapsed milliseconds of workTimer` | Expressions. A non-timer is an error. | SD D101 | Lexer.cs l.391-392 (`ElapsedTime`/`ElapsedMilliseconds` tokens); P `ReadOfOperation` -> `OfOperationExpr(OfOperation.ElapsedTime/ElapsedMilliseconds)` | timers.ot; Pester: TierOneRuntime |

### 15.6 Random

| Construct | Syntax | Semantics (one line) | Reference | otter-net | Tests |
|---|---|---|---|---|---|
| Random number | `random number from 1 to 10 into n` | Inclusive. Bounds are math expressions and swapped bounds are allowed. Non-number bounds are an error. | SD D30 l.961; INV B6 | P `ReadRandom` -> `RandomNumberStmt` | random_seeded.ot, syn_random.ot, c4_core.ot, c4_compiled_engine.ot; conf: json_random_diagnostics.ot (not in manifest) |
| Random item | `random item from games into g` | Picks an element. An empty list gives `gone`. A non-list is an error. | SD D30; INV B6 | P `ReadRandom` -> `RandomItemStmt` | random_seeded.ot, syn_random.ot (`random thing`) |
| Seed | `set random seed to 42` | Makes `random number`/`random item` reproducible. Does not affect secure random. Not supported on web. | SD D101 | P `ReadSet` -> `SetRandomSeedStmt` | random_seeded.ot, syn_random.ot (`set random sauce`), c4_compiled_engine.ot; Pester: TierOneRuntime, CompiledRun |

### 15.7 Crypto

| Construct | Syntax | Semantics (one line) | Reference | otter-net | Tests |
|---|---|---|---|---|---|
| Hash text | `hash "text" as "sha256" into digest` | Algorithms: md5, sha1, sha256, sha384, sha512. Spellings such as `"SHA1"`, `"sha-256"` and `"sha 384"` are accepted. Output is **lowercase hex**. | SD D91 l.6692 (lowercase hex l.6706); INV C12 | P `ReadHash` -> `HashTextStmt(key=null)`; `Crypto.cs` | crypto.ot, syn_crypto.ot (no `as`), c4_core.ot; Pester: Crypto |
| HMAC text | `hash "text" as "sha256" with key "secret" into digest` | HMAC with the same algorithm set. | SD D91; INV C12 | P `ReadHash` -> `HashTextStmt(key)` | crypto.ot, syn_crypto.ot (`with secret`) |
| Encrypt/decrypt text | `encrypt "text" with key "secret" into cipher` / `decrypt "cipher" with key "secret" into text` | Password-derived encryption with base64 output `base64(salt‖IV‖ciphertext‖tag)`. Every encryption differs. A wrong key or bad data is an error. Interoperable with otterPS. | SD D92 l.6787 (format l.6805); INV C12 | P `ReadCipher` -> `CipherTextStmt(decrypt)` | crypto.ot, syn_crypto.ot |
| Digest of bytes | `sha256 of DATA` / `sha384 of DATA` / `sha512 of DATA` | Expression, bytes in and bytes out. A text input is an error. The words remain ordinary names otherwise. | SD D109 l.7544 | P primary (crypto block ~l.1765) -> `CryptoHashExpr` | crypto_bytes.ot, syn_crypto_bytes.ot; Pester: Crypto |
| HMAC of bytes | `hmac sha256 of DATA using KEY` (also sha384/sha512) | Expression over bytes. An empty key is an error. | SD D109 | P primary -> `CryptoHmacExpr` | crypto_bytes.ot, syn_crypto_bytes.ot (`with key`) |
| Secure random | `secure random bytes N` | Expression giving N cryptographically random bytes. N of 0 or a non-integer N is an error. | SD D109 | P primary -> `SecureRandomBytesExpr` | crypto_bytes.ot; tests/cli run.psd1 l.38 |
| Generate key | `generate encryption key and call it key` | Makes a 32-byte key. | SD D109 | P statement prelude (~l.336) -> `GenerateKeyStmt` | crypto_bytes.ot, syn_crypto_bytes.ot (`into key`) |
| Authenticated encrypt/decrypt (bytes) | `encrypt DATA using KEY and call it R` / `decrypt DATA using KEY and call it R` | AES-256-CBC + HMAC-SHA256 encrypt-then-MAC over bytes. A wrong key size or tampered data is an error. | SD D109 | P `ReadCipher` (`using`) -> `CryptoCipherStmt` | crypto_bytes.ot, syn_crypto_bytes.ot (`into y`) |
| Password hash | `hash password P and call it H` | PBKDF2-SHA256 with 600,000 iterations, giving the text `otter-pbkdf2-sha256$600000$salt$hash`. An empty password is an error. | SD D109 | P `ReadHash` (`password`) -> `HashPasswordStmt` | crypto_bytes.ot, c4_core.ot |
| Password check | `password P matches hash H` | A condition. A malformed hash is an error. | SD D109 | P condition (~l.2040) -> `PasswordMatchesExpr` | crypto_bytes.ot, syn_crypto_bytes.ot (missing `hash`) |
| Constant-time equals | `A securely equals B` | A condition, bytes only. Text operands are an error. | SD D109 | P condition (~l.2064) -> `SecurelyEqualsExpr` | crypto_bytes.ot |

**Total: 60 constructs** (7 environment and system info, 9 processes and commands, 21 Windows admin and desktop, 6 console UX, 3 timing, 3 random, 11 crypto). Grouped forms such as the four power actions count as one row.


## 16. Network, events and concurrency


Abbreviations used in the tables:

- **SD**: otterPS-1.0.1 `SPEC-DECISIONS.md`. Line numbers are where each decision's heading starts: D49 l.2764, D51 l.2883, D56 l.3645/3709, D96 l.6976, D101 l.7425, D104 l.7469, D106 l.7501, D107 l.7517, D108 l.7532, D112 l.7597, D113 l.7616, D116 l.7229, D119 l.7639, D116-affirmation l.7667, D121 l.7677, D130 l.7890, D133 l.7934.
- **EM**: otterPS `docs/OTTER_1_0_EVENT_MODEL.md`.
- **RW**: otterPS `docs/OTTER_1_0_RESERVED_WORDS.md`, D-2 state words, l.182-194.
- **r2**: otterPS `rules2.md`.
- **PN**: otter-net `src/Otter.Language/Parser.Network.cs`. The line given is the method's own line.
- **SN**: otter-net `src/Otter.Language/Syntax.Network.cs`. It holds the AST node classes.
- **IN**: otter-net `src/Otter.Runtime/Interpreter.Network.cs`.
- **c/**: otter-net `tests/compat/`.
- **n/**: otter-net `tests/network/`.
- **PS tests**: the otterPS `tests/*.Tests.ps1` files. They were searched only by grep.

In the syntax column, `[...]` means optional, `a|b` means a choice, and UPPER words are expressions or names.

### 16.1 HTTP client

| Construct | Syntax | Semantics (one line) | Reference | otter-net | Tests |
|---|---|---|---|---|---|
| GET text | `get URL into X` | Synchronous GET. The body text goes into X. | SD D49, D116A §1; PARITY doc (superseded) | PN `TryReadHttpGet` l.682 / `ReadHttpGetRest` → `HttpGetStmt(AsJson=false)` | n/http_sync.ot, c/net_http_errors.ot; conformance `http-invalid-url-negative`, `http-web-fetch` |
| GET JSON (suffix) | `get URL as json into X` | GET, then the body is decoded from JSON into an Otter value. | SD D116A §1 | PN `ReadHttpGetRest` → `HttpGetStmt(AsJson=true)` | n/http_sync.ot, c/net_http_errors.ot |
| GET JSON (prefix) | `get json from URL into X` | Same as the suffix form. | SD D49, D116A §1 | PN `TryReadHttpGet` (Json branch) → `HttpGetStmt(AsJson=true)` | n/http_sync.ot, c/net_http_errors.ot |
| POST | `post DATA [as json] to URL [into X]` | Synchronous POST. Without `as json` the body is text or bytes; with it the data is serialised to JSON. | SD D49, D116A §1 | PN `ReadPost` l.618 → `HttpSendStmt("POST")` | n/http_sync.ot, c/net_http_errors.ot (`as json`). The form without `into` was found only in PS Parser.Tests.ps1. |
| PUT | `put DATA [as json] to URL [into X]` | Synchronous PUT. Without `to`, the line falls back to the UI form `put X in Y` (D47). | SD D49 §3, D116A §1 | PN `TryReadHttpPut` l.638 → `HttpSendStmt("PUT")` | c/net_http_errors.ot |
| DELETE | `delete from URL [into X]` | Synchronous DELETE. `from` tells it apart from the file and folder delete forms. | SD D49 §4, D116A §1 | PN `ReadHttpDelete` l.662 → `HttpDeleteStmt` (switch: `Delete when next==From`) | n/http_sync.ot, c/net_http_errors.ot |
| Clause continuation | `get URL` then, on an indented line, `into X` | The `into` clause may sit on an indented continuation line. | SD D38A (soft continuation) | PN `SoftContinuation` l.516 → `ReadHttpEnd` | none found |
| Options block | an indented block after a get/post/put/delete line or after `start ... and call it R`, with one option per line | Per-request options. A bad option line is recorded as an error and parsing goes on (D117). | SD D116A §2, D116B §1, D117 §3 | PN `ReadHttpOptions` l.525 → `HttpOptions` | n/http_sync.ot, c/net_http_errors.ot, c/net_http_requests.ot, c/net_syn_forms.ot (errors) |
| ↳ header | `with header NAME is VALUE` | Adds a request header. | SD D116A §2 | `HttpHeaderClause` | n/http_sync.ot, c/net_http_errors.ot, c/net_http_requests.ot |
| ↳ cookies | `with cookies` / `without cookies` | Turns the cookie jar on or off. | SD D116A §2, D116B §1 | `HttpOptions.WithCookies` | n/http_sync.ot, c/net_http_errors.ot, c/net_http_requests.ot |
| ↳ redirects | `following redirects` / `without redirects` | Follows redirects, or does not. | SD D116A §2, D116B §1 | `HttpOptions.FollowRedirects` | n/http_sync.ot, c/net_http_errors.ot |
| ↳ timeout | `with timeout N second|seconds` | Sets the request timeout. The unit must be second(s); any other word raises `ExpectedSeconds`. | SD D116A §2, D116B §3 | `HttpOptions.TimeoutSeconds` | c/net_http_errors.ot, c/net_http_requests.ot, n/http_sync.ot |
| Download | `download file from URL to PATH` | Streams the body to PATH through a temp file in the same folder. It never overwrites an existing file, the parent folder must exist, and the response must be 2xx. | SD D96 | PN `ReadDownload` l.824 → `DownloadFileStmt` | n/http_sync.ot, c/net_http_errors.ot |
| Async GET handle | `start get from URL [as json] and call it R` | Starts the request in the background. R begins in the `pending` state. | SD D116B §1 (written there as `start get <url>`, see Gaps) | PN `ReadStart` l.467 → `ReadHttpStart` l.771 → `HttpStartStmt("GET")` | c/net_http_async.ot, c/net_syn_start.ot, c/net_http_requests.ot; PS Http.Tests.ps1 |
| Async POST/PUT | `start post|put DATA [as json] to URL and call it R` | Background POST or PUT. | SD D116B §1 | `HttpStartStmt("POST"/"PUT")` | c/net_http_async.ot (post), c/net_http_requests.ot (put); PS Http.Tests.ps1 |
| Async DELETE | `start delete from URL and call it R` | Background DELETE. | SD D116B §1 | `HttpStartStmt("DELETE")` | c/net_http_errors.ot; PS Http.Tests.ps1 |
| Cancel | `cancel R` | Moves a pending request to `cancelled` and fires `on cancel`. Cancelling again does nothing. Any other value gives "cancel requires an HTTP request." Also cancels command jobs. | SD D116B §1, D119-R2 | PN `TryReadNetworkStatement` (cancel) → `HttpCancelStmt`; IN l.66 | c/net_http_requests.ot, c/net_http_errors.ot, c/net_c4_errors.ot, c/net_jobs.ot |
| Request state check | `R is [not] pending|running|completed|failed|cancelled` | Tests the state. `running` is a synonym for `pending`, and `is not` wraps the check in NotExpr. Works on requests and on jobs. | SD D116B §4; RW l.182-186 | PN `ReadNetworkStatePredicate` l.954 → `HttpRequestIsStateExpr` (+`NotExpr`) | c/net_http_async.ot, c/net_http_requests.ot, c/net_c4_errors.ot, c/net_syn_reserved.ot |
| Request properties | `state of R`, `response of R`, `error of R`, `status of R` | `state` is pending, completed, failed or cancelled. `response` raises an error unless the request completed. `error` is gone unless it failed. `status` is the numeric status code. | SD D116B §4 | generic `PropertyAccessExpr`; IN `TryNetworkProperty` l.~318 | c/net_http_async.ot, c/net_http_requests.ot |
| Received response | `received response` | Ambient value inside `on complete of R`. It is the body, already parsed when the request used `as json`. | SD D116B §2 | PN `TryReadNetworkValue` l.891 → `ReceivedResponseExpr` | c/net_http_async.ot, c/net_syn_start.ot, c/net_http_requests.ot |

### 16.2 Web server (`otter serve` only)

| Construct | Syntax | Semantics (one line) | Reference | otter-net | Tests |
|---|---|---|---|---|---|
| Server declaration | `NAME is a web server` with an indented block holding `port is N` and `host is "H"` | Declares the server. The defaults are port 8080 and host localhost. `otter serve` reads only literal values. | SD D51 example; r2 §10, §13 | core object definition (`ObjectDefStmt` with TypeName "web server"), read by `src/Otter.Cli/ServeCommand.cs` l.67 | tests/cli/ps-texts.psd1 ("serve: a declared port in use", host and port cases) |
| Route | `when S receives METHOD at PATH [into REQ]` + block | Declares a route. METHOD is any word and is upper-cased. When `at` comes straight after `receives`, the method is GET. Paths are matched case-insensitively and a trailing `/` is ignored. | SD D51 §1-3; r2 §10, §13 | PN `ReadWebRoute` l.404 (switch: `When` + ident + `Receives`) → `WebRouteStmt` | c/net_server.ot (GET with `into`), tests/cli/ui.psd1, ps-texts.psd1 |
| Route, any method | `when S receives a request at PATH` | Matches every method (`ALL`). | SD D51 §1; r2 §10 | `ReadWebRoute` → `WebRouteStmt(Method="ALL")` | c/net_server.ot |
| Respond | `respond with VALUE [as json] [and status N]` | Sets the route's response. Under `serve`, only the route's first `respond` counts, and only a literal value or status is used (UI2-2). | SD D51 §4; r2 §20 | PN `ReadRespond` l.435 → `RespondStmt` | c/net_server.ot (`as json and status 201`) |
| Respond, status only | `respond with status N` | Sends a status with no body. | SD D51 §4 | `RespondStmt(Value=null)` | c/net_server.ot |
| Start server | `start [the] S` / `listen S` | Starts the declared server. | SD D51 §5 | PN `ReadStart` / `ReadListenServer` l.497 → `StartServerStmt` | c/net_server.ot |
| Listen on port | `listen [on] port N` | Short server form. Its port is the default when no server is declared. | SD D51 §5; r2 §10 | `ReadListenServer` → `ListenServerStmt` | c/net_server.ot (`listen on port`, `listen port`), tests/cli/ps-texts.psd1 |
| Under `otter run` | (any of the above) | Parses, then stops at run time with a "cannot run" error (`NotRunnable`). | SD D51; otter-net SPEC-QUESTIONS UI2-2 | IN l.84-87 | c/net_server.ot |

### 16.3 WebSocket

| Construct | Syntax | Semantics (one line) | Reference | otter-net | Tests |
|---|---|---|---|---|---|
| Connect | `connect to websocket URL [using protocol P] and call it W` | Opens a ws:// or wss:// connection without waiting. W starts in `connecting`. | SD D106 | PN `ReadNetworkConnect` l.838 → `WebSocketConnectStmt` | c/net_websocket.ot, n/websocket_echo.ot, PS WebSocket.Tests.ps1 |
| Send | `send VALUE through W` | Sends a text or bytes message. | SD D106 | PN `ReadSend` l.310 → `WebSocketSendStmt` | n/websocket_echo.ot, c/net_websocket.ot |
| Close | `close websocket W [with code N] [and reason R]` | Starts the close handshake. | SD D106 | PN `ReadWebSocketClose` l.291 → `WebSocketCloseStmt` | n/websocket_close.ot, n/websocket_close_application_code.ot, c/net_websocket.ot |
| Handlers | `on open of W`, `on message from W`, `on close of W`, `on error of W` + block | Event handlers run by the event loop. | SD D106; EM | PN `ReadOnHandler` l.336 → `NetworkEventStmt(Open/Message/Close/Error)` | c/net_websocket.ot, n/websocket_echo.ot |
| Ambient values | `received message`, `close code`, `close reason`, `close was clean`, `websocket error` | Valid only inside the matching handler; anywhere else they raise an error. | SD D106 | PN `TryReadNetworkValue` → `ReceivedMessageExpr`, `CloseCodeExpr`, `CloseReasonExpr`, `CloseWasCleanExpr`, `WebSocketErrorExpr` | c/net_websocket.ot, n/websocket_*.ot |
| States | `W is connecting|open|closing|closed` | State check. The same node also serves TCP and UDP. | SD D106; RW l.189-192 | `ReadNetworkStatePredicate` → `WebSocketIsStateExpr` | c/net_websocket.ot (connecting, open, closed); `closing` only in PS Parser.Tests.ps1 |
| Properties | `state of W`, `url of W`, `protocol of W` | Reads the socket's state, URL and protocol. | SD D106 | IN `TryNetworkProperty` (OtterWebSocket) | c/net_websocket.ot, n/websocket_echo.ot |

### 16.4 TCP / TLS

| Construct | Syntax | Semantics (one line) | Reference | otter-net | Tests |
|---|---|---|---|---|---|
| Connect | `connect to tcp HOST on port N and call it C` | Opens a TCP client without waiting. Clauses may come in any order, on the line or in an indented block. | SD D107 | PN `ReadNetworkConnect` + `ReadNetClauses` l.158 → `TcpConnectStmt(IsSecure=false)` | c/net_tcp.ot, c/net_tcp_errors.ot |
| Secure connect | `connect securely to tcp HOST on port N [for server NAME] [using protocol P | using protocols LIST] and call it C` | TLS connection that always validates the certificate. `for server` sets SNI and is refused on a plain connection. | SD D112 | `TcpConnectStmt(IsSecure=true, ServerName, Protocols)` | c/net_tls.ot, c/net_c4_errors.ot, c/net_tcp_errors.ot; `using protocols` only in PS Parser.Tests.ps1 |
| Send | `send BYTES through C` | Sends bytes only; text must be converted first with `bytes from text`. | SD D107 | `ReadSend` → `WebSocketSendStmt` (shared node) | c/net_tcp.ot |
| Close | `close tcp C` | Closes the connection. | SD D107 | PN `TryReadNetworkStatement` → `NetCloseStmt("tcp")` | c/net_tcp.ot, c/net_tcp_errors.ot |
| Client handlers | `on connect of C`, `on data from C`, `on close of C`, `on error of C` | Handlers for a TCP connection. | SD D107, D112 | `NetworkEventStmt(Connect/Data/Close/Error)` | c/net_tcp.ot, c/net_tls.ot, c/net_tcp_errors.ot |
| Ambient values | `received data` (bytes), `network error` | Valid only inside `on data from` and `on error of` respectively. | SD D107 | `NetContextExpr("data"/"network error")` | c/net_tcp.ot, c/net_tls.ot, c/net_tcp_errors.ot |
| Client states | `C is connecting|connected|closed`, `C is secure` | State checks on a connection. | SD D107, D112; RW l.189-193 | `WebSocketIsStateExpr`, `ConnectionIsSecureExpr` | c/net_tls.ot, c/net_tcp_errors.ot (`secure`); `connected` only in PS Network.Tests.ps1, EventContract.Tests.ps1 |
| Properties | `state of C`, `remote address|port of C`, `local address|port of C`, `tls version|protocol of C` | Read-only properties. | SD D107, D112 | PN `TryReadNetworkValue` → `PropertyAccessExpr("remote address", ...)`; IN l.352-390 | c/net_tcp_errors.ot, c/net_tcp.ot |
| Server listen | `listen for tcp [on ADDRESS] on port N and call it S` | Opens a TCP listener. Port 0 picks a free port. | SD D113 | PN `ReadTcpListen` l.282 + `ReadListenClauses` → `TcpListenStmt` | c/net_tcp.ot, c/net_tcp_errors.ot, c/net_c4_errors.ot |
| TLS server (reserved) | `listen securely for tcp ...` | Always a syntax error: "reserved for TLS servers, which Otter does not support yet." | (otter-net/otterPS parser message only) | PN `TryReadNetworkStatement` | c/net_syn_forms.ot |
| Accept | `on connection to S` + `incoming connection` | Runs once per accepted client. `incoming connection` is a D107 connection. | SD D113 | `NetworkEventStmt(Connection)`, `NetContextExpr("incoming connection")` | c/net_tcp.ot, c/net_http_async.ot |
| Stop | `stop tcp S` | Stops the listener. Stopping again does nothing. | SD D113 | PN → `TcpStopStmt` | c/net_tcp.ot, c/net_tcp_errors.ot |
| Server states and properties | `S is listening|stopped`; `state|local address|local port of S` | State checks and properties on a listener. | SD D113; RW l.187-188 | `TcpServerIsStateExpr`; IN l.394-400 | c/net_tcp.ot, c/net_tcp_errors.ot |

### 16.5 UDP

| Construct | Syntax | Semantics (one line) | Reference | otter-net | Tests |
|---|---|---|---|---|---|
| Open | `open udp [on port N] and call it U` | Binds a UDP socket. A `to` clause is refused. | SD D108 | PN `ReadUdpOpen` l.272 → `UdpOpenStmt` | c/net_udp.ot, c/net_tcp_errors.ot |
| Send | `send BYTES through U to HOST on port N` (clauses may be on indented lines) | Sends one datagram. Both destination clauses are required. | SD D108 | `ReadSend` + `ReadNetClauses` → `UdpSendStmt` | c/net_udp.ot (both layouts), c/net_tcp_errors.ot |
| Close | `close udp U` | Closes the socket. | SD D108 | `NetCloseStmt("udp")` | c/net_udp.ot, c/net_tcp_errors.ot |
| Receive | `on data from U` + `received data`, `sender address`, `sender port` | Handler for each datagram, with its ambient values. | SD D108 | `NetworkEventStmt(Data)`, `NetContextExpr` | c/net_udp.ot, c/net_loop_passes.ot |
| States and properties | `U is open|closed`; `state of U`, `port of U` | State checks and properties on a socket. | SD D108 ("state as for D107") | `WebSocketIsStateExpr`; IN l.405-406 | c/net_udp.ot |

### 16.6 File watching

| Construct | Syntax | Semantics (one line) | Reference | otter-net | Tests |
|---|---|---|---|---|---|
| Watch | `watch file PATH and call it W` / `watch folder PATH [recursively] and call it W` | Starts a watcher. A missing path is an error. `recursively` is accepted only for folders. | SD D104 | PN `ReadFileWatch` l.128 → `FileWatchStmt` | c/net_watch.ot, c/net_loop_passes.ot, c/net_c4_loop_passes.ot; PS FileWatching.Tests.ps1 |
| Handlers | `on change of W`, `on create in W`, `on delete in W`, `on rename in W` | Handlers for each change kind. Duplicate notifications within 150 ms are merged. | SD D104 | PN `ReadOnHandler` → `ReadWatchEvent` l.388 → `WatchEventStmt` | c/net_watch.ot (create, delete, change errors), c/net_check_runs.ot; `rename` only in PS FileWatching.Tests.ps1 |
| Ambient values | `changed path`, `changed file name`, `change kind`, `old path` | Valid only inside a watcher handler. | SD D104 | `ChangedPathExpr`, `ChangedFileNameExpr`, `ChangeKindExpr`, `OldPathExpr` | c/net_watch.ot |
| Is watching | `W is watching` | True while the watcher runs. | SD D104; RW l.194 | `IsWatchingExpr` | c/net_watch.ot |
| Stop | `stop watching W` | Stops the watcher. Stopping twice is fine; a non-watcher is an error. | SD D104 | PN (`Return` token `stop`) → `StopWatchingStmt` | c/net_watch.ot, c/net_c4_loop_passes.ot |

### 16.7 Command jobs

| Construct | Syntax | Semantics (one line) | Reference | otter-net | Tests |
|---|---|---|---|---|---|
| Start | `start command CMD and call it J` | Starts a process in the background. `.ps1` and `.cmd` scripts are dispatched to their hosts (D119-R1). | SD D119-R2; STANDARD_LIBRARY "Processes" | PN `ReadStart` → `StartCommandStmt` | c/net_jobs.ot; PS AsyncCommand.Tests.ps1 |
| Handlers | `on output from J`, `on error output from J`, `on exit of J`, `on complete of J` (exit code 0), `on cancel of J` | The first three become `WhenStmt` nodes. `complete` and `cancel` are `NetworkEventStmt`. Terminal events are kept for handlers registered later. | SD D119-R2 | PN `ReadOnHandler` → `ReadJobHandler` l.381 (→ `WhenStmt`) / `ReadNetworkEvent` | c/net_jobs.ot |
| Ambient values | `received output`, `received error output` | Valid only inside the output handlers. | SD D119-R2 | `JobContextExpr` | c/net_jobs.ot |
| Cancel | `cancel J` | Kills the process tree. Cancelling again does nothing. | SD D119-R2 | `HttpCancelStmt` (shared) | c/net_jobs.ot |
| States | `J is running|completed|failed|cancelled` (and `pending` as a synonym) | State checks on a job. | SD D119-R2 (lists running, completed and failed only); RW l.182-186 | `HttpRequestIsStateExpr` | c/net_jobs.ot |
| Properties | `state`, `exit code`, `output`, `error output`, `id`, `command` `of J` | Read-only properties. | SD D119-R2 | `src/Otter.Runtime/Jobs.cs` `TryJobProperty` l.~336 | c/net_jobs.ot (state, exit code, error output); `command of J` does not parse (see Gaps) |

### 16.8 Event handlers, event loop, wait, await

| Construct | Syntax | Semantics (one line) | Reference | otter-net | Tests |
|---|---|---|---|---|---|
| Request handlers | `on complete of R`, `on error of R`, `on cancel of R` | Exactly one terminal event fires per request. A request that has already finished fires its handler when the handler is set up. | SD D116B §2, §5 | `NetworkEventStmt(Complete/Error/Cancel)` | c/net_http_requests.ot, c/net_http_async.ot |
| Unknown `on` word | `on X ...` | Any word that is not a known event is a syntax error: "I expected 'start' or 'close' after 'on'". | parser message | `ReadOnHandler` | c/net_syn_forms.ot |
| Event loop | (implicit) | Handlers run one at a time after the last statement. Open watchers keep the program alive; sockets, requests and jobs keep it alive only while they have handlers. | SD D121; EM | `src/Otter.Runtime/EventLoop.cs` `RunEventLoop` l.~83 | all c/net_* event tests |
| Wait | `wait N millisecond(s)|second(s)|minute(s)|hour(s)|day(s)|month(s)|year(s)` | Sleeps and services every event source while it waits (EV3). Not available on the web target. | SD D101, D121 §4 (EV3); EM rule 4; STANDARD_LIBRARY | core `Parser.cs` `ReadWait` l.998; EventLoop `ServiceEventsDuringWait` | c/net_watch.ot, c/net_http_requests.ot, c/net_loop_passes.ot |
| Loop-pass capture | (a handler set up inside a loop) | Watcher and `when` handlers keep the names of their own loop pass. Network handlers read the loop's names live (Q13). | SD D130 | `src/Otter.Language/LoopPasses.cs`; EventLoop D130 | c/net_loop_passes.ot, c/net_c4_loop_passes.ot |
| Handler-run capture | a `when` handler set up inside another handler | Keeps the outer run's names. A watcher handler nested like this is refused before the program runs. | SD D133 | EventLoop D133; LoopPasses.cs | c/net_check_runs.ot |
| Await | `await X [make|into Y]`, `await get URL`, value `await X` | Parses, but it is not part of 1.0: `run` stops with "'await' is not supported in Otter 1.0." | SD D56 V1 audit l.3709 | `src/Otter.Language/Parser.Ui.cs` `ReadAwaitStatement` l.164 / `ReadAwaitValue` → `AwaitStmt` / `AwaitExpr`; IN l.88, l.~189 | c/net_server.ot, c/net_syn_forms.ot |

Construct count: 71 table rows (HTTP 20 including 4 option sub-rows, Web server 8, WebSocket 7, TCP/TLS 13, UDP 5, File watching 5, Command jobs 6, Event/loop/wait/await 7).


## 17. UI, reactivity, routing; databases and queries


Paths: otterPS = `otterPS-1.0.1`; otter-net = `otter-net`.
Abbreviations: **SD** = otterPS `SPEC-DECISIONS.md` (line); **RW** = otterPS `docs/OTTER_1_0_RESERVED_WORDS.md`; **INV** = otterPS `docs/OTTER_1_0_LANGUAGE_AND_RUNTIME_INVENTORY.md`; **SQ** = otter-net `docs/SPEC-QUESTIONS.md`; **PUi** = otter-net `src/Otter.Language/Parser.Ui.cs`; **PData** = `Parser.Data.cs`; **IUi** = `src/Otter.Runtime/Interpreter.Ui.cs`. Test names without a folder are in otter-net `tests/compat/`; `web/x.ot` = `tests/web/`, `desktop/x.ot` = `tests/desktop/`, `cli/ui.psd1` = `tests/cli/ui.psd1`. otterPS test suites are under otterPS `tests/`.

`otterPS docs/GRAMMAR.md` lists only `UiStmt` (line 104) and says library statements, UI included, are "described on their own documentation pages" (lines 5-8): no EBNF exists for anything in this section. Grammar authority is therefore the SPEC-DECISIONS entries plus the parser.

Runtime status is what `otter run` does (otter-net, same as otterPS by design). "Web-only" means only `otter web` gives the form a meaning.

### 17.1 UI, reactivity & routing

| Construct | Syntax | Semantics (one line) | Reference (otterPS) | otter-net (parser method + AST node) | Tests |
|---|---|---|---|---|---|
| Create UI resource | `create [the] KIND into [the] NAME` (KIND = one or more words up to `into`: `button`, `text box`, `window`, `row`, `column`, `scroll`, `card`, `heading`, `panel`, `grid`, `text`, `primary button`, `secondary button`, `danger button`) | Makes a native UI resource and binds it to NAME. Run: on macOS/Linux it fails with "Otter could not start its UI provider. WPF may not be available on this system."; on Windows it goes through the `otter-desktop` WPF host. An unknown KIND is accepted by the parser and refused at runtime: "I don't know how to create a {kind} yet." | SD D44 (l.2033); D52 contextual `the` (l.3051); D53 scroll (l.3149); D128 web runtime creation (l.7799); RW `create` row | PUi `TryReadUiStatement` case `Create` (l.142) + `ReadUiResourceTypeName` (l.389) → `CreateUiResourceStmt(TypeName, Target)`; kinds in `src/Otter.Desktop/WpfUiProvider.cs` `KindNames` (l.82) | ui_run_create, ui_run_create_try, ui_run_create_unknown; desktop/kinds_all.ot, desktop/example_hello_app.ot; web/create_resources.ot; cli/ui.psd1 "run: create without WPF"; otterPS UI.Tests.ps1 |
| Resource by assignment | `NAME is a KIND [with PROP VALUE, ...]`, block form `NAME is a KIND` + indented `PROP is VALUE` lines + `.`; `NAME is panel with ...` (article optional only for `panel ... with`) | On `otter web` this declares a page element. On `otter run` it is an ordinary `thing` whose properties can be read back (no window). | SD D128 (l.7799); otterPS docs/TUTORIALS.md §4-5 (the tutorial's `counterLabel.text is ...` uses dot access, which the scope matrix lists as UNSUPPORTED) | core `Parser.cs` assignment path (l.1324-1349, D110 bare-panel rule at l.1326) → `ObjectDefStmt`/`AssignStmt` (not a Syntax.Ui node) | ui_run_resource_thing; web/resources.ot, web/variant_buttons.ot, web/page_counter.ot, web/links.ot, web/align_spread.ot |
| UI property set/read | `PROP of NAME is VALUE`; `[the] PROP of [the] NAME`; `NAME has PROP [is] VALUE, PROP VALUE` (inline) or block `NAME has` + lines; UI value forms `align top\|middle\|bottom\|left\|center\|right`, `width full`, `height full`, bare `round` / `spread` | When NAME holds a UI resource, `has` sets properties on it rather than building a thing; otherwise it is the D40 thing literal. | SD D45 (l.2173), D52 (l.2962), D54 (l.3293) | `Parser.cs` has-lists (l.1586, l.1636) + PUi `ReadUiPropertyValue` (l.610); runtime `Interpreter.cs` l.167 → IUi `SetUiProperties` (l.166) | desktop/properties.ot, desktop/kinds_all.ot; web/c4_set_ui_properties.ot, web/align_spread.ot; ui_run_resource_thing; cli/ui.psd1 "check: a UI syntax error" (`align sideways`) |
| Event handler | `when NAME [is] EVENT` + block + `.` (EVENT is one raw word, e.g. `clicked`, `click`, `changed`, `closed`) | Registers the block for that resource event. Run: WPF only; a non-resource target gives "I can only listen for an event on a UI resource or command job, but this is …". `when the x …` is a syntax error: "Event targets do not use \"the\" here." | SD D46 (l.2341) | PUi `TryReadWhenStatement` (l.304) → `WhenStmt(Target, EventName, Body)`; runtime `Jobs.cs` l.254 → IUi `AddUiHandler` (l.152); events mapped in `WpfUiProvider.cs` l.73-78 | ui_run_when_clicked; desktop/events.ot, desktop/handlers_capture.ot; web/page_counter.ot, web/two_handlers.ot, web/c4_when_in_function.ot; otterPS UI.Tests.ps1, Web.Tests.ps1 |
| Put in container | `put [the] ITEM[, ITEM ...] in [the] CONTAINER` | Adds each item to the container in order (one `PutIn` per item). Item and container must both be UI resources ("I can only put a UI resource somewhere, but this is …"). `put X to URL` / `put X as json to URL` are HTTP. | SD D47 (l.2479); D52 comma `put` | PUi `TryReadPutInStatement` (l.332) → `PutInStmt(Item, Container)`, or a `StatementGroup` that is spliced into the statement list | ui_run_put, ui_run_put2; desktop/kinds_all.ot; web/dragdrop_board.ot, web/runtime_ui_put_number.ot |
| Show | `show [the] NAME` (`show progress …` is the D100 console statement) | Shows the window modally (WPF `ShowDialog`) until it closes. Non-resource: "I can only show a UI resource, but this is …". | SD D47 (l.2479); D56 affirmation (l.7834: `show` as a UI *action* is post-1.0) | PUi `TryReadUiStatement` case `Show` (l.133) → `ShowStmt(Target)` | ui_run_show, ui_run_show_missing; desktop/*.ot; web/page_counter.ot |
| Declarative UI element | `TAG [LABEL-EXPR]` + optional indented body (children, properties, layout, events, animations), where TAG ∈ `window page card heading text button panel section sidebar main link image input grid`. Not an element when the next token is `is/are/of/has/make/into/is not`. | `otter run` runs only the nested statements: properties, events and animations are ignored and no window opens (SQ UI2-5). `otter web` renders the element. Post-v1 by D56. | SD D56 (l.3645) and D56 scope note (l.3790); RW UI element words (rows 168-181) | PUi `ReadUiElementStatement` (l.409) → `UiElementStmt(Tag, Variant, Label, Name, Layout, Properties, Events, Animations, Children)` | ui_run_page, ui_run_ui_words; web/declarative.ot, web/c4_declarative.ot, web/declarative_roots.ot, web/declarative_function.ot; cli/ui.psd1 "check: a declarative page", "run: declarative elements run their statements" |
| Element variants | `primary\|secondary\|danger TAG …`, where TAG ∈ `button card heading text panel link image input` | Styled variant of the element. `primary "x"` with no tag after it is not an element (it parses as a call). | SD D56 scope note (l.3790); RW l.251 | PUi l.29 `isVariant` → `UiElementStmt.Variant` | ui_run_primary_text, ui_run_ui_words; web/declarative.ot, web/variant_buttons.ot, web/runtime_ui_variant_buttons.ot |
| Input element | `input NAME` | Names an input field. When the token after `input` is an identifier it becomes `Name`, otherwise it is read as a label. | SD D56 | PUi l.419 → `UiElementStmt.Name` | web/declarative.ot |
| Layout | Inside an element: `layout MODE` / `layout grid N`. At top level: `layout MODE [N]`. Modes seen: `centered stack row grid navbar cards form` | Sets the element's layout mode (and grid column count). At top level it is an empty `UiElementStmt("layout")` that does nothing when run. | SD D56; RW `layout` row (l.129: "read as a UI element (D56): nothing runs") | PUi body loop l.436 and top-level case `Layout` (l.116) → `UiLayoutSpec.Mode/Columns` | ui_run_layout; web/declarative.ot, web/c4_declarative.ot, web/window_layout.ot |
| Align / gap / spread | `align WORD`, `gap EXPR`, `spread` (each on its own line in an element body) | Alignment, spacing and spread distribution for the element. | SD D54 (l.3293), D56 scope note (gap vs D48 `spacing`) | PUi l.445-468 → `UiLayoutSpec.Align/Gap/Spread` | web/declarative.ot, web/c4_declarative.ot, web/align_spread.ot, web/c4_err_column_spread.ot |
| Responsive rules | `columns N on BREAKPOINT`, `stack on BREAKPOINT` (breakpoints seen: `small medium large wide`) | Per-breakpoint column count, or stacking. N is cast with PowerShell `[int]` (2.5 → 2, `true` → 1, `"0x10"` → 16); a value it cannot cast is a syntax error. | SD D56 | PUi l.469-497 → `ResponsiveRule(Breakpoint, Columns, Stack)` | t3_ui_columns_cast, t3_ui_columns_text; web/responsive_text_columns.ot, web/t3_columns_cast.ot |
| Style properties | `round [N]`, `background V`, `placeholder V`, `padding V`, `margin V`, `width V\|full`, `height V\|full` (no `is`) | Element style values. Any other word (for example `color`, `foreground` in c4_declarative) is read as an ordinary child statement. | SD D56, D48 (l.2602) | PUi l.498 → `UiElementStmt.Properties` (`AssignStmt`s) | web/declarative.ot, web/c4_declarative.ot, web/dimensions.ot |
| Animation block | `enter\|leave\|hover\|press` + indented steps: `fade [in\|out]`, `move DIR [N]`, `slide WORDS…` (`slide from left`), `grow\|shrink\|scale\|rotate [N]`, `animate N [ms\|s] [EASING]` (EASING: `ease`, `ease-out`, `ease-in`, `spring`, `linear`; default 300 ms `ease`) | Web-only CSS animation. Run: ignored (inside an element); `IUi` has "UI animation is not supported in Otter 1.0." but no parsed program reaches that case. | SD D56 (excluded: `UiAnimation`); INV A12 | PUi l.507 + `ReadAnimationBlock` (l.534) → `UiAnimationBlock(Trigger, Steps, DurationMs, Easing)` / `UiAnimationStep` | web/declarative.ot, web/c4_declarative.ot; otterPS UI.Tests.ps1 (`animate`) |
| UI event block | `click\|change\|input\|submit\|focus\|blur` + indented block, inside an element (`hover`/`press` are always read as animation blocks, which are checked first) | Web-only element event. Run: ignored (same unreachable refusal "UI event blocks are not supported in Otter 1.0."). | SD D56 (excluded: `UiEvent`); INV A12 | PUi l.514 → `UiEventStmt(EventName, Body)` | ui_run_page; web/declarative.ot, web/c4_declarative.ot |
| State | `state NAME is EXPR` | Declares a reactive signal. Assigning to it notifies watchers only when the value changes (Otter equality). | SD D56 "V1 SUPPORTED" (l.3667); RW l.254 (top-level `count is …` is reserved, so use another name) | PUi case `State` (l.68) → `StateDefStmt(Name, InitialValue)`; IUi l.60 (`OtterSignal`, `Reactivity.cs`) | ui_run_state, ui_run_watch_try, ui_run_watch_error; cli/ui.psd1 "run: state and watchers"; web/declarative.ot; otterPS Interpreter.Tests.ps1 |
| Derive | `derive NAME is EXPR` | A derived value that tracks its dependencies. A cycle is a runtime error: "Circular dependency detected in derived value \"X\"." Worked out in the scope that reads it (SQ UI2-4). On the web a derive with arithmetic gives an invalid page script (SQ UI-1). | SD D56 | PUi case `Derive` (l.77) → `DeriveDefStmt`; IUi `ReadDerived` | ui_run_state, ui_run_derive_cycle; web/declarative.ot, web/c4_err_derive_in_handler.ot |
| Watch | `when NAME changes` + block | Runs the block when the state or derived value changes. On a plain variable, `otter run` wraps it into a signal; the web differs (scope-matrix "Known differences"). | SD D56; scope matrix l.72 | PUi `TryReadWhenStatement` (l.326, event word `changes`) → `WatchStmt(TargetName, Body)`; IUi l.71 | ui_run_state, ui_run_watch_error, ui_run_watch_try; web/handler_runs_watch_refused.ot, web/t3_err_watch.ot |
| Memo | `memo NAME` + block | Not in 1.0. Parses; run: "'memo' is not supported in Otter 1.0." (the web refuses it the same way, D128). | SD D56 excluded; INV A12; RW `memo` | PUi case `Memo` (l.86) → `MemoDefStmt` | ui_run_memo; web/err_memo.ot; otterPS Interpreter.Tests.ps1 |
| Shared | `shared NAME is EXPR` | Not in 1.0. Run: "'shared' is not supported in Otter 1.0." | SD D56 excluded; INV A12 | PUi case `Shared` (l.92) → `SharedStateStmt` | ui_run_shared; web/err_shared.ot |
| Lifecycle | `on start` + block; `on close` + block (`on close of X` is networking) | Not in 1.0. Run: "'on start' is not supported in Otter 1.0." (and the same for `on close`). | SD D56 excluded; INV A12 | PUi `TryReadUiOnStatement` (l.292) → `LifecycleStmt(Stage, Body)` | ui_run_on_start, ui_run_on_close; web/err_lifecycle.ot; desktop/lifecycle.ot; otterPS Interpreter.Tests.ps1 |
| UI actions | `focus VALUE`, `hide VALUE` | Not in 1.0 (planned for 1.1). Run: "'focus' is not supported in Otter 1.0." / "'hide' …"; the web refuses them too (D128). | SD D56 + D56 affirmation (l.7834); INV A12; RW `focus`/`hide` | PUi cases `Focus`/`Hide` (l.108) → `UiActionStmt(Action, Target)` | ui_run_focus, ui_run_hide; web/err_focus.ot |
| Await statement | `await EXPR`; `await NAME ARG [and ARG…]`; `await get URL`; `await X make NAME`; `await X into NAME` | Web: awaits an async call (functions become async). Run: a plain `await …` gives "I do not know how to run a Await statement yet."; the `make`/`into` forms become an `AssignStmt` of an `AwaitExpr` and give "'await' is not supported in Otter 1.0." | SD D56 excluded; INV A12 (+ caveat on the `make`/`into` forms) | PUi `ReadAwaitStatement` (l.164) → `AwaitStmt` or `AssignStmt(…, AwaitExpr)` | ui_run_await_stmt, ui_run_await_make; web/await_forms.ot, web/await_delay.ot; otterPS Web.Tests.ps1 (20 hits) |
| Await expression | `NAME is await X` (also `await delay 10`, `await get "x"`) | Run: "'await' is not supported in Otter 1.0." | INV A12 (`say await 5`) | PUi `ReadAwaitValue` (l.204) via `TryReadUiValue` (l.644) → `AwaitExpr` | ui_run_await_value; web/await_forms.ot |
| Route | `route PATH-EXPR shows PAGE` (PATH may hold `:param` segments); `route otherwise shows PAGE` (`show` is also accepted for `shows`) | Web-only SPA route table. Run: "I do not know how to run a Route statement yet." `route is …` is an ordinary variable. | SD D103 (l.7455) | PUi `ReadRouteStatement` (l.231) → `RouteStmt(Path, IsOtherwise, PageName)` | ui_run_route, ui_run_ui_words; web/routing.ot, web/routing_pages.ot, web/route_variable.ot; otterPS Web.Tests.ps1 |
| Go to | `go to PATH-EXPR` | Web: navigates. Run: "I do not know how to run a GoToRoute statement yet." | SD D103 | PUi l.38-52 → `GoToRouteStmt(Path)` | ui_run_goto; web/routing.ot |
| Go back / forward | `go back`, `go forward` (`go home` is not this form: it is a call to `go` and fails at runtime with "Otter could not find anything called \"go\".") | Web: browser history. Run: "I do not know how to run a GoNavigate statement yet." | SD D103 | PUi l.43-47 → `GoNavigateStmt(IsForward)` | ui_run_goback, ui_run_go_home; web/routing.ot; otterPS UI.Tests/Web.Tests (`go back`) |
| Replace route | `replace route with PATH-EXPR` (must end there: `… in X` makes it the text `replace` statement) | Web: replaces the current history entry. Run: "I do not know how to run a ReplaceRoute statement yet." | SD D103 | PUi `TryReadReplaceRouteStatement` (l.366) → `ReplaceRouteStmt(Path)` | ui_run_replace_route, t3_replace_route_extra; web/routing.ot, web/t3_replace_route_extra.ot |
| Route change | `on route change` + block | Web: handler for route changes. Run: "I do not know how to run a RouteChangeEvent statement yet." Not deferred for D133 (SQ UI-3). | SD D103 | PUi `TryReadUiOnStatement` (l.264) → `RouteChangeStmt(Body)` | ui_run_route_change; web/routing.ot |
| Current route | `current route` (expression) | Web: the current path. Run: "I do not know how to work out a CurrentRoute value yet." | SD D103 | PUi `TryReadUiValue` (l.647) → `CurrentRouteExpr` | c4_ui_run_current_route; web/routing.ot |
| Route parameter | `route parameter EXPR` (e.g. `route parameter "id"`) | Web: value of the `:id` segment. Run: "I do not know how to work out a RouteParameter value yet." | SD D103 | PUi l.652 → `RouteParameterExpr(Name)` | ui_run_route_param |
| Query parameter | `query parameter EXPR` (expression only; a line that *starts* with `query` is the D97 `query` statement) | Web: URL query-string value. Run: "I do not know how to work out a QueryParameter value yet." | SD D103 | PUi l.657 → `QueryParameterExpr(Name)` | ui_run_query_param; otterPS Parser.Tests.ps1 |
| Drag/drop properties | `draggable is true`, `accepts drops is true` (inline `with …` list or block form) | Marks an element draggable / a drop target (web). | SD D110 (l.7565) | PUi `ReadAcceptsDrops` (l.595), called from `Parser.cs` l.1586/1636 | ui_run_resource_thing; web/dragdrop_board.ot, web/dragdrop_tile.ot; otterPS DragDrop.Tests.ps1 |
| Drag/drop handlers | `on drag of X`, `on drop on X`, `on files dropped on X` + block; also `when X drag` | Web-only (top-level only, D128). Run: "Drag and drop (\"on drag of\", \"on drop on\", \"on files dropped on\") is not supported for windows yet. It works in web applications." | SD D110 | PUi `TryReadUiOnStatement` (l.273-290) → `WhenStmt(Target, "drag"\|"drop"\|"files dropped")`; refused in `Jobs.cs` l.256 | ui_run_on_drag; web/dragdrop_board.ot, web/dragdrop_tile.ot, web/c4_when_drag.ot; otterPS DragDrop.Tests.ps1 |
| Set drag data | `set drag data to EXPR` | Attaches data to the drag (web, inside `on drag of`). Run: "\"set drag data\" is only available in web applications, inside \"on drag of ...\"." | SD D110 | PUi l.55 → `SetDragDataStmt(Value)` | ui_run_drag; web/dragdrop_board.ot |
| Drag context values | `dragged item`, `dropped files`, `drag data`, `drop x`, `drop y` | Values available inside drag and drop handlers (web). Run: "\"drop x\" is only available in web applications, inside a drag or drop event." | SD D110 | PUi `TryReadUiValue` (l.663) → `DragContextExpr(Field)` | ui_run_dragged, ui_run_drop_x; web/dragdrop_board.ot |

### 17.2 Databases & queries

| Construct | Syntax | Semantics (one line) | Reference (otterPS) | otter-net (parser method + AST node) | Tests |
|---|---|---|---|---|---|
| Database config | `CFG has` + `provider is "sqlite"` + `connection is "file.db"\|":memory:"` [+ `readonly "true"`] + `.` (or the inline `has` form) | A plain D40 thing that describes the connection. | SD D97 (l.7044) | core object literal (`ObjectDefStmt`) | data_db, data_query |
| Connect | `connect CONFIG-EXPR into NAME` (CONFIG may be a thing or a text path such as `"data.db"`). `connect to …` and `connect securely …` are networking. | Opens a `'database connection'`. Connection-string secrets are masked when printed. | SD D97 §1; RW `connect` | PData `TryReadDataStatement` (l.28) → `ReadDatabaseConnect` (l.105) → `ConnectDbStmt(Config, Target)` | data_db, data_syn_db, data_db_checks, data_db_errors; web/t3_err_db.ot (refused on the web); otterPS Database.Tests.ps1 |
| Disconnect | `disconnect EXPR` | Closes the connection. Using it afterwards is an Otter error. | SD D97 §1 | PData l.30 → `DisconnectDbStmt(Connection)` | data_db, data_query, data_syn_db |
| Query (one line) | `query CONN with SQL-EXPR into NAME` | Runs SQL and stores a list of `database row` things (columns are properties; NULL → `gone`). Only the first SQL statement in the text runs (SQ Q11). | SD D97 §2 | PData `ReadDbQuery` (l.151) → `DbQueryStmt(Connection, Query, Parameters, Target)` | data_db, data_syn_db, data_db_fatal, data_db_internal |
| Query with parameters | `query CONN with` / indented `SQL-EXPR` / `parameter "NAME"\|NAME is EXPR` … / dedent / `into NAME` | Parameterised SQL (placeholders `@n`, `:n`, `$n`, `?1`). Names ignore case; the first spelling wins. `gone` binds NULL. | SD D97 §2, Security Model | PData `ReadDbQuery` + `ReadDbParameters` (l.117) → `DbParameter(Name, Value)` | data_db, data_syn_db |
| Execute | `execute CONN with SQL-EXPR [into NAME]`, or the block form with `parameter` lines and an optional trailing `into NAME` | Runs INSERT/UPDATE/DELETE/DDL. `into` receives a `'database result'` with `rows affected of r` and `last inserted id of r`. | SD D97 §3 | PData `ReadDbExecute` (l.180) → `DbExecuteStmt(Connection, Command, Parameters, Target?)` | data_db, data_syn_db, data_db_errors |
| Begin transaction | `begin transaction on CONN into NAME` (then `execute TX with …` / `query TX with …`) | Starts a transaction object that the D97 statements accept in place of the connection. | SD D97 §4 | PData `ReadBeginTransaction` (l.218) → `BeginTransactionStmt(Connection, Target)` | data_db, data_syn_db |
| Commit | `commit TX` | Commits. | SD D97 §4; RW `commit` | PData l.43 → `CommitTransactionStmt(Transaction)` | data_db, data_syn_db |
| Rollback | `rollback TX` | Rolls back; works with `try/otherwise`. | SD D97 §4; RW `rollback` | PData l.50 → `RollbackTransactionStmt(Transaction)` | data_db, data_syn_db |
| Get tables | `get tables from CONN into NAME` | A list of `database table` (`name`, `schema`, `kind` = "table"\|"view"). | SD D98 (l.7124) | PData `TryReadSchemaGet` (l.238) → `GetTablesStmt(Connection, Target)` | data_db, data_syn_db |
| Get columns | `get columns from TABLE-EXPR in CONN into NAME` (TABLE is text or a `database table`) | A list of `database column` (`name`, `type`, `database type`, `nullable`, `primary key`, `primary key position`, `default expression`). | SD D98 | PData `TryReadSchemaGet` → `GetColumnsStmt(Table, Connection, Target)` | data_db, data_syn_db |
| D99 query | `get [distinct] all\|FIELD[ and\|, FIELD…] from TABLE in CONN [as ALIAS] [with] [where …] [order by …] [take\|first\|limit N] [skip\|offset N] into NAME`. Clauses may be on indented lines; the whole statement may be spread over lines (`get` / `all` / `from …`). | Translated to parameterised SQL. Experimental, not part of 1.0 (D99 is deferred), but it parses and runs. | SD D99 (l.7368, "What exists"); docs/D99-QUERY-LANGUAGE-DESIGN.md; SQ Q10 | `Parser.cs` l.856 `IsQueryStatementAhead` (PData l.281) → `ReadQueryStatement` (l.380) → `QueryStmt(IsDistinct, Projections, Table, Connection, Alias, Where, OrderBy, Limit, Offset, Target)`; runtime `Queries.cs` | data_query, data_syn_query, data_query_checks, data_query_internal; otterPS Query.Tests.ps1 |
| Where clause | `where TERM [and\|or TERM…]` with `is`, `is not`, `is at least\|at most\|greater than\|less than`, `contains`, `starts with`, `ends with`, `is gone`/`is not gone` (or binds loosest, then and; no `not`, no parentheses) | Filter. The left operand is a column; the right operand is a value (variable or literal) bound as a parameter. | SD D99; D99 design §3 | PData `ReadQueryWhere`/`ReadQueryAnd`/`ReadQueryTerm` (l.505-576) → `LogicalExpr`/`ComparisonExpr`/`ContainsExpr`/`TextMatchExpr` | data_query |
| Between | `COL is between A and B` (also `between`) — **only inside a query `where`** | Inclusive range. | SD D99; D99 design l.584-586 | PData `ReadQueryTerm` → `QueryBetweenExpr(Expression, Lower, Upper)` | data_query, data_syn_query, data_in_conditions (syntax error outside a query) |
| In / not in | `COL is in LIST-EXPR`, `COL is not in LIST-EXPR` (a single value counts as a one-item list) — **only inside a query `where`** | Membership. An empty list gives no rows (`in`) or every row (`not in`). | SD D99; D99 design l.576 | PData `ReadQueryTerm` → `QueryInExpr(Expression, Collection, IsNot)` | data_query, data_in_conditions |
| Order by | `order by EXPR [ascending\|descending] [then EXPR [ascending\|descending] …]` | Sort order. | D99 design Q21 (l.505) | PData `ReadQueryOrderByItems` (l.578) → `QueryOrderByItem` | data_query |
| Limit / offset | `take N`, `first N`, `limit N`; `skip N`, `offset N` (a fractional N is rounded; non-numeric text is an internal PowerShell conversion error) | LIMIT / OFFSET. | D99 design Q22 (l.506: `first`/`skip`) | PData `ReadQueryStatement` l.430-440 | data_query, data_query_internal, data_syn_query |
| Aggregate query | `count all\|count of X\|sum of X\|total of X\|average of X\|avg of X\|minimum of X\|min of X\|maximum of X\|max of X from TABLE in CONN [as ALIAS] [with] [where …] into NAME` (`of` is optional) | A single scalar. `total` is SQLite `TOTAL` (0.0, not `gone`, on no rows); `avg`/`min`/`max` are synonyms. `count from A to B as n` stays the count loop. | SD D99 "What exists"; D99 design Fixture E (l.323); RW l.252 (`total/avg/min/max` only on lines with `from` and `into`) | `Parser.cs` l.317 `IsAggregateQueryAhead` (l.1402), l.398, l.410 → PData `ReadQueryAggregateStatement` (l.455) → `QueryAggregateStmt(AggregateFunction, Expression, Table, Connection, Alias, Where, Target)`; `Queries.cs` l.98-102 | data_query, data_syn_query; otterPS Query.Tests.ps1 (l.263-285) |
| `the` before a property | `the PROP of X` (also two-word properties: `the exit code of`, `the rows affected of`, `the last inserted id of`, `the primary key of` …) | The readability article. It is dropped with no AST effect; elsewhere `the` is an ordinary name (`the is "article"`). | SD D52 "Contextual `the`" (l.3051); SD l.452-459; rules.md l.721 | PData `SkipReadabilityArticle` (l.73) + `CommandResultPropertyAt`, called at `Parser.cs` l.1700 | data_the, data_syn_the, data_db (`the rows affected of second`) |
| `the` in property assignment | `the PROP of X is VALUE` | Same as `PROP of X is VALUE`. | SD D52 | PData `TryReadDataStatement` l.15 → `AssignStmt` | data_the, data_syn_the; desktop/example_hello_app.ot |
| `the` before resource names | `create the window into the app`, `put the a in the b`, `show the app` | Dropped. Not allowed after `when` (syntax error). | SD D52 | `ReadOptionalTheBeforeName` (`Parser.System.cs` l.446), called from PUi l.137/151/336/356 | desktop/example_hello_app.ot; sample ui_resources.ot |


## Verified samples

All paths are relative to `samples/syntax/` in this folder. "valid (0)" = `otter check` printed "... is valid." with exit code 0.

### Core (sections 1-13)


All under `samples/syntax/core/`. Each was run as `perl -e 'alarm 20; exec @ARGV' otter check|run FILE`.

| Sample | Covers | check | run |
|---|---|---|---|
| core_values_ops.ot | comments, literals, escapes, assignment, `not`, `0 minus 5`, all arithmetic words and symbols, left-to-right order, `and` as addition, `make`, `say` (incl. empty), log/warn/error | valid (0) | 0; `2 plus 3 times 4` -> 20, `10 - 4 / 2` -> 3, `\q` kept verbatim, prefixes `log: `/`warn: `/`error: ` |
| core_control.ot | if / otherwise if / otherwise, all comparisons, contains/starts/ends, and/or/not, D123 arithmetic, D38B continuation, truthiness, while, repeat, count, for each, bare each, increase/decrease, add/remove on numbers | valid (0) | 0 |
| core_functions.ot | `to` with `and` between params, return, stop, call statement, `make` and `into` capture, call expression, recursion, try/otherwise [into], fail with, try without otherwise | valid (0) | 0 |
| core_collections.ot | list literal/empty, add/remove, of-operations, sort, reverse, contains, things (inline, block, bare property, `is a thing`), type + `with` + no-property instance, nested `of`, `the`, get/set keys, replace (both), split, join, find, math operations, pi | valid (0) | 0 |
| core_dates.ot | date from [using], date parts, add/remove units, format, between (statement and expression), today, now | valid (0) | 0 (a first version with `add 2 hours` on a date-only value failed at run time as documented: "This is a date with no time of day, so it has no hour.") |
| core_modules.ot + lib/helpers.ot | `use` (twice: included once), a call to an imported function | valid (0) | 0 (prints 42) |
| core_ask.ot | `ask ... and call it`, numeric coercion | valid (0) | 0 with stdin `41` -> "Next year: 42" |

Probes (`samples/syntax/core/probes/`, `check` only unless noted) confirming what is NOT Otter 1.0 syntax:

| Probe | Result |
|---|---|
| `x is -5` | exit 2 "I expected a value here." |
| `x is (2 plus 3)` | exit 2 "I don't understand '('." |
| `x is 7 modulo 2` | exit 2 (no `modulo`) |
| `say ready or done` | exit 2 "\"or\" only works inside an if or while condition." |
| `result is ready and active` (booleans) | check 0; run: runtime error "A true/false value cannot be combined with \"and\" here." |
| `now is 5` | exit 2 built-in value |
| `say p.name` / `x = 5` | exit 2 lexer errors |
| `to area with w, h` (SPECIFICATION.md §3.1/§8.1) | exit 2 "I expected a parameter name." |
| `make 2` as a return (SPECIFICATION.md §3.1) | exit 2 "I don't understand 'make'." |
| `say p's name` (SPECIFICATION.md §9.1) | exit 2 "I don't understand '''." |
| `a User has name, email` (SPECIFICATION.md §9.1) | exit 2 |
| `x is area with 10, 20` (SPECIFICATION.md §8.1) | exit 2 |
| `read file "x" into t`, `write "x" to file "x"`, `ask "q" into v` (SPECIFICATION.md §13) | exit 2 each |
| `convert p to json` without `into` (SPECIFICATION.md §13) | exit 2 "I expected \"into\"" |
| `return age is at least 18` / `say x is 5` | exit 2 (comparisons are not values) |
| `xs are "a", "b"` | exit 2 |
| 2-space indentation | exit 2 "Indentation must use groups of 4 spaces or tabs." |
| `count is 1` at top level | exit 2 reserved |
| typed instance with indented initializer | exit 2 "'Person' is a declared type, so its properties must use 'with' on the same line." |
| `to f a b` | exit 2 (`a` is reserved) |
| `m is increase n` | exit 2 (statement only) |
| `break` / `skip` in a loop | check 0 (read as calls); run: "Otter could not find anything called \"break\"" |
| `if x is empty` (example in SPEC-DECISIONS D37) | check 0; run: "Otter could not find the variable \"empty\"" |
| `if xs` with an empty list | prints the `otherwise` branch (empty list is falsy) |
| `say f 3 minus 1` | valid; prints 2 (`(f 3) minus 1`) |


### Files, folders and data formats (section 14, `samples/syntax/files_data/`)


All samples are in `docs/web/samples/syntax/files_data/` and were run from that folder with `perl -e 'alarm 20; exec @ARGV' $B check|run <file>`.

1. **`files_folders.ot`**
   - Covers: create folder; set current directory (in and back out); get current directory; write; append; write atomically; read; `file exists`; `file is locked`; copy file (creates parent folders); move file into a folder; get files; get files and subfolders; file object name/extension/size; get folders; copy folder; move folder; zip folder; unzip; delete file; delete folder.
   - check: exit 0 ("files_folders.ot is valid.")
   - run: exit 0. Output: `Hello world`, `a exists`, `a not locked`, `2`, three file rows (`a-copy.txt .txt 11`, `b.txt .txt 2`, `d.txt .txt 4`), `sub`, `deep`, `a deleted`, `done`.
2. **`data_formats.ot`**
   - Covers: convert to json; read json from; convert from json; read csv from; write csv; convert to csv; convert from csv; bytes from text/hex/base64; hex/base64/text from bytes; empty bytes; length of bytes; write bytes to file (plain and atomically); bytes from file.
   - check: exit 0
   - run: exit 0. Output: PowerShell-5.1-style JSON (`"name":  "Ann"`), `Ann`, `1, 2`, `Fort, Worth`, the CSV text with `Bo,"Fort, Worth"`, `2`, `4869`, `SGk=`, `Hi`, `Hi`, `0`, `2`.
3. **`xml_sample.ot`**
   - Covers: xml with root; root of; add element ... and call it; add element with text; set attribute; pretty text from xml; text of X in Y; attribute X of E; has attribute; element exists in; elements in; child N in; children N in; name of; element X in; set text of E; set text of X in Y; attributes of; remove attribute; write xml to file; xml from file; text from xml; xml from text; remove element.
   - check: exit 0
   - run: exit 0. Output: the pretty XML, `Otter`, `1`, `has id`, `book exists`, `1`, `book`, `1`, `id`, `<library><book><title>Again</title></book></library>`, `<a></a>`.
4. **`doc_forms.ot`** (negative): `write "x" to file "f.txt"`, the form in SPECIFICATION.md §13.
   - check: exit 2, "I expected the write statement to end here. I found '"f.txt"' instead."
   - not run.
5. **`probes/p*.ot`**: one-line checks of doc-only or edge forms. Results:
   - Rejected by the parser (Otter Syntax Error):
     - `read file "f.txt" into t` ("I expected \"into\" ...")
     - `convert x to json` with no `into` ("I expected \"into\" ...")
     - `copy folder to "Backup"` where `folder` is a loop variable ("I expected a function name.")
     - `delete folder "x" and everything in it`
     - `delete "a.txt"`
     - `get files in "." recursively into all`
     - `copy ... atomically` and `append ... atomically`
     - `write xml d to "f.xml"` and `write bytes b to "y.bin"` (no `file`)
     - `convert x to yaml into y`
     - `e is file "p1.ot" exists` ("I expected the assignment to end here")
     - `get files in "."` with `and subfolders into all` on an indented next line
   - Accepted:
     - `get files in "." and subfolders` with `into all` on an indented next line
     - `get current folder into` / `set current folder to`
     - `csv`, `json`, `xml` and `bytes` used as ordinary variable names
     - `not file "x" exists` and `while file "x" exists`
   - `measure folder "x" into size` passes `check` but `run` exits 3 with "Otter could not find anything called \"measure\"": it is parsed as a call to a user function.


### System (section 15, `samples/syntax/system/`)


All samples are in `$SCR/samples/system/`, where `$SCR` = `docs/web/samples/syntax`. Each ran under `perl -e 'alarm N; exec @ARGV' $B ...`.

- `safe_run.ot`. Covers: get environment variable (set and unset), `run command "echo hi" into result` with `output of` / `error output of` / `exit code of`, set random seed, random number, random item, start timer, `wait 1 second`, `wait 20 milliseconds`, elapsed milliseconds and elapsed time of, hash, hash with key, encrypt and decrypt with key, `sha256 of`, `hmac sha256 of ... using`, secure random bytes, generate encryption key, encrypt and decrypt `using ... and call it`, securely equals, hash password, password matches hash, `say ... in color`, `console is interactive`, show progress.
  - **check:** exit 0, "Otter: safe_run.ot is valid."
  - **run** (stdin `/dev/null`): exit 0. Output: `[ hi ] exit 0`, `seeded number 5`, `seeded item c`, waited at least a second, `hash ... as "sha256"` -> `ba7816bf...15ad` (lowercase), HMAC `342e519c...78ee`, `round trip`, `hex from bytes sha256 of` -> `BA7816BF...15AD` (uppercase), HMAC bytes `9C196E32...A6AB`, 16, 32, `bytes round trip`, `password verified`, `Colored` (no escape codes when redirected), `not interactive`, progress bars joined by `\r`, `done`.
- `check_only_system.ot` (**never run**). Covers: set environment variable, get system folder, get and set current directory, get system information, get arguments, get processes, `run "..." into p`, `run "notepad.exe"`, set priority, wait for process (with and without `into`), kill process (with and without `and its children`), `run command` without `into`, `on remote ... using credential`, `over ssh to`, the clipboard copy and get forms, notify, the 3 file dialogs, the 4 registry forms, event log, the 3 credential forms, the 4 secret forms, print, the 3 symbolic-link forms, get owner, is read only, set to read only and writable, set cursor, choose from, ask secretly, `lock the computer`, `sign out`, `restart the computer`, `restart computer`, `shut down the computer`.
  - **check:** exit 0, "Otter: check_only_system.ot is valid."
  - **run:** not run, by design.
- `negative_forms.ot` (check only). Ten forms that should be rejected: `wait 5`, `restart the machine`, `kill foo`, `random thing ...`, `in colour`, `show progress 50`, `set cursor to 1 1`, `hash "x" into digest`, `over sh`, `get system info`.
  - **check:** exit 2, "Found 12 errors in source." Each line got the expected error, but `set cursor to 1 1` produced 3 errors: the expected `I expected "row" after "to".` plus two `I expected "make"...` recovery errors.


### Network (section 16, `samples/syntax/network/`)


Each sample was checked with `perl -e 'alarm 20; exec @ARGV' $B check FILE`. I ran `check` only and never `run`. The samples are under `$SCR/samples/network/`.

| File | Constructs covered | check |
|---|---|---|
| `http_client.ot` | All the synchronous get/post/put/delete forms, including the forms without `into`; an options block on get and on post (header, cookies, without cookies, following/without redirects, timeout); download; `start get from` (with `as json` and options), `start post` (with and without `as json`), `start put`, `start delete`; `on complete/error/cancel of`; `received response`; status/response/state/error of; `is pending`, `is not completed`, `is not failed`, `is cancelled`, `is running`; `cancel` | exit 0, "http_client.ot is valid." |
| `sockets.ot` | websocket connect (with and without protocol), send, close (bare, and with code and reason), the four handlers, all 5 ambient values, the 4 states, url/protocol/state of; tcp listen (with and without `on ADDRESS`), `on connection to`, `incoming connection`, `on data from`, `received data`, send bytes, close tcp, is listening/stopped, local address/port; connect, connect securely with `for server`, connect securely with indented clauses and `using protocols`, on connect/error, `network error`, is connected/secure, remote/local/tls properties, stop tcp; udp open (with and without port), on data from, sender address/port, `count of received data`, send to/on port, close udp | exit 0, "sockets.ot is valid." |
| `watch_jobs.ot` | watch file, watch folder, watch folder recursively; on change of / create in / delete in / rename in; changed path / changed file name / change kind / old path; is watching; stop watching; start command; on output from / error output from / exit of / complete of / cancel of; received output / received error output; exit code/state/output/error output/id of; job is running/completed/failed/cancelled; cancel job; `while not done` + `wait 100 milliseconds` | exit 0, "watch_jobs.ot is valid." (the first version, with `command of job`, gave exit 2; see probes) |
| `server.ot` | web server object with port and host; routes using GET with `as json`, POST with `into` and `as json and status 201`, `a request`, the default method (`receives at`), DELETE with `respond with status 204`; `start api` | exit 0, "server.ot is valid." (the first version, with `respond with "Not Found" and status 404`, gave exit 2; see probes) |

Probes (`$SCR/samples/network/probes/`) are one-line checks of disagreements between the docs and the parser. Each was checked once; the first five are the expected-failure probes.

| File | Result |
|---|---|
| `start_get_no_from.ot` (`start get "url" and call it r`, written as in D116B §1) | exit 2: `I expected "from" and a URL after "start get".` |
| `respond_with_status.ot` (`respond with "x" with status 201`, the D51 §4 alternative) | exit 2: "I expected the respond statement to end here. I found 'with' instead." |
| `respond_and_status_plain.ot` (`respond with "Not Found" and status 404`, from r2 §20 and D51) | exit 2: "I expected the respond statement to end here. I found '404' instead." |
| `command_of_job.ot` (`say command of x`) | exit 2: "I expected a value here." (`command` lexes as a keyword) |
| `listen_securely.ot` | exit 2: `"listen securely for tcp" is reserved for TLS servers...` (this is the documented behaviour) |
| `udp_send_partial.ot` (`send "x" through u to "127.0.0.1"`) | exit 2: `A udp send needs both "to <host>" and "on port <port>".` (also documented behaviour) |

No sample was `run`, because every sample touches the network, a server, a watcher or a job.


### UI and databases (section 17, `samples/syntax/ui_data/`)


All are in `docs/web/samples/syntax/ui_data/`. Each was run under `perl -e 'alarm 20; exec @ARGV' $B …`.

- `ui_resources.ot`: `create the … into the …`, `the PROP of X is …`, comma `put`, `when X is clicked`, `when app is closed`, `show`, a declarative `page` with layout/gap/align/`columns … on`/`stack on`, a card with spread/round/background/padding/`width full`, variants (primary/secondary/danger), hover/enter/leave animations (grow, fade, move, slide, animate ms/s with easing), a `click` event block, `input NAME`, grid/panel/section/link/image/sidebar/main. **check: exit 0 ("is valid").** run: not done (UI).
- `ui_routes_state.ot`: state/derive/`when … changes`, page resources, `route … shows`, `route otherwise`, `go to`/`go back`/`go forward`, `replace route with`, `on route change` with current route / route parameter / query parameter, `accepts drops`/`draggable`, `on drag of`, `set drag data`, `on drop on`, drag data/drop x/drop y/dragged item, `on files dropped on`/dropped files, await (statement, value, make, into), memo, shared, on start, on close, focus, hide. **check: exit 0.** The first version failed with exit 2 because `count is count plus 1` at top level is reserved ("'count' is a reserved statement keyword…", RW l.254); it was renamed to `clicks`. Note that `check` accepts every 1.0-refused form: the refusals happen only at runtime. run: not done (UI).
- `db_query.ot`: config + connect (SQLite file inside the sample folder), execute with/without parameters and `into`, `the rows affected of`, transactions (commit and rollback), block `query` with a parameter, get tables/columns, D99 `get name and price … order by … descending`, `get distinct`, `is in` + `is between`, `is not in` + `take`/`skip`, every aggregate form (count all, sum with where, total, average, minimum, max), disconnect, `delete file`. **check: exit 0. run: exit 0.** Output: `rows affected: 1 last id: 1`; query: Backpack, Notebook, Pen; tables: 1; columns: 4; first sorted: Backpack; categories: 3; picked: 2; others: 1; `4 5.5 31.25 7.8125 0.75 25`; done. The `.db` file was removed afterwards. The first version failed with exit 2: `into a` is not allowed because `a` is the article, so the target was renamed `avgPrice`.
- `edge_expected_errors.ot` (expected failures): **check: exit 2** with "I expected the statement to end here. I found 'is in' instead." (`if 2 is in numbers` outside a query) and "I expected \"shows\" and a page after the route path." (`route "/a"`). `get all … first 3 into rows` and `go home` passed check. A separate scratch run showed `first 3` limiting 5 rows to 3 (run exit 0) and `go home` failing at runtime with "Otter could not find anything called \"go\"." (exit 3).


## Coverage gaps

### Cross-cutting findings

- **The documents disagree with each other.** `docs/SPECIFICATION.md` (marked "Frozen & Authoritative") describes syntax that neither implementation accepts: `to NAME with a, b`, `make EXPR` as a return, `f with 10, 20` calls, `player's name`, one-line `a User has name, email`, `ask ... into`, `read file ... into`, `write ... to file`, `convert ... to json` with no `into`, operator precedence with parentheses and `modulo`, and a mandatory closing period. All were rejected by `otter check` (see the core probes). `GRAMMAR.md`, `SEMANTICS.md`, SPEC-DECISIONS and the tests agree with the implementation. Treat SPECIFICATION.md as non-normative until it is corrected.
- **GRAMMAR.md covers only the core.** It names `FileStmt`, `HttpStmt` and `UiStmt` without productions, and has no productions for system, crypto, console UX, network, watch, job, server, UI, routing, database or query statements. Several core forms are also missing from its EBNF (`fail with`, `increase`/`decrease`, `sort`/`reverse`/`replace`/`split`/`join`/`find`, dynamic `get`/`set`, date adjust/difference, bare `each`, `into` call capture, `date from`, D89/D90 math words). Everything outside the core is defined only by SPEC-DECISIONS prose plus the parser.
- **The INVENTORY is outdated:** it calls `use` unsupported (MODULE_STATUS.md certifies it and otter-net runs it), calls HTTP web-only (D116A/B and STANDARD_LIBRARY.md make console HTTP part of 1.0), and has no rows for D94, D95, D100-D113, D115 or D119.
- **Scope labels vs reality:** D97/D98 databases and D99 queries are labelled deferred (1.1 / 1.2+) yet parse and run in 1.0.1 (SQ Q10, Q11).
- **`otter check` accepts forms that 1.0 refuses at run time** (`memo`, `shared`, `on start/close`, `focus`, `hide`, `await`, routes, drag and drop), and words like `break`, `skip`, `measure` or `go home` that silently parse as calls to undefined functions, so they fail only at `run`.
- **Behaviour is Windows-only for many system statements** (notify, dialogs, registry, event log, credentials, power, print, some system information); on macOS only their error paths could be exercised.

### Core language (sections 1-13)


- **SPECIFICATION.md disagrees with GRAMMAR.md and with both implementations** in many places, although it calls itself "Frozen & Authoritative": `to NAME with a, b` parameters, `make EXPR` as return, `area is f with 10, 20`, `player's name`, one-line `a User has name, email, score`, `ask <prompt> into <var>`, `read file <path> into`, `write ... to file`, `convert ... to json` without `into`, precedence with parentheses and `modulo` (§6.1), and a period that MUST end every block (§3.2; in practice it is optional). All were rejected by `otter check` (probes above). GRAMMAR.md, SEMANTICS.md and the tests match the implementation.
- SPECIFICATION.md §2.4's reserved list (e.g. `item`, `list`, `number`, `text`, `thing`, `else`, `check`) does not match OTTER_1_0_RESERVED_WORDS.md (D124); `item` is explicitly not reserved (Grammar.Tests.ps1 "item is NOT a reserved word"). SPECIFICATION.md §2.5 says names are case-insensitive; GRAMMAR.md §1.4 says case-sensitive.
- SPECIFICATION.md §5.3 says loop variables are scoped to the loop body; SPEC-DECISIONS D28 says control-flow blocks create no scope (only `find`'s item is temporary).
- GRAMMAR.md's `Statement` list is not complete: `fail with`, `increase`/`decrease`, `sort`/`reverse`/`replace`/`split`/`join`/`find`, `get`/`set` keys, date adjust/between, bare `each`, `not` values, `x is not v`, `date from`, the D89/D90 math words and `stop` inside handlers are in SPEC-DECISIONS and the parser but not in the EBNF. Its `CallStmt` omits the `into` capture that the parser accepts.
- INVENTORY (OTTER_1_0_LANGUAGE_AND_RUNTIME_INVENTORY.md) A11 says `use` is "EXPLICITLY NOT SUPPORTED IN V1"; this is outdated (OTTER_1_0_MODULE_STATUS.md certifies it and otter-net runs it).
- SPEC-DECISIONS D37's example `if name is empty` does not work: `empty` is only a keyword after `are`, so it is read as an undefined variable.
- D9 lists "undefined variable -> runtime error" while SPECIFICATION.md §10.1 and SEMANTICS.md §2 list only the falsy values; no contradiction in behaviour, but documents differ in coverage.
- No tests/compat program covers D38B condition continuation or a user-function call captured with `into`. `percent of`, `power`, `ask secretly`, `stop`, `natural log`, the trig words, `larger`/`smaller` and `increase`/`decrease` each have only one or two compat programs.
- `break`/`continue`/`skip` do not exist (D37 defers them); they are read as function calls and fail at run time, not at check time.

### Files, folders and data formats (section 14)


- **SPECIFICATION.md §13 shows forms the parser rejects** (otterPS and otter-net parse these the same way):
  - `read file <path> into <var>` and `write <expr> to file <path>` are rejected. The real forms are `read P into N` and `write X to P`, as in rules.md and Inventory B1.
  - `convert <expr> to json` and `convert <expr> from json` with no `into N` are also rejected.
- **GRAMMAR.md lists `FileExistsExpr` under `PrimaryExpr`** (l.243, l.251), but both implementations accept it only as a condition (otterPS `Read-OtterConditionPrimary`, Otter.Parser.psm1 l.1097-1140; otter-net TryReadSystemCondition). `e is file "x" exists` is a syntax error.
- **GRAMMAR.md names `FileStmt`** (l.99) but never defines it. The header says library statements are documented elsewhere.
- **D105 says `child/children N in X` select "by position"**, but `children N in X` ignores N and returns every child element, in both implementations. N is still required by the grammar. No document explains this.
- **rules3.md §12-13 show `copy folder to "Backup"` / `move folder to "Archive"`** with a folder object called `folder`. This fails because `folder` is a keyword. A folder object only works with another name (`copy folder f to ...`).
- **Designed but deferred; no implementation:**
  - `delete folder ... and everything in it`: a syntax error (SD l.847, l.1025; rules3 §15; rules4 l.293).
  - `measure folder ... into size` (rules3 §10, SD l.851): not a construct. It parses as a call to an undefined function.
  - `sort files by name` (SD l.1025, rules3 l.741).
- **File object properties are documented inconsistently.** D20's table has name/extension/size/path. rules3 §8 and the runtime also have `created`/`modified`, which are `gone` for a missing file.
- **`atomically` is accepted only on `write` and `write bytes`**, not on `append`, `copy` or `write xml`. Separately, `write xml` and `write bytes` require `to file P`, while plain `write` takes `to P`. These asymmetries are real but are not written down in one place.
- **The otterPS inventory is incomplete for this area.** Inventory Part B has no rows for CSV, XML or bytes, and STANDARD_LIBRARY.md only says "byte conversion and file-byte operations". So the authority for those three areas is SPEC-DECISIONS D95/D102/D105/D115 alone, plus the Pester suites.
- **Conformance coverage is thin.** Only one manifest case (`file-json-command`) touches this area: write, read json, convert to json, delete file. There is no conformance case for append, copy/move, folders, discovery, zip, bytes, XML or CSV.
  - `conformance/misc/file_exists_run_command_json_roundtrip.ot` and `conformance/json_random_diagnostics/json_random_diagnostics.ot` are not listed in manifest.json.
  - The misc file also hard-codes a Windows path (`C:/Users/jmacy/...`).
- **`file P is locked` is only tested in the negative in otter-net** (files.ot prints "not locked"). The positive case (a file held open) is tested only in otterPS Part3.Tests.
- **otter-net merges some otterPS AST nodes.** This changes no syntax.
  - `TransferStmt(FileAction)` stands in for `CopyFileStmt`, `MoveFileStmt`, `CopyFolderStmt`, `MoveFolderStmt`, `ZipFolderStmt` and `UnzipFileStmt`.
  - `GetEntriesStmt` stands in for `GetFilesStmt` and `GetFoldersStmt`.
  - `ConvertJsonStmt` stands in for `ConvertToJsonStmt` and `ConvertFromJsonStmt`; `ConvertCsvStmt` does the same for the two CSV nodes.
  - Docs that cite node names (D95's AST table) match otterPS, not otter-net.
- **Keyword status is not stated precisely.** D95 says `csv` is "contextual". In both lexers `csv` and `json` are tokens, yet as names they still work (`csv is "a"` passes check). Neither `csv`, `json`, `xml` nor `bytes` is fully reserved.
- **The `zip` archive is not byte-identical across hosts.** zip.ot notes that the compressed bytes differ between .NET Framework and .NET 10; only the extracted content is part of the contract.

### System (section 15)


- **No test for some literal forms.** `restart the computer` with `the` does not appear in any compat test (sys_windows.ot uses `restart computer`); the parser accepts both. No otterPS Pester grep hit for `set priority of process`, `shut down the computer`, `print "...`, or `notify "...`, although INV cites "D70/D71/D82/D83 regression suites".
- **Off-Windows paths only on this Mac.** On macOS, the compat tests reach only the error paths of notify, file dialogs, registry, event log, credentials, secrets, owner, power and print (`NotOnWindows`, `Interpreter.System.cs` l.362-367). Their success paths are verified only by Windows runs.
- **GRAMMAR.md is incomplete for `run`.** GR l.152 `RunStmt` omits the `on remote ... using credential` (D84) and `over ssh to` (D85) suffixes. GR also has no productions for any of the system, crypto or console statements; those are only in SD and the inventory.
- **Inventory is out of date.** INV covers D30, D65-D92 but has no rows for D100 (console UX), D101 (wait, timers, seed), D109 (bytes crypto, password hashing), D111 (secret vault) or D94 (set environment variable, current directory, arguments). A spec built from INV alone would miss about 25 constructs.
- **`run` vs `run command` result shapes differ** (INV B4 calls this out). `run "x" into p` gives a process handle; only `run command` gives `output`/`error output`/`exit code`. The syntax gives no hint of this.
- **Hex casing is inconsistent.** The D91 `hash` statement prints lowercase hex. D102 `hex from bytes` (used with D109 digests) prints uppercase. Both are verified in safe_run.ot.
- **D100 design names vs otter-net.** D100doc proposes `SetCursorPositionStmt` and a `TokenKind::Console`; otter-net uses `SetCursorStmt` and a single lexer token `ConsoleInteractive` for the whole phrase "console is interactive". Behaviour matches.
- **D100doc says a labelled progress bar is "deliberately not included".** `show progress "X" at 50 percent` must not be documented.
- **SPEC-QUESTIONS open items:**
  - Q7: registry `HKCR:`/`HKU:`/`HKCC:` are accepted by the path check but fail or return `gone`.
  - Q8: on macOS/Linux, the Windows-only system information kinds give empty or partial values instead of the D129 message, and ssh looks for `ssh.exe`.
  - Q9: `kill process p and its children` kills the whole tree on all platforms in otter-net, but only the parent in otterPS on PowerShell 7 off Windows.
  - P2-1: a remote command off Windows ends in an internal error in otterPS (copied).
  - P2-2: event log names act as wildcards.
  - P2-3: events with no description have `message` gone.
- **Notify test comment is wrong.** The comment in sys_print_notify.ot says notify and print "run natively on every platform", but otter-net's interpreter throws the Windows-only error for both off Windows (`Interpreter.System.cs` l.153, l.292).
- **Units are fixed in two forms.** `wait for process ... up to N seconds` only accepts `seconds` (sys_syntax_errors.ot `up to 5 minutes`), while the general `wait` takes any time unit.
- **Secret expression parses narrowly.** `secret NAME` with a variable is recognized only when the name ends the line or is followed by `exists` (`TryReadSystemValue`). `say secret name "x"` and similar mid-line uses fall back to treating `secret` as an ordinary name.
- **Web target limits.** D100 console UX and D101 `wait` / `set random seed` are refused for web (tests/cli ui.psd1 checks `in color`). D111 vault is console on Windows only. D109 crypto is supported on web via D114 (the contract comment saying otherwise is stale).
- **Jobs left out of scope.** D119 command jobs (`start command ... and call it JOB`, `on output from JOB`, `cancel JOB`, and so on) touch processes but belong to the jobs/network group. Only the D119-R1 script dispatch for `run command` is noted here.
- **Parser recovery noise.** A malformed `set cursor to 1 1` reports 3 errors for one line: the right one plus 2 cascading `I expected "make"` errors (negative_forms.ot).
- **The conformance manifest barely covers this area.** Only `file-json-command` (`run command` + `output of` / `exit code of`) is in the manifest. `system_integration/clipboard_env_notify.ot`, `json_random_diagnostics.ot` and `misc/file_exists_run_command_json_roundtrip.ot` exist but are not listed in `manifest.json`.

### Network (section 16)


- **`start get` spelling.** D116B §1 lists `start get <url> and call it <target>`, but both parsers (otterPS `Otter.Parser.psm1` l.3260 and otter-net `ReadHttpStart`) require `start get from <url>`. Any `as json` goes after the URL. The PS tests use the `from` form, so the D116B text is wrong.
- **`respond with V and status N` does not parse unless `as json` comes before `and status`.** The value is read with `ReadMathExpression`, where `and` is the D34 arithmetic `plus`. The parser therefore reads `V and status` as one addition, then stops at `N`. This breaks the D51 and r2 §20 examples (`respond with "Not Found" and status 404`). Every test uses `as json and status`. otterPS probably behaves the same, since otter-net ports its expression reader, but I could not confirm this here.
- **`respond with V with status N` is never accepted** (SPEC-QUESTIONS Q14), although D51 §4 documents it.
- **`otter serve` never runs route bodies** (UI2-2). Only the first literal `respond` value and status are used, and `into REQ` is never bound. D51 §3 promises a request object with `method`, `path`, `query`, `body` and `headers`, and r2 §13-14 shows `body of request as json`. None of that has any runtime meaning.
- **D51 lists the verbs GET, POST, PUT, DELETE, PATCH, OPTIONS and HEAD, but the parser accepts any word** and upper-cases it. No test covers a route using PUT, DELETE or PATCH.
- **`command of JOB` cannot be written.** D119-R2 and the runtime's own error text ("Try state, exit code, output, error output, id, or command") offer it, but `command` lexes as a keyword and the parser says "I expected a value here". No test anywhere covers it.
- **D119-R2 lists `JOB is running|completed|failed`, but `cancelled` and `pending` also work** (c/net_jobs.ot uses `is cancelled`). For requests, `running` is accepted as a synonym of `pending`. D116B does not mention `running`.
- **Network handlers set up inside a loop do not keep the loop pass** (Q13), contrary to the general wording of D130. File-watcher handlers do keep it.
- **GRAMMAR.md names `HttpStmt` (l.100) but never defines it.** None of the network, watch, job or server productions are in the formal grammar. STANDARD_LIBRARY.md points to rules.md and SPEC-DECISIONS.md instead.
- **Stale documents.** `OTTER_1_0_HTTP_TARGET_PARITY.md` (marked superseded) and the inventory's §C1 (l.406-438, "web-target-only") both contradict D116A/B and the D116 affirmation. The PARITY doc also names `HttpPostStmt` and `HttpPutStmt`; otter-net has a single `HttpSendStmt(Method)` node.
- **D116B §4 says `response of` is gone when the request is not completed.** otter-net, like otterPS's runtime as ported, raises an error instead ("The HTTP request has not completed yet." / "was cancelled." / "failed: ..."); c/net_http_requests.ot checks this. D116B says `status of` is gone if the request failed; the runtime returns a number.
- **r2 aspirational forms that are not implemented anywhere:** `send GET to "/users" [with X]` (§12); `connect to "host" on port N as connection`, `message is received from connection`, `close connection` (§23); `when request arrives` (§24); the port utilities `port N is in use` and `find an open port from A to B into P` (§11).
- **`listen securely for tcp` is reserved but not implemented.** It is a hard syntax error and appears in no decision text.
- **Constructs with no otter-net test:** `on rename in` (PS FileWatching only); `W is closing` and `using protocols` (PS Parser.Tests only); `C is connected` (PS Network and EventContract tests only); `post DATA to URL` without `into`; `close websocket W and reason R` without a code; routes using PUT, DELETE or PATCH; the continuation form `get URL` / indented `into X`; `start post DATA as json` in a valid program (only an error form exists).
- **Platform-dependent message text** (Q15) and **behaviour that differs between 5.1 and 7** (Q16): ALPN `using protocol` is refused, `post gone as json` behaves differently, and Unix-path URLs differ. A documentation page should state only the Otter-level prefix of these messages.
- **`await` is in this area only as a parsed-but-unsupported form** (D56 audit). It is parsed in `Parser.Ui.cs`, not in Parser.Network.cs.

### UI and databases (section 17)


- **No grammar for this whole area.** GRAMMAR.md stops at `UiStmt` (l.104) and has no productions for UI, routing, databases or queries. Every form above is defined only by SPEC-DECISIONS prose, examples and the parser.
- **Databases: scope docs disagree.** The release scope matrix (l.26) says "Database / SQL Integration — DEFERRED … to 1.2+". D97 and D98 say "Otter 1.1 introduces …". Yet D97/D98 parse and run in 1.0.1 (otter-net also runs them on macOS/Linux with the system SQLite; otterPS on PowerShell 7 off Windows fails with an internal error, SQ Q11). D99 is "DEFERRED from 1.0" yet parses and runs (SQ Q10). The docs need to decide whether these are documented as 1.0 surface.
- **`first N` limit.** SQ Q10 says `first N` never matches (lexed as `first of`) and the query asks for `into`. The current otter-net lexer makes `first` a keyword only before `of` (Lexer.cs l.360), and the published binary accepts `get all … first 3 into rows` and limits correctly. Q10 is stale, or otter-net now differs from otterPS. This needs re-checking against otterPS.
- **D99 design vs implementation.** The design doc's fixtures `get customers from customers in db` (table name as "all"), `get count of X from …` (an aggregate inside `get`), `group by` (Fixture F), joins / `even when none` (G/H) and `get first …` returning one row are **not** implemented. Only the "What exists" list in SD D99 matches. A qualified column (`name of t`) in a projection, `order by` or aggregate produces an empty column name (`no such column`, SQ Q10). `is in`/`is between` exist only inside `where`.
- **D56 vs implementation on UI event/animation blocks.** D56 and INV A12 say `UiEvent`/`UiAnimation` are "now a hard error" (the counter.ot `click` handlers "do not work"). In both implementations these nodes live only inside `UiElementStmt.Events/Animations`, which `otter run` never executes, so they are silently ignored. The interpreter's refusal cases (IUi l.101-104) are unreachable from parsed programs (SQ UI2-5).
- **`otter check` accepts every 1.0-refused form** (`memo`, `shared`, `on start/close`, `focus`, `hide`, `await`, routes, drag and drop). They fail only at `run`, with two different message styles: "'X' is not supported in Otter 1.0." versus "I do not know how to run a Route statement yet." / "I do not know how to work out a CurrentRoute value yet." The route forms use the generic "not yet" text, while D103 says the console "fail[s] with an Otter error".
- **`await` statement vs expression messages differ.** `await delay 250` gives "I do not know how to run a Await statement yet." (note the grammar "a Await"), while `x is await f` / `await f make x` give "'await' is not supported in Otter 1.0." INV A12 flags that the `make`/`into` forms were uncommitted work at the time of the audit and need their own V1 decision.
- **Tutorial uses unsupported syntax.** otterPS docs/TUTORIALS.md §4 uses `counterLabel.text is …` (dot member access), which the scope matrix lists as UNSUPPORTED. It also declares `app is a window with …`, which on `otter run` builds a plain thing and opens no window.
- **STUDIO.md toolbox kinds** (`label`, `checkbox`, `container`) are not in the WPF provider's `KindNames` (button, card, column, danger button, grid, heading, panel, primary button, row, scroll, secondary button, text, text box, window). `create label into x` parses but is refused at runtime.
- **The declarative tag list and `create` kinds differ.** Element tags include `page section sidebar main link image input`, which `create` (WPF) does not know, while `create` knows `row column scroll text box`, which are not element tags. Variants apply to 8 tags (not window/page/section/sidebar/main/grid).
- **`hover`/`press` are listed both as animation triggers and as event words** (PUi l.507/514). Animation is checked first, so a `hover` event block can never be written.
- **The UI spacing vocabularies overlap.** `gap` (D56) vs `spacing` (D48), and three separate `align` implementations (SD D56 scope note). For the docs, pick one.
- **`query parameter` only as a value.** A line that starts with `query parameter …` is read as the D97 `query` statement (TokenKind.Query dispatch in PData l.37).
- **Constructs with thin or no direct tests:** `query parameter` has only a runtime-error test (ui_run_query_param) and no web test in otter-net; `go forward` and `replace route` appear only in web/routing.ot plus the run-error tests; `input NAME`, `sidebar`/`main` tags and the `margin`/`placeholder` properties have no dedicated test (only the web declarative pages, and `sidebar`/`main` only through RW and this audit's sample); `on files dropped on` appears only in web/dragdrop_board.ot; `total of` vs `sum of` on an empty table has no test of the 0.0-vs-gone difference; `readonly` connections appear only in data_db.
- **Web refuses databases** (web/t3_err_db.ot): D97/D98/D99 are console-only, which no D-entry states explicitly.
- **Reserved-word traps that matter for docs examples:** a top-level `count is …` is refused (use `state count is 0` or another name); `a` cannot be an `into` target. Every UI tag word (`text`, `page`, `main`, `image`, `input`, `link`, …) cannot start a line as a call or function name (RW l.168-181).
