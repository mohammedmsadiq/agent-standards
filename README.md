# Agent Standards

A vendor-neutral, open-source toolkit for installing, updating, validating and customising shared engineering
standards for AI coding assistants — **Claude Code** and **GitHub Copilot** today, with an adapter interface for
others. It is distributed as a .NET tool, but it manages repositories in any language.

> **Status:** `0.1.0-preview.1`, not yet published to NuGet.org. Until it is, install from source (see
> [Building from source](#building-from-source)).

## Features

- **One command to set up a repository:** `agent-standards init` detects the project type from real files and
  writes `AGENTS.md`, `CLAUDE.md`, `.github/copilot-instructions.md`, path-scoped rules, skills and specialist
  agents in each assistant's native format.
- **Canonical content, per-assistant output.** Standards are stored once and linked; rules, skills and agents are
  authored once and rendered into each assistant's format.
- **Balanced autonomy by default.** Assistants may do normal development, tests and low-risk refactoring, but must
  ask before destructive operations, architecture changes, dependency changes, credential handling, production
  changes and publishing.
- **Profiles:** `generic`, `dotnet`, `maui`, `aspnetcore`, `webapi`, `blazor`, `classlib` — extensible through a JSON
  schema without engine changes. Non-.NET repositories get only the generic profile.
- **Safe updates:** a manifest records every managed file and its hash. Local edits are never overwritten without
  `--force`; unknown files are never touched; obsolete files are removed only with `--prune`. Conflicts stay visible
  until you resolve them.
- **Transactional and locked:** every change is backed up first and applied as one unit under an exclusive lock; a
  failure part-way is reverted automatically, and an interrupted run is detected and recoverable with `rollback`.
- **Dry-run, diff, rollback and version pinning**, project and global (per-user) scopes.
- **Offline:** the tool bundles the templates matching its version; other versions come from your NuGet sources, a
  local folder or the NuGet global packages folder.
- **No runtime footprint.** Nothing is added to your application's build or dependencies.

## Installation

Once published:

```bash
dotnet tool install --global AgentStandards.Tool --prerelease
```

Or as a repository-local tool:

```bash
dotnet new tool-manifest
dotnet tool install AgentStandards.Tool --prerelease
dotnet tool run agent-standards --help
```

Requires the .NET 10 runtime or later (the tool rolls forward to newer major versions).

## Quick start

```bash
cd your-repo
agent-standards init --dry-run      # preview: detected profile and every file that would be written
agent-standards init                # install (asks for confirmation in an interactive terminal)
agent-standards status              # versions, profiles, locally modified files
agent-standards validate            # check generated files, frontmatter, imports and links
git diff                            # review, then commit the generated files
```

Other examples:

```bash
agent-standards init --profile generic
agent-standards init --profile maui
agent-standards init --profile webapi --assistants copilot
agent-standards init --profile dotnet --exclude skill:review-performance,agent:migration-specialist
agent-standards update --dry-run
agent-standards diff
agent-standards list profiles
agent-standards resolve --all --dry-run
agent-standards rollback --dry-run
agent-standards init --global       # personal standards for Claude Code and Copilot
```

## What gets generated

| File | Assistant | Notes |
|---|---|---|
| `AGENTS.md` | shared | Managed block (policy, links to standards, index of rules/skills/agents) plus a project-context section that belongs to you |
| `.agent-standards/standards/*.md` | shared | Canonical detailed standards, stored once |
| `.agent-standards/manifest.json` | — | Installed versions, options and file hashes. Commit it. |
| `CLAUDE.md` | Claude Code | Managed block that imports `@AGENTS.md` (Claude Code does not read `AGENTS.md` when a `CLAUDE.md` exists) |
| `.claude/rules/*.md` | Claude Code | Path-scoped rules (`paths:` frontmatter) |
| `.claude/skills/<name>/SKILL.md` | Claude Code | Skills |
| `.claude/agents/<name>.md` | Claude Code | Subagents; review agents limited to `Read, Grep, Glob` |
| `.github/copilot-instructions.md` | GitHub Copilot | Managed block with the core policy and links (for Copilot surfaces that do not read `AGENTS.md`) |
| `.github/instructions/*.instructions.md` | GitHub Copilot | Path-specific instructions (`applyTo:` frontmatter) |
| `.github/skills/<name>/SKILL.md` | GitHub Copilot | Agent skills |
| `.github/agents/<name>.agent.md` | GitHub Copilot | Custom agents; review agents limited to `['read', 'search']` |
| `.mcp.json` | Claude Code | Only with `--mcp`, the `claude` adapter, and a profile that declares servers. Existing servers are never changed. |

Existing files are preserved. For `AGENTS.md`, `CLAUDE.md` and `copilot-instructions.md`, the tool appends a
clearly marked managed block and never changes content outside it. See
[docs/assistants.md](docs/assistants.md) for the verified discovery behaviour behind these choices.

## Supported assistants

| Assistant | Project scope | Global scope (`--global`) |
|---|---|---|
| Claude Code (`claude`) | `CLAUDE.md`, `.claude/`, optional `.mcp.json` | `~/.claude/CLAUDE.md`, `~/.claude/{rules,skills,agents}` |
| GitHub Copilot (`copilot`) | `.github/` | `~/.copilot/copilot-instructions.md`, `~/.copilot/{instructions,skills,agents}` |

Other assistants that read `AGENTS.md` benefit from the shared content without an adapter. New adapters implement
`IAssistantAdapter` (see [CONTRIBUTING.md](CONTRIBUTING.md)).

## Supported project profiles

| Profile | Extends | Detected when |
|---|---|---|
| `generic` | — | fallback when nothing more specific matches |
| `dotnet` | `generic` | any `*.csproj`, `*.fsproj`, `*.vbproj`, `*.sln`, `*.slnx` |
| `maui` | `dotnet` | a project file contains `<UseMaui>true</UseMaui>` |
| `aspnetcore` | `dotnet` | a project uses `Microsoft.NET.Sdk.Web` |
| `webapi` | `aspnetcore` | Web SDK plus OpenAPI/Swashbuckle, `[ApiController]` or `MapGet(` |
| `blazor` | `dotnet` | `Microsoft.NET.Sdk.BlazorWebAssembly` or `*.razor` files (not MAUI) |
| `classlib` | `dotnet` | project files that are not executables, web, Razor, worker or MAUI projects |

Detection keeps the most specific matches (a monorepo can get `maui` and `webapi`). Override it with `--profile`.
Run `agent-standards list` to see standards, rules, skills and agents per template set.

## Configuration precedence

**Instructions** (stated in the generated policy so every assistant applies it):

1. The user's explicit instructions for the current task.
2. Project-specific guidance: content outside managed blocks, nested `AGENTS.md` files, and standards edited for
   the project.
3. Installed profile standards (managed blocks and `.agent-standards/standards/`).
4. Personal global standards (`~/.claude/CLAUDE.md`, `~/.copilot/copilot-instructions.md`).
5. General practice.

The approval rules apply at every level unless the user explicitly waives one for the current task.

**Tool options** for `update`: command-line options › values recorded in the manifest › defaults (detected profile,
all assistants, bundled templates).

## Customisation

- **Project context:** fill in the *Project context* section that `init` adds below the managed block in a new
  `AGENTS.md` (architecture, commands, conventions, decisions, known issues, overrides). It is never modified.
- **Edit any generated file.** Edits are detected and kept; when the template also changes, `update` reports a
  conflict and saves the incoming version for you to merge (see [Resolving conflicts](#resolving-conflicts)).
- **Choose content:** `--profile`, `--assistants`, `--exclude standard:|rule:|skill:|agent:NAME`. Choices are
  remembered in the manifest.
- **Your own standards:** copy `templates/AgentStandards.Templates/agent-standards`, change it, and use
  `--templates <dir>`, or publish it as your own package and use `--package-id <id> --source <feed>`. See
  [docs/profiles.md](docs/profiles.md).
- **Global vs project:** `--global` installs personal standards independently of any repository.

## Skills and agents

Generic skills: `review-code`, `investigate-bug`, `write-tests`, `refactor-code`, `review-security`,
`review-performance`, `document-architecture`, `review-dependencies`, `create-feature`, `review-pull-request`.
Profile skills: `analyze-dotnet-build`, `review-maui-page`, `design-api-endpoint`, `review-blazor-component`,
`review-public-api`.

Generic agents: `software-architect`, `code-reviewer`, `security-reviewer`, `performance-engineer` and
`migration-specialist` (read-only); `test-engineer` and `documentation-engineer` (read-write). Profile agents:
`maui-architect`, `api-designer` (read-only).

## Updating

```bash
agent-standards update --dry-run   # what would change
agent-standards diff               # unified diffs
agent-standards update             # apply
agent-standards update --prune     # also remove files the templates no longer provide (unmodified ones only)
```

For each managed file `update` compares the last applied template (baseline), the file on disk and the new template:

| On disk | Template | Result |
|---|---|---|
| unchanged | changed | updated |
| modified | unchanged | kept |
| modified | changed | **conflict**: local kept, see below; exit code 2 |
| equal to the new template | — | adopted (clears any conflict) |
| deleted | any | left deleted (`--force` restores) |
| unmanaged file at a target path | — | **conflict**: left untouched (`--force` replaces it, with a backup) |

Line-ending differences do not count as modifications. `--force` replaces modified and unmanaged files with the
template version (after a backup) and clears conflicts; it never touches content outside managed blocks.

## Resolving conflicts

A conflict keeps your local file and saves, under `.agent-standards/conflicts/`:

- `<path>.incoming` — the new template version;
- `<path>.base` — the previously applied version, when it can be rebuilt offline (bundled, cached or directory templates).

The copies are yours to edit while merging: the tool records what it wrote and never overwrites or deletes a copy
you changed (it says so instead); `--force` on `update` or `resolve` removes edited copies too. Delete an edited
copy yourself to get a fresh one on the next `update`.

The conflict stays pending — shown by `status`, `doctor` and `validate`, and every `update` exits with code 2 — until:

- you merge what you want from `.incoming` into your file and run `agent-standards resolve <path>` (or
  `resolve --all`); your file, exactly as it is, becomes the accepted version and unedited copies are removed; or
- your file ends up identical to the incoming version (resolved automatically on the next `update`); or
- the template returns to the previously applied version; or
- you run `update --force` to take the template version.

`resolve` shows each file as `edited` (changed since the conflict was reported), `unchanged` (nothing merged) or
`unknown` (recorded by an older version), and asks for confirmation; scripts, CI and `--json` must pass `--yes`.
An `unchanged` file is refused, because accepting it silently drops the incoming changes; pass `--accept-local` to
keep your version deliberately. `edited` only means the file changed: the tool cannot check that the incoming
changes are actually in it, so compare with the `.incoming` copy before confirming. `resolve --dry-run` previews all of this without changing anything.

Unmanaged files that block an install cannot be "resolved"; rename or remove them, or use `--force`.

## Versioning

- Both packages follow Semantic Versioning and are versioned together. The tool bundles the templates with the
  same version.
- `init`/`update` without `--version` use the bundled templates; updating the tool (`dotnet tool update`) therefore
  brings new standards. An installed newer template version is never downgraded.
- `--version <x>` pins that templates version; `update` keeps it until you pass another `--version`.
  `--version latest` resolves the newest version from your sources and unpins.
- Template sets carry a `schemaVersion`; the tool refuses a schema it does not understand.

## Backups, rollback and recovery

Every operation that changes files (`init`, `update`, `resolve`, `rollback`) first saves a complete backup under
`.agent-standards/backups/` (the newest 10 are kept; the folder is git-ignored), then writes a journal
(`.agent-standards/operation.json`), applies all changes, and removes the journal.

- **A failure while writing** (permissions, disk full, a locked file) reverts every change automatically; the
  command exits 1 and says so.
- **If the process is killed** part-way, the journal remains. `status` and `validate` exit 1, `doctor` reports it,
  and `init`/`update` refuse to run until you recover with `agent-standards rollback`.
- **If writing the backup or the journal fails**, nothing has been modified and the unused backup is removed.
- **If the journal cannot be removed** after all changes were written, the command exits 1 (it does not report
  success) and the installation is marked as interrupted: run `rollback` to undo, or `complete` to keep it.
  `complete` checks that every file the operation changed has exactly the content it wrote and that the manifest
  loads; only then does it clear the journal. If anything differs it changes nothing and recommends `rollback`.
- **If a recovery is itself interrupted**, rolling it back restores the original interrupted state and its journal;
  run `rollback` again to finish. Recovery is safe to repeat.
- **If the journal's backup is missing or unreadable**, the operation can be neither verified nor rolled back.
  `complete` then lists every managed file that differs from the manifest (each is either your own edit or a change
  the interrupted operation wrote) and changes nothing. Review those files with `git status` and `git diff`, restore
  from version control anything that is wrong, then run `complete --unverified` (it asks for confirmation; `--yes`
  in scripts) to clear the interrupted state, and `update` to reconcile the managed files. Do not delete
  `.agent-standards/operation.json` by hand: that skips this check.

### Concurrent operations

Commands that change an installation (`init`, `update`, `resolve`, `rollback`, `complete`) take an exclusive lock on
`.agent-standards/lock` (in the home directory for `--global`). A second command fails at once with "Another
agent-standards operation is running" instead of waiting, so processes cannot deadlock. The lock is an open file
handle (`FileShare.None`: a sharing lock on Windows, `flock` on Linux and macOS), so the operating system releases it
when a process exits or crashes; a leftover `lock` file never blocks anything. Read-only commands (`status`,
`validate`, `doctor`, `diff`, `list`, and every `--dry-run`) do not take the lock and report an operation in
progress instead of an interrupted one. The lock protects one machine; it is not designed for repositories shared
over network file systems.

```bash
agent-standards rollback --list
agent-standards rollback --dry-run      # exactly the actions rollback will take
agent-standards rollback --yes          # undo the most recent operation (or recover an interrupted one)
agent-standards rollback --to <id> --yes
agent-standards complete --dry-run      # check whether an interrupted operation finished writing
agent-standards complete                # keep it (only if every change verifies)
```

Files you changed after the operation are kept (`skip-modified`) and stay tracked against the version they are
based on, so the manifest always describes the files on disk; such a partial rollback exits 2. `--force` reverts
those files too (a safety backup is taken first). Rollback itself is transactional.

## Offline usage

- Bundled templates need no network: `agent-standards init --offline`.
- Other versions resolve in this order: tool cache (`~/.agent-standards/cache`), NuGet global packages folder,
  then package sources. With `--offline`, only local folder sources are used.
- Air-gapped: copy `AgentStandards.Templates.<version>.nupkg` to a folder and run
  `agent-standards update --version <version> --source <folder> --offline`.

## Security model

- All target paths are validated: no absolute paths, `..` segments or escapes from the target root.
- The tool refuses to write through symbolic links or junctions, and refuses symlinks in template sources and
  package entries.
- Packages are read in memory with entry-size and count limits and identity checks; nothing is extracted to disk.
- Writes are atomic and transactional; every change is backed up first; a file edited between planning and
  applying aborts the operation; `--dry-run`, `diff`, `status`, `validate`, `list` and `doctor` do not write.
- Template sources with duplicate or case-colliding paths, malformed zips and oversized entries are rejected.
- Nothing is deleted without `--prune` (or `rollback`), and only files whose content is unchanged.
- Project installs never write to `~/.claude` or `~/.copilot`.
- No credentials, tokens, machine paths or private addresses are bundled. Template validation rejects MCP servers
  with literal secrets or absolute command paths. Existing `.mcp.json` entries are never modified.
- The generated instructions guide assistants; they are not an enforcement mechanism. Configure each assistant's
  permission settings for hard limits.

Exit codes: `0` success; `1` error, validation failure or interrupted operation; `2` completed but needs attention
(unresolved conflicts, partial rollback).

## Limitations

- No automatic line-level three-way merge; conflicts produce `.incoming`/`.base` copies for manual merging.
- Global scope always writes to `~/.claude` and `~/.copilot` (or under `AGENT_STANDARDS_HOME`). `CLAUDE_CONFIG_DIR`
  and `COPILOT_HOME` are not followed because the assistants do not document them consistently for instructions,
  skills and agents; `init --global`, `update --global` and `doctor` warn when they are set. Project installs in the home directory are refused because
  they would modify the global configuration.
- No MCP servers are bundled. Only Claude Code's `.mcp.json` is generated; Copilot MCP configuration is not.
- Copilot surfaces that do not read `AGENTS.md` see the core policy from `copilot-instructions.md` but not the
  project-context section. Links to detailed standards are read on demand; assistants do not guarantee they load
  linked files.
- Skills and agents are rendered once per assistant folder, so enabling both adapters writes two copies.
- Rewriting `.mcp.json` to add servers normalises its formatting (a backup is kept).
- Project detection scans six directory levels and up to 20,000 files and skips build, dependency and VCS folders;
  `doctor` reports when the limit is reached. Use `--profile` to override.
- Conflict copies are git-ignored, so a pending conflict recorded in a committed manifest has no copy on another
  machine; re-run `update` there to regenerate it.
- Operation locking covers processes on one machine, not network file systems shared between machines.
- No package icon yet.

## Building from source

```bash
git clone https://github.com/mohammedmsadiq/agent-standards.git
cd agent-standards
dotnet test AgentStandards.slnx
dotnet pack AgentStandards.slnx -c Release -o artifacts/packages
dotnet tool install --global AgentStandards.Tool --prerelease --add-source ./artifacts/packages
scripts/e2e.sh   # optional end-to-end check of the packages
```

## Publishing to NuGet.org

Publishing is manual and requires approval. See [docs/publishing.md](docs/publishing.md) for the release checklist,
API-key handling and the `release.yml` workflow.

## Further documentation

- [Architecture and NuGet delivery](docs/architecture.md)
- [Assistant integration and verified discovery behaviour](docs/assistants.md)
- [Profiles and template schema](docs/profiles.md)
- [Publishing](docs/publishing.md)
- [Contributing](CONTRIBUTING.md) · [Changelog](CHANGELOG.md) · [Security](SECURITY.md)

## Licence

[MIT](LICENSE) © 2026 Mohammed Sadiq
