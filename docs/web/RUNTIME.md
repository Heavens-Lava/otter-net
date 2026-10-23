# Otter Web runtime 0.1 (preview)

**Preview. Loopback only by default. Not for production.** It serves an
Otter program's routes over HTTP with the Native AOT `otter-net`; no
PowerShell, no .NET install, no Node. The exact rules are in
[RUNTIME_CONTRACT.md](RUNTIME_CONTRACT.md).

## Try it

`otter.json`:

```json
{
  "name": "hello-web",
  "version": "0.1.0",
  "target": "server",
  "entryPoint": "main.ot",
  "web": { "runtime": "otter-web-0.1" }
}
```

`main.ot` (Otter 1.0.1 syntax, D51):

```otter
site is a web server
    port is 8080
.

when site receives GET at "/hello" into req
    get "name" from query of req into name
    if name is gone
        name is "stranger"
    .
    respond with "Hello, " plus name
.

when site receives POST at "/api/notes" into req
    note is a thing
    convert body of req from json into note
    respond with note as json and status 201
.
```

```
otter-net run .
curl "http://127.0.0.1:8080/hello?name=Ada"        # Hello, Ada
curl -X POST -d '{"title":"hi"}' http://127.0.0.1:8080/api/notes
```

Ctrl+C stops it after answering the requests in progress.

## What it does not do yet

Path parameters, static files, a response object with headers and
redirects (Phase 1b); forms, templates, databases for the web (Phase 2);
cookies, sessions, authentication, HTTPS (Phase 3); React/Vite templates
(Phase 4); WebSockets and 3D templates (Phase 5). See
[../OTTER_WEB_PLAN.md](../OTTER_WEB_PLAN.md).

Without `"web"` in `otter.json`, nothing changes: `otter run` and `otter
serve` behave exactly as in Otter 1.0.1.
