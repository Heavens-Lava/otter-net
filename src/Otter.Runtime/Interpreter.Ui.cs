using Otter.Language;

namespace Otter.Runtime;

// The runtime for user interfaces as `otter run` runs them (otterPS
// src/Otter.Interpreter.psm1's UI cases). Only `create` reaches a desktop
// window (WPF, src/Otter.UI.psm1), through the UI provider (UiProvider.cs):
// on Windows a program that creates one runs in the `otter-desktop` host,
// which has the WPF provider (FileMode.cs decides before it starts);
// elsewhere WPF cannot start, as otterPS says. Everything else here needs no
// window: state, derived values and watchers (D56), declarative elements
// (their statements run), and the forms Otter 1.0 refuses when run.
public sealed partial class Interpreter
{
    bool TryExecuteUi(Stmt statement, Scope scope)
    {
        switch (statement)
        {
            // create button into helloButton (D44): New-OtterUiResourceValue
            // starts WPF first, whatever the kind.
            case CreateUiResourceStmt create:
                if (UiProviders.Current is { } provider)
                {
                    scope.Set(create.Target, provider.Create(create.TypeName, create.Line));
                    return true;
                }
                if (OperatingSystem.IsWindows())
                {
                    throw new InvalidOperationException("Desktop windows (WPF) are run by the otter-desktop host.");
                }
                throw new OtterError("Otter could not start its UI provider. WPF may not be available on this system.", 0, "runtime");
            // put helloButton in app (D47)
            case PutInStmt put:
            {
                object? item = Evaluate(put.Item, scope);
                if (item is not UiResource itemResource)
                {
                    throw RuntimeError($"I can only put a UI resource somewhere, but this is {Values.TypeName(item)}.", put.Line);
                }
                object? container = Evaluate(put.Container, scope);
                if (container is not UiResource containerResource)
                {
                    throw RuntimeError($"I can only put something in a UI resource, but this is {Values.TypeName(container)}.", put.Line);
                }
                Ui.Put(containerResource, itemResource, put.Line);
                return true;
            }
            // show app (D47)
            case ShowStmt show:
            {
                object? target = Evaluate(show.Target, scope);
                if (target is not UiResource resource)
                {
                    throw RuntimeError($"I can only show a UI resource, but this is {Values.TypeName(target)}.", show.Line);
                }
                Ui.Show(resource, show.Line);
                return true;
            }
            // state count is 0
            case StateDefStmt state:
            {
                object? value = Evaluate(state.InitialValue, scope);
                scope.SetLocal(state.Name, new OtterSignal(state.Name, value));
                return true;
            }
            // derive doubled is count times 2
            case DeriveDefStmt derive:
                scope.SetLocal(derive.Name, new OtterDerived(derive.Name, derive.Expression, ReadDerived));
                return true;
            // when count changes
            case WatchStmt watch:
            {
                object? raw = scope.GetRaw(watch.TargetName);
                IReadOnlyList<Stmt> body = watch.Body;
                Action<object?> action = _ => Execute(body, scope);
                if (raw is OtterSignal signal) signal.Subscribe(action);
                else if (raw is OtterDerived derived) derived.Subscribe(action);
                else
                {
                    var made = new OtterSignal(watch.TargetName, raw);
                    made.Subscribe(action);
                    scope.SetLocal(watch.TargetName, made);
                }
                return true;
            }
            // card / window / primary button: the statements inside run; the
            // element itself is the web's.
            case UiElementStmt element:
                Execute(element.Children, scope);
                return true;
            case MemoDefStmt memo:
                throw RuntimeError("'memo' is not supported in Otter 1.0.", memo.Line);
            case LifecycleStmt lifecycle:
                throw RuntimeError($"'on {lifecycle.Stage}' is not supported in Otter 1.0.", lifecycle.Line);
            case SharedStateStmt shared:
                throw RuntimeError("'shared' is not supported in Otter 1.0.", shared.Line);
            case UseModuleStmt use:
                throw RuntimeError("'use' is not supported in Otter 1.0.", use.Line);
            case UiActionStmt action:
                throw RuntimeError($"'{action.Action}' is not supported in Otter 1.0.", action.Line);
            case UiEventStmt uiEvent:
                throw RuntimeError("UI event blocks are not supported in Otter 1.0.", uiEvent.Line);
            case UiAnimationBlock animation:
                throw RuntimeError("UI animation is not supported in Otter 1.0.", animation.Line);
            // D110
            case SetDragDataStmt setDragData:
                throw RuntimeError("\"set drag data\" is only available in web applications, inside \"on drag of ...\".", setDragData.Line);
            // otterPS's interpreter has no case for these: its default.
            case AwaitStmt awaitStmt:
                throw NotRunnable("Await", awaitStmt.Line);
            case RouteStmt route:
                throw NotRunnable("Route", route.Line);
            case GoToRouteStmt goTo:
                throw NotRunnable("GoToRoute", goTo.Line);
            case GoNavigateStmt go:
                throw NotRunnable("GoNavigate", go.Line);
            case ReplaceRouteStmt replace:
                throw NotRunnable("ReplaceRoute", replace.Line);
            case RouteChangeStmt routeChange:
                throw NotRunnable("RouteChangeEvent", routeChange.Line);
        }
        return false;
    }

