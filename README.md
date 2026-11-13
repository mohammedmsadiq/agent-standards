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
  `--force`; unknown files are never touched; obsolete files are removed only with `--prune`.
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
| `.mcp.json` | both | Only with `--mcp` and only when a profile declares servers |

Existing files are preserved. For `AGENTS.md`, `CLAUDE.md` and `copilot-instructions.md`, the tool appends a
clearly marked managed block and never changes content outside it. See
[docs/assistants.md](docs/assistants.md) for the verified discovery behaviour behind these choices.

## Supported assistants

| Assistant | Project scope | Global scope (`--global`) |
|---|---|---|
| Claude Code (`claude`) | `CLAUDE.md`, `.claude/` | `~/.claude/CLAUDE.md`, `~/.claude/{rules,skills,agents}` |
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
- **Edit any generated file.** Edits are detected; `update` keeps them and saves the incoming version under
  `.agent-standards/conflicts/` for you to merge.
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

For each managed file `update` compares the recorded hash, the file on disk and the new template:

| On disk | Template | Result |
|---|---|---|
| unchanged | changed | updated |
| modified | unchanged | kept |
| modified | changed | **conflict**: local kept, incoming saved to `.agent-standards/conflicts/<path>`, exit code 2 |
| deleted | any | left deleted (`--force` restores) |
| unmanaged file at a target path | — | **conflict**: left untouched (`--force` replaces it, with a backup) |

Line-ending differences do not count as modifications. The tool does not perform automatic line-level merges;
conflict copies are the documented alternative.

## Versioning

- Both packages follow Semantic Versioning and are versioned together. The tool bundles the templates with the
  same version.
- `init`/`update` without `--version` use the bundled templates; updating the tool (`dotnet tool update`) therefore
  brings new standards. An installed newer template version is never downgraded.
- `--version <x>` pins that templates version; `update` keeps it until you pass another `--version`.
  `--version latest` resolves the newest version from your sources and unpins.
- Template sets carry a `schemaVersion`; the tool refuses a schema it does not understand.

## Rollback

Every `init`/`update` that changes files first saves a backup under `.agent-standards/backups/` (the last 10 are
kept; the folder is git-ignored).

```bash
agent-standards rollback --list
agent-standards rollback --dry-run
agent-standards rollback --yes          # undo the most recent operation
agent-standards rollback --to <id> --yes
```

Files changed after the operation are left alone unless you add `--force`.

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
- Writes are atomic; every change is backed up first; `--dry-run`, `diff`, `status`, `validate`, `list` and
  `doctor` do not write.
- Nothing is deleted without `--prune` (or `rollback`), and only files whose content is unchanged.
- No credentials, tokens, machine paths or private addresses are bundled. Template validation rejects MCP servers
  with literal secrets or absolute command paths. Existing `.mcp.json` entries are never modified.
- The generated instructions guide assistants; they are not an enforcement mechanism. Configure each assistant's
  permission settings for hard limits.

Exit codes: `0` success, `1` error or validation failure, `2` completed with conflicts that need attention.

## Limitations

- No automatic line-level three-way merge; conflicts produce a copy for manual merging.
- Global scope writes to `~/.claude` and `~/.copilot` (or under `AGENT_STANDARDS_HOME`); the `CLAUDE_CONFIG_DIR`
  and `COPILOT_HOME` overrides are not read.
- No MCP servers are bundled. `.mcp.json` at the repository root is read by Claude Code; VS Code Copilot reads it
  only with an experimental setting, and `.vscode/mcp.json` is not generated.
- Copilot surfaces that do not read `AGENTS.md` see the core policy from `copilot-instructions.md` but not the
  project-context section. Links to detailed standards are read on demand; assistants do not guarantee they load
  linked files.
- Skills and agents are rendered once per assistant folder, so enabling both adapters writes two copies.
- Rewriting `.mcp.json` to add servers normalises its formatting (a backup is kept).
- No package icon yet.

## Building from source

```bash
git clone <repository-url>
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
