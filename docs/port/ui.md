# UI port inventory (web and desktop)

Status of every user-interface capability of otterPS 1.0.1 in otter-net.
Status values: **native** (otter-net does it itself), **partial**,
**missing** (handed to the PowerShell Otter before anything runs),
**out of scope** (not part of this port).

_First pass, 2026-10-17: everything below is missing; updated as batches land._

| Capability | otterPS source | otter-net | Status | Fallback | Tests | Remaining work |
|---|---|---|---|---|---|---|
| `otter web` | otter.ps1, Otter.Web.psm1 `Export-OtterWebApplication` | - | missing | whole command | - | port |
| `otter serve` | otter.ps1, Otter.Server.psm1 | - | missing | whole command | - | needs network area |
| `otter desktop` | otter.ps1, Otter.Desktop.psm1 | - | missing | whole command | - | see desktop-options.md |
| `otter studio` | otter.ps1, Otter.Desktop.psm1 `Start-OtterStudio` | - | out of scope | whole command | - | developed separately |
