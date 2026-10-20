# create-module

Zero-dependency Node (>= 18) CLI that scaffolds a platform module (.NET 8 API + React/Vite
federation remote + `manifest.yml` + parity test), wires it into the solution, Aspire AppHost,
`docker-compose.yml` and tests, and can publish/install it.

```bash
cd module_as_plugin
node tools/create-module/create-module.mjs new inventory --display-name "Inventory" -y
node tools/create-module/create-module.mjs help
```

- `templates/` is the source of truth (tokens like `__MODULE_NAME__`, `__PASCAL__`, `__PERM__`).
  `*.template` files are renamed on output (kept suffixed so IDEs don't restore them in place).
- Full guide: [docs/module-developer-guide.md](../../docs/module-developer-guide.md).
