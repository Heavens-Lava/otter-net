# WEB-004: SQLite and migrations

**Status:** proposed (2026-10-23).

- Keep D97 (raw SQL, named parameters, transactions) and the operating
  system's SQLite (no bundled binaries).
- Fix before web use: S-1 query-language vulnerability (approved:
  security exception to 1.0.1 parity), S-2 wildcard escaping, S-7 (multi-statement
  `execute`, bytes parameters, BLOB reads, 64-bit integers).
- Web defaults: `busy_timeout` 5 s, WAL, connection per request, secrets
  from environment variables.
- Migrations: numbered `.sql` files in `migrations/`, applied in a
  transaction, recorded in a `schema_migrations` table, by an SDK library.
- PostgreSQL/SQL Server: provider-neutral later; needs dependency review.
