using System.Runtime.CompilerServices;
using Otter.Web.Hosting;

namespace Otter.Cli;

// The Otter Web runtime's HTTP host (Kestrel) for the otter command. Not
// compiled into otter-desktop (Otter.Desktop.csproj excludes this file), so
// the desktop host carries no ASP.NET Core.
internal static class WebHosting
{
    [ModuleInitializer]
    internal static void Register() => FileMode.WebHostFactory = KestrelWebHost.Factory;
}
