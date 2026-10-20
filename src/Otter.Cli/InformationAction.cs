namespace Otter.Cli;

// `-InformationAction Stop` or `Ignore` on otter.ps1 (an advanced script, so
// PowerShell's common parameters apply; docs/SPEC-QUESTIONS.md C2-5). Every
// Write-Host of otterPS - otter.ps1's own and its modules', the program's
// `say` included - then fails, as measured on Windows PowerShell 5.1 in CI
// (.github/workflows/cli-windows.yml):
//
//   Stop    the text is written, then "The running command stopped because
//           the preference variable ... is set to Stop: <text>" is thrown;
//   Ignore  nothing is written, and "The value Ignore is not supported for
//           an ActionPreference variable ..." is thrown. PowerShell puts the
//           default back in the scope where that Write-Host ran, so when it
//           ran in otter.ps1's own scope inside a try (`otter new`, opening
//           the browser of `otter web`), the catch block's Write-Host works.
//
// The failure travels as otterPS's would: a catch of otter.ps1 shows it
// (whose own first Write-Host fails again), or it ends the script, and
// powershell.exe reports it at that Write-Host with exit code 1.
// `-InformationAction Inquire` prompts on the console before each line and
// offers a nested PowerShell prompt; it stays with the PowerShell Otter
// (docs/SPEC-QUESTIONS.md F5-1).
internal static class InformationAction
{
    public enum Mode { Continue, Stop, Ignore }

    public static Mode Current { get; set; } = Mode.Continue;

    // Ignore was replaced by the default in otter.ps1's own scope.
    public static bool Replaced { get; set; }

    public static bool Active => Current == Mode.Stop || (Current == Mode.Ignore && !Replaced);

    // Before a Write-Host writes: Ignore fails at once.
    public static void Before(string text, PsSite? site)
    {
        if (!Active) return;
        if (site == null)
        {
            // Every Write-Host that can fail first carries its position; one
            // that does not is a gap in otter-net, handed over while nothing
            // has been written.
            if (!Host.HasWritten) throw new NeedsPowerShell("-InformationAction reached a Write-Host whose otterPS position otter-net does not know");
            throw new InvalidOperationException("otter-net: a Write-Host without its otterPS position failed under -InformationAction.");
        }
        if (Current == Mode.Ignore) throw new WriteHostFailure(Mode.Ignore, text, site);
    }

    // After it wrote: Stop fails.
    public static void After(string text, PsSite? site)
    {
        if (Active && Current == Mode.Stop) throw new WriteHostFailure(Mode.Stop, text, site!);
    }
}

// The exception a failing Write-Host throws, and powershell.exe's report of
// it when it ends otter.ps1.
internal sealed class WriteHostFailure(InformationAction.Mode mode, string text, PsSite site) : Exception(MessageFor(mode, text))
{
    public InformationAction.Mode Mode { get; } = mode;
    public PsSite Site { get; } = site;

    static string MessageFor(InformationAction.Mode mode, string text) => mode == InformationAction.Mode.Stop
        ? $"The running command stopped because the preference variable \"InformationPreference\" or common parameter is set to Stop: {text}"
        : "The value Ignore is not supported for an ActionPreference variable. The provided value should be used only as a value for a preference parameter, and has been replaced by the default value. For more information, see the Help topic, \"about_Preference_Variables.\"";

    public PowerShellError Report() => Mode == InformationAction.Mode.Stop
        ? new PowerShellError("Write-Host : ", Message, Site.Position, "OperationStopped", "Write-Host", "ParentContainsErrorRecordException",
            "ActionPreferenceStop,Microsoft.PowerShell.Commands.WriteHostCommand")
        : new PowerShellError("Write-Host : ", Message, Site.Position, "NotSpecified", "Write-Host", "NotSupportedException",
            "System.NotSupportedException,Microsoft.PowerShell.Commands.WriteHostCommand");
}

// The program's own output under -InformationAction: each `say` (a Write
// or WriteLine of a whole text) is one Write-Host of otterPS's interpreter
// module. Characters written one at a time (the echo of typed input) are not.
internal sealed class InformationWriter(TextWriter inner) : TextWriter
{
    public override System.Text.Encoding Encoding => inner.Encoding;

    public override void Write(char value) => inner.Write(value);

    public override void Write(string? value)
    {
        InformationAction.Before(value ?? "", PsSite.Handled);
        inner.Write(value);
        InformationAction.After(value ?? "", PsSite.Handled);
    }

    public override void WriteLine(string? value)
    {
        InformationAction.Before(value ?? "", PsSite.Handled);
        inner.Write(value);
        inner.Write('\n');
        InformationAction.After(value ?? "", PsSite.Handled);
    }

    public override void WriteLine() => WriteLine("");

    public override void Flush() => inner.Flush();
}
