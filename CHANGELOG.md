# Changelog

All notable changes are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [0.1.0-preview.1] - unreleased

### Changed (pre-release review)

- Conflicts now stay pending (manifest `pendingHash`) until resolved instead of being cleared after one report;
  copies are written as `<path>.incoming` and, when available, `<path>.base`. New `resolve` command.
- `init`, `update`, `resolve` and `rollback` are transactional: complete backup, journal, automatic revert on
  failure, detection of interrupted runs and recovery with `rollback`.
- Rollback keeps files changed after the operation tracked against their current baseline (partial rollback exits
  2), reports files already at their original content, and supports interrupted-operation recovery.
- Project installs in the home directory are refused (they would modify global assistant configuration).
- `.mcp.json` is generated only by the Claude Code adapter; differing existing servers are reported and kept.
- `doctor` warns about `CLAUDE_CONFIG_DIR`/`COPILOT_HOME`, reports interrupted operations, unresolved conflicts and
  detection limits; `status` lists unmanaged assistant files and unresolved conflicts and exits 1 when an operation
  was interrupted.
- Template sources with duplicate or case-colliding paths, malformed zips and oversized entries are rejected;
  generated YAML frontmatter escapes control characters; incomplete manifests and unexpected errors produce clear
  messages instead of stack traces.
- Release workflow is tag-based, separates build from publish, handles already-published and partially published
  versions explicitly, verifies availability after pushing, and pins actions to commit SHAs. `scripts/e2e.sh`
  checks version consistency and that the bundled templates equal the templates package and the source.
- Removed the redundant `accessibility` entry from the `blazor` profile.

### Added

- `agent-standards` .NET tool (`AgentStandards.Tool`) with `init`, `update`, `diff`, `status`, `validate`, `list`,
  `doctor`, `rollback` and `resolve` commands, `--json` output and project or `--global` scope.
- `AgentStandards.Templates` content package with 22 standards, 9 path-scoped rules, 15 skills, 9 specialist agents
  and 7 profiles: `generic`, `dotnet`, `maui`, `aspnetcore`, `webapi`, `blazor`, `classlib`.
- Claude Code adapter: `CLAUDE.md` (imports `AGENTS.md`), `.claude/rules`, `.claude/skills`, `.claude/agents`.
- GitHub Copilot adapter: `.github/copilot-instructions.md`, `.github/instructions`, `.github/skills`,
  `.github/agents`.
- Manifest-based safe updates with local-modification detection, conflict copies, pruning, backups and rollback.
- Template acquisition from bundled templates, the tool cache, the NuGet global packages folder, NuGet sources or a
  local folder, including fully offline operation.
- Optional, add-only MCP server merging into `.mcp.json`.
