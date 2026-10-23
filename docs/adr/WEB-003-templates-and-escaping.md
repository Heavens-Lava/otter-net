# WEB-003: Templates and escaping

**Status:** proposed (2026-10-23).

- Otter has no interpolation (D8) and no HTML encoder; the `replace`
  idiom inserts raw text (XSS by default).
- **Proposed:** an SDK template library in plain Otter: `{{name}}`
  placeholders HTML-escaped by default; raw output only with an explicit
  `{{{name}}}` marker; includes for layouts/partials resolved inside the
  project; no expressions or code in templates. `escapeHtml` is available
  today (`../web/samples/proposals/html.ot`, runs).
- Attribute, URL and JavaScript contexts: separate helpers
  (`escapeAttribute`, `encodeUrl`); never inject user data into `<script>`.
- Rejected for now: template syntax in Otter source; interpolation
  (language changes).
