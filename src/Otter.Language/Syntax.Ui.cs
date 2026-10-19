namespace Otter.Language;

// Syntax nodes for user interfaces (see docs/PORTING.md): otterPS's D44-D47
// resources and events, D56 declarative UI and reactivity, D103 routing and
// D110 drag and drop. Named and shaped as in otterPS's contract; the field
// order is the contract's, because otterPS walks nodes field by field.

// A line that otterPS's parser turns into several statements
// (`put a, b in app` is one `put` per item). Never part of a finished tree:
// the statement list it lands in takes its statements instead.
public sealed class StatementGroup(IReadOnlyList<Stmt> statements, int line) : Stmt(line)
{
    public IReadOnlyList<Stmt> Statements { get; } = statements;
}

// create button into helloButton (D44)
public sealed class CreateUiResourceStmt(string typeName, string target, int line) : Stmt(line)
{
    public string TypeName { get; } = typeName;
    public string Target { get; } = target;
}

// when helloButton is clicked (D46); also `on drag of x`, `on drop on x`,
// `on files dropped on x` (D110), whose EventName is drag/drop/files dropped.
public sealed class WhenStmt(Expr target, string eventName, IReadOnlyList<Stmt> body, int line) : Stmt(line)
{
    public Expr Target { get; } = target;
    public string EventName { get; } = eventName;
    public IReadOnlyList<Stmt> Body { get; } = body;
}

// put helloButton in app (D47)
public sealed class PutInStmt(Expr item, Expr container, int line) : Stmt(line)
{
    public Expr Item { get; } = item;
    public Expr Container { get; } = container;
}

// show app (D47)
public sealed class ShowStmt(Expr target, int line) : Stmt(line) { public Expr Target { get; } = target; }

// ---- declarative UI (D56) -------------------------------------------------

// columns 2 on medium / stack on small
public sealed class ResponsiveRule(string breakpoint, int columns, bool stack)
{
    public string Breakpoint { get; } = breakpoint;
    public int Columns { get; } = columns;
    public bool Stack { get; } = stack;
}

public sealed class UiLayoutSpec
{
    public string? Mode { get; set; }
    public string? Align { get; set; }
    public bool Spread { get; set; }
    public Expr? Gap { get; set; }
    public Expr? Columns { get; set; }
    public List<ResponsiveRule> Responsive { get; } = new();
}

// fade in / move up 20 / slide from left / grow 1.1
public sealed class UiAnimationStep(string operation, string? direction, Expr? amount)
{
    public string Operation { get; } = operation;
    public string? Direction { get; } = direction;
    public Expr? Amount { get; } = amount;
}

// hover / press / enter / leave blocks inside a UI element
public sealed class UiAnimationBlock(string trigger, IReadOnlyList<UiAnimationStep> steps, double durationMs, string easing, int line) : Stmt(line)
{
    public string Trigger { get; } = trigger;
    public IReadOnlyList<UiAnimationStep> Steps { get; } = steps;
    public double DurationMs { get; } = durationMs;
    public string Easing { get; } = easing;
}

// click / change / input ... blocks inside a UI element
public sealed class UiEventStmt(string eventName, IReadOnlyList<Stmt> body, int line) : Stmt(line)
{
    public string EventName { get; } = eventName;
    public IReadOnlyList<Stmt> Body { get; } = body;
}

// card "Title" / primary button "Go" as ... / layout grid 3
public sealed class UiElementStmt(string tag, string? variant, Expr? label, string? name, UiLayoutSpec layout,
    IReadOnlyList<Stmt> properties, IReadOnlyList<Stmt> events, IReadOnlyList<Stmt> animations, IReadOnlyList<Stmt> children, int line) : Stmt(line)
{
    public string Tag { get; } = tag;
    public string? Variant { get; } = variant;
    public Expr? Label { get; } = label;
    public string? Name { get; } = name;
    public UiLayoutSpec Layout { get; } = layout;
    public IReadOnlyList<Stmt> Properties { get; } = properties;
    public IReadOnlyList<Stmt> Events { get; } = events;
    public IReadOnlyList<Stmt> Animations { get; } = animations;
    public IReadOnlyList<Stmt> Children { get; } = children;
}

