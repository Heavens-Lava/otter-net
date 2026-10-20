using System.Globalization;
using Otter.Language;
using Otter.Runtime;

namespace Otter.Desktop;

// otter-desktop.exe: the `otter` command (Otter.Cli's Program, compiled in)
// with otterPS's desktop windows. The native otter starts it, on Windows,
// for a program that creates a window and for the REPL; it takes the same
// arguments and shares the console, standard input and output, and its exit
// code is otter's.
public static class DesktopProgram
{
    // The user's formats, which PowerShell runs with (WPF's own messages use
    // them), as .NET Framework reads them (Otter.Language.UserFormats).
    public static CultureInfo UserCulture { get; private set; } = CultureInfo.InvariantCulture;

    public static int Main(string[] args)
    {
        UserCulture = UserFormats.Culture;
        // The native otter runs with the invariant culture (invariant
        // globalization); WPF needs real culture data, so this host has it,
        // and the command still sees the invariant culture.
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        UiProviders.Current = new WpfUiProvider();
        // WPF runs on a single-threaded apartment thread: the one that runs
        // the program, as otterPS runs WPF on PowerShell's own thread.
        return Otter.Cli.Program.RunOnLargeStack(args, singleThreadedApartment: true);
    }
}
