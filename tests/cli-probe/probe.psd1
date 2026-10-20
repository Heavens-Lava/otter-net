# Measurement only (cli-windows.yml): texts PowerShell itself prints.
@{
    Cases = @(
        # SPEC-QUESTIONS T3-3: a port [int] cannot read.
        @{ Name = 'T3-3 port text'; Args = @('serve', 's.ot'); TimeoutSeconds = 15; Files = @{ 's.ot' = "api is a web server`n    port is `"abc`"`n.`nwhen api receives GET at `"/hi`"`n    respond with `"hi`"" } }
        @{ Name = 'T3-3 port too big'; Args = @('serve', 's.ot'); TimeoutSeconds = 15; Files = @{ 's.ot' = "api is a web server`n    port is 30000000000`n.`nwhen api receives GET at `"/hi`"`n    respond with `"hi`"" } }
        @{ Name = 'T3-3 listen port text'; Args = @('serve', 's.ot'); TimeoutSeconds = 15; Files = @{ 's.ot' = "listen on port `"x`"`nwhen api receives GET at `"/hi`"`n    respond with `"hi`"" } }
    )
}
