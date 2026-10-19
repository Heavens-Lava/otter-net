namespace Otter.Runtime;

// D44: a UI resource made by `create KIND into x` (otterPS's OtterUiResource
// in src/Otter.UI.psm1). Its Native object belongs to the UI provider and is
// never shown to a program: it prints as "a button", and PowerShell's
// [string] of the class is its name, which is what otterPS compares when a
// program asks whether two resources are equal (so any two compare equal).
public sealed class UiResource(string kind, object native) : IOtterTypeNamed
{
    // The kind as the program wrote it ("button", "Text Box").
    public string Kind { get; } = kind;
    public object Native { get; } = native;
    public string OtterTypeName => $"a {Kind}";
    public string OtterText => $"a {Kind}";
    public override string ToString() => "OtterUiResource";
}

// What runs desktop windows for `otter run`: otterPS's src/Otter.UI.psm1
// (WPF). The native `otter` has none (Native AOT cannot host WPF); the
// `otter-desktop` host (src/Otter.Desktop) installs its WPF provider before
// anything runs. Every method throws otterPS's own errors.
public interface IUiProvider
{
    // New-OtterUiResourceValue
    UiResource Create(string kind, int line);
    // Get-OtterUiProperty
    object? GetProperty(UiResource resource, string property, int line);
    // Set-OtterUiProperty
    void SetProperty(UiResource resource, string property, object? value, int line);
    // Add-OtterUiEventHandler: the handler runs on the window's thread.
    void AddEventHandler(UiResource resource, string eventName, Action handler, int line);
    // Add-OtterUiChild
    void Put(UiResource container, UiResource item, int line);
    // Show-OtterUiResource: modal, returns when the window closes.
    void Show(UiResource resource, int line);
    // Get-OtterUiDispatcher is set (a window was created or shown) and this is
    // its thread: a cancelled command job then runs its handlers at once.
    bool OnDispatcherThread { get; }
}

public static class UiProviders
{
    // Set once, at start, by the desktop host; null in the native `otter`.
    public static IUiProvider? Current { get; set; }
}
