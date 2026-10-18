namespace Otter.Web;

// A PowerShell error inside otterPS's web compiler that is not an Otter
// error: Export-OtterWebApplication's catch-all reports it as "Otter hit a
// problem inside itself" (`otter web`, `otter desktop`), and with
// -PassThruExceptions (`otter build`, `publish`) it reaches the caller as is.
// The message is PowerShell's own.
public sealed class WebInternalProblem(string message) : Exception(message);
