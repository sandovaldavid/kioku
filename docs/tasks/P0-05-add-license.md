# P0-05 — Añadir archivo LICENSE

| Campo | Valor |
|---|---|
| Prioridad | P0 |
| Rama | `chore/add-license` |
| Commit | `chore(config): add MIT license file and metadata` |
| Tamaño | S |
| Dependencias | **Decisión del autor**: confirmar que la licencia es MIT |

## Contexto

Detectado en la revisión de 2026-07-02: el README raíz declara "MIT — ver `LICENSE`"
pero **el archivo `LICENSE` no existe** en la raíz del repo, y ninguna metadata lo
declara (`package.json` sin campo `license`, csproj sin `PackageLicenseExpression`). El
paquete se publica en NuGet.org (`publish-nuget` en `release-please.yml`) y el plugin aspira
a la Community Store — ambos requieren licencia explícita.

## Alcance

1. Confirmar la licencia (el README dice MIT).
2. Crear `LICENSE` en la raíz (texto MIT, copyright David Sandoval).
3. Declararla en metadatos:
   - `src/Kioku.Mcp.Server/Kioku.Mcp.Server.csproj`: `<PackageLicenseExpression>MIT</PackageLicenseExpression>`
   - `package.json` raíz y `src/obsidian-kioku-mcp/package.json`: `"license": "MIT"`

## Criterios de aceptación

- [ ] `LICENSE` existe y el link del README resuelve.
- [ ] `dotnet pack` incluye la licencia sin warnings.
- [ ] GitHub detecta la licencia en la página del repo.
