# Architecture

## Components

| Component | Shipped as | Purpose |
|---|---|---|
| `AgentStandards.Tool` | NuGet .NET tool (`agent-standards`) | CLI. Contains the engine and a bundled copy of the templates matching its version. |
| `AgentStandards.Templates` | NuGet content package | Versioned template content for pinning and independent updates. |
| `AgentStandards.Core` | Assembly inside the tool (not published) | Engine: template acquisition, profiles, detection, rendering, adapters, planning, manifest, backups, validation. |

Core is not published separately because no external consumer needs it yet; it can be packaged later without code
changes.

## NuGet content delivery (verified)

A NuGet package cannot install files at a repository root: `content/` only applies to `packages.config` projects,
`contentFiles/` items are added to the *consuming project's compilation* under `PackageReference`, and .NET tool
packages only lay out their `tools/` folder in the tool store. None of these writes `AGENTS.md` or `.github/` into a
repository, and relying on them would also make the templates a dependency of an application.

Therefore:

1. The templates package stores files under `agent-standards/` — a folder NuGet assigns no meaning to — with
   `IncludeBuildOutput=false`, `DevelopmentDependency=true` and no dependencies. Referencing it from an application
   adds no files, assets or runtime dependencies (`scripts/e2e.sh` checks this with a real console app).
2. The CLI acquires a template set itself (`TemplateResolver`):
   1. `--templates <dir>`: a plain directory (custom or forked standards).
   2. **Bundled**: `tools/net10.0/any/templates/` inside the tool package, described by `bundle.json`. Used when no
      version is requested or the requested version equals the bundled one. Works fully offline.
   3. **Tool cache**: `~/.agent-standards/cache/packages/<id>/<version>/<id>.<version>.nupkg`.
   4. **NuGet global packages folder** (respects `NUGET_PACKAGES` and `nuget.config`): a completed extraction
      (`.nupkg.metadata` present) of the requested version.
   5. **Package sources**: `--source` values or the enabled sources in the `nuget.config` hierarchy of the target
      directory, through `NuGet.Protocol` (`FindPackageByIdResource`), so authenticated feeds configured for NuGet
      work. `--offline` keeps local folder sources only.
3. A package is read as a zip **in memory**: the package identity is checked against the requested ID and version,
   entries outside `agent-standards/` are ignored, entry paths are validated (no traversal or absolute paths),
   symlink entries and oversized entries are rejected, and content must be UTF-8. Downloaded packages are stored in
   the tool cache (not during `--dry-run`).

## Data flow

```
TemplateResolver ──► TemplateSet ──► ProfileResolver (extends, exclusions) ──► RenderContext
                                                    │
                     ContentBuilder (shared files) ─┼─► IAssistantAdapter.Render (claude, copilot, …)
                                                    ▼
                                         RenderedFile[] (Whole | Block | JsonMerge)
                                                    │
               previous manifest + disk ──► InstallPlanner ──► InstallPlan (no writes)
                                                    │
                     --dry-run / diff stop here ◄───┤
                                                    ▼
                                    InstallExecutor: backup → atomic writes → conflict copies → manifest
```

## Managed modes

- **Whole:** the toolkit owns the file. Hash = whole content (line endings normalised).
- **Block:** the toolkit owns only the text between `<!-- agent-standards:begin … -->` and
  `<!-- agent-standards:end -->`. Content outside is the user's. Used for `AGENTS.md`, `CLAUDE.md` and
  `copilot-instructions.md`. CRLF files keep CRLF.
- **JsonMerge:** MCP servers are added to `mcpServers` when their names are absent; existing entries are never
  changed or removed.

## Update algorithm

For each rendered file, with `B` = hash recorded in the manifest, `L` = hash on disk, `N` = hash of the new
template content:

| Condition | Action |
|---|---|
| no entry, no file | create |
| no entry, file equals `N` | adopt (start tracking) |
| no entry, file differs | conflict, untouched (`--force`: overwrite with backup) |
| `L = B`, `N = B` | unchanged |
| `L = B`, `N ≠ B` | update |
| `L ≠ B`, `L = N` | adopt |
| `L ≠ B`, `N = B` | keep local |
| `L ≠ B`, `N ≠ B` | conflict: keep local, write incoming to `.agent-standards/conflicts/<path>`, record `N` as the new baseline so the conflict is reported once (`--force`: overwrite with backup) |
| entry, file deleted | leave deleted (`--force`: restore) |

Managed paths that the new templates no longer produce are reported as *obsolete*. With `--prune`, unmodified ones
are deleted (or their block removed) and modified ones are untracked but kept.

## Manifest

`.agent-standards/manifest.json` (project) or `~/.agent-standards/manifest.json` (global) records the schema version,
tool version, scope, templates package ID/version/origin/pin, requested and resolved profiles, assistants,
exclusions, MCP flag, timestamps, and every managed file with its mode, hash and source. Local modifications are
computed on demand by `status` rather than stored. A corrupt or newer-schema manifest stops `update`, `status` and
`validate`; `init --force` replaces it (the corrupt copy is kept in the backup).

## Backups and rollback

Before writing, the executor stores the previous content of every file it will change, the list of files it
creates (with the hash it writes), and the previous manifest under `.agent-standards/backups/<timestamp>-<op>/`.
`rollback` restores changed files, deletes created files, and restores the manifest — skipping any file that changed
after the operation unless `--force` is given — then removes that backup. The newest 10 backups are kept.

## Path safety

`SafePaths` normalises every relative path from templates, manifests and backups, rejects absolute paths, `.`/`..`
segments, invalid characters and escapes from the root, and checks every existing path component for symbolic links,
junctions and reparse points before reading or writing. Writes go to a temporary file in the same directory and are
moved into place.