// state count is 0
public sealed class StateDefStmt(string name, Expr initialValue, int line) : Stmt(line)
{
    public string Name { get; } = name;
    public Expr InitialValue { get; } = initialValue;
}

// derive doubled is count times 2
public sealed class DeriveDefStmt(string name, Expr expression, int line) : Stmt(line)
{
    public string Name { get; } = name;
    public Expr Expression { get; } = expression;
}

// memo name (block) - refused in Otter 1.0
public sealed class MemoDefStmt(string name, IReadOnlyList<Stmt> body, int line) : Stmt(line)
{
    public string Name { get; } = name;
    public IReadOnlyList<Stmt> Body { get; } = body;
}

// when count changes
public sealed class WatchStmt(string targetName, IReadOnlyList<Stmt> body, int line) : Stmt(line)
{
    public string TargetName { get; } = targetName;
    public IReadOnlyList<Stmt> Body { get; } = body;
}

// on start / on close - refused in Otter 1.0
public sealed class LifecycleStmt(string stage, IReadOnlyList<Stmt> body, int line) : Stmt(line)
{
    public string Stage { get; } = stage;
    public IReadOnlyList<Stmt> Body { get; } = body;
}

// shared theme is "dark" - refused in Otter 1.0
public sealed class SharedStateStmt(string name, Expr initialValue, int line) : Stmt(line)
{
    public string Name { get; } = name;
    public Expr InitialValue { get; } = initialValue;
}

// focus box / hide panel - refused in Otter 1.0
public sealed class UiActionStmt(string action, Expr target, int line) : Stmt(line)
{
    public string Action { get; } = action;
    public Expr Target { get; } = target;
}

// await delay 250 / x is await fetchData (D56). otterPS has one node kind
// for both; otter-net needs an expression and a statement.
public sealed class AwaitExpr(Expr expression, int line) : Expr(line) { public Expr Expression { get; } = expression; }

public sealed class AwaitStmt(Expr expression, int line) : Stmt(line) { public Expr Expression { get; } = expression; }

// use files - refused in Otter 1.0 (a `use "file.ot"` import never gets here)
public sealed class UseModuleStmt(string module, int line) : Stmt(line) { public string Module { get; } = module; }

// ---- routing (D103) ---------------------------------------------------------

// route "/path" shows page / route otherwise shows page
public sealed class RouteStmt(Expr? path, bool isOtherwise, string pageName, int line) : Stmt(line)
{
    public Expr? Path { get; } = path;
    public bool IsOtherwise { get; } = isOtherwise;
    public string PageName { get; } = pageName;
}

// go to "/path"
public sealed class GoToRouteStmt(Expr path, int line) : Stmt(line) { public Expr Path { get; } = path; }

// go back / go forward
public sealed class GoNavigateStmt(bool isForward, int line) : Stmt(line) { public bool IsForward { get; } = isForward; }

// replace route with "/login"
public sealed class ReplaceRouteStmt(Expr path, int line) : Stmt(line) { public Expr Path { get; } = path; }

// on route change
public sealed class RouteChangeStmt(IReadOnlyList<Stmt> body, int line) : Stmt(line) { public IReadOnlyList<Stmt> Body { get; } = body; }

// current route
public sealed class CurrentRouteExpr(int line) : Expr(line);

// route parameter "id"
public sealed class RouteParameterExpr(Expr name, int line) : Expr(line) { public Expr Name { get; } = name; }

// query parameter "search"
public sealed class QueryParameterExpr(Expr name, int line) : Expr(line) { public Expr Name { get; } = name; }

// ---- drag and drop (D110) ---------------------------------------------------

// dragged item / dropped files / drag data / drop x / drop y
public sealed class DragContextExpr(string field, int line) : Expr(line) { public string Field { get; } = field; }

// set drag data to cardId
public sealed class SetDragDataStmt(Expr value, int line) : Stmt(line) { public Expr Value { get; } = value; }