    bool TryEvaluateUi(Expr expression, Scope scope, out object? value)
    {
        value = null;
        switch (expression)
        {
            case AwaitExpr awaitExpr:
                throw RuntimeError("'await' is not supported in Otter 1.0.", awaitExpr.Line);
            case DragContextExpr drag:
                throw RuntimeError($"\"{drag.Field}\" is only available in web applications, inside a drag or drop event.", drag.Line);
            // No case in otterPS's Get-OtterValue: its default.
            case CurrentRouteExpr current:
                throw NotEvaluable("CurrentRoute", current.Line);
            case RouteParameterExpr routeParameter:
                throw NotEvaluable("RouteParameter", routeParameter.Line);
            case QueryParameterExpr queryParameter:
                throw NotEvaluable("QueryParameter", queryParameter.Line);
        }
        return false;
    }

    // A UI resource exists only when a provider made it.
    static IUiProvider Ui => UiProviders.Current ?? throw new InvalidOperationException("A UI resource exists without a UI provider.");

    // when helloButton is clicked (D46), after Jobs.cs has ruled out a
    // command job. A handler set up while another handler runs keeps that
    // run's names (D133); the body runs in the scope the handler was set up
    // in, as otterPS's GetNewClosure() handler does.
    void AddUiHandler(WhenStmt handlerStmt, UiResource resource, Scope scope)
    {
        Scope captured = NewHandlerCaptureScope(scope);
        IReadOnlyList<Stmt> body = handlerStmt.Body;
        Ui.AddEventHandler(resource, handlerStmt.EventName, () =>
        {
            bool entered = EnterHandlerRun(handlerStmt);
            try { Execute(body, captured); }
            finally { ExitHandlerRun(entered); }
        }, handlerStmt.Line);
    }

    // `x has text "Hi", width 80` on an existing resource (otterPS's ObjectDef
    // case): each property in turn, through the provider.
    void SetUiProperties(UiResource resource, ObjectDefStmt objectDef, Scope scope)
    {
        foreach (var property in objectDef.Properties)
        {
            if (property.Target is not VariableExpr name)
            {
                throw RuntimeError("A UI resource property name must be a plain name.", property.Line);
            }
            object? value = Evaluate(property.Value, scope);
            Ui.SetProperty(resource, name.Name, value, property.Line);
        }
    }

    OtterError NotEvaluable(string kind, int line) => RuntimeError($"I do not know how to work out a {kind} value yet.", line);

    // Get-OtterDerivedValue: worked out in the scope that reads it, then
    // subscribed to every state variable or derived value it read there.
    object? ReadDerived(OtterDerived derived, Scope scope)
    {
        if (derived.IsEvaluating)
        {
            throw RuntimeError($"Circular dependency detected in derived value \"{derived.Name}\".", 0);
        }
        if (derived.IsDirty)
        {
            derived.IsEvaluating = true;
            HashSet<string>? previous = Scope.DependencyTracker;
            var tracker = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Scope.DependencyTracker = tracker;
            try
            {
                object? value = Evaluate(derived.Expression, scope);
                derived.CachedValue = value;
                derived.IsDirty = false;
                foreach (string name in tracker)
                {
                    if (string.Equals(name, derived.Name, StringComparison.OrdinalIgnoreCase)) continue;   // PowerShell's -eq
                    object? raw = scope.GetRaw(name);
                    if (raw is OtterSignal signal) signal.Subscribe(derived);
                    else if (raw is OtterDerived source) source.Subscribe(derived);
                }
            }
            finally
            {
                Scope.DependencyTracker = previous;
                derived.IsEvaluating = false;
            }
        }
        return derived.CachedValue;
    }
}
