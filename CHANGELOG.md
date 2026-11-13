# Changelog

All notable changes are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [0.1.0-preview.1] - unreleased

### Added

- `agent-standards` .NET tool (`AgentStandards.Tool`) with `init`, `update`, `diff`, `status`, `validate`, `list`,
  `doctor` and `rollback` commands, `--json` output and project or `--global` scope.
- `AgentStandards.Templates` content package with 22 standards, 9 path-scoped rules, 15 skills, 9 specialist agents
  and 7 profiles: `generic`, `dotnet`, `maui`, `aspnetcore`, `webapi`, `blazor`, `classlib`.
- Claude Code adapter: `CLAUDE.md` (imports `AGENTS.md`), `.claude/rules`, `.claude/skills`, `.claude/agents`.
- GitHub Copilot adapter: `.github/copilot-instructions.md`, `.github/instructions`, `.github/skills`,
  `.github/agents`.
- Manifest-based safe updates with local-modification detection, conflict copies, pruning, backups and rollback.
- Template acquisition from bundled templates, the tool cache, the NuGet global packages folder, NuGet sources or a
  local folder, including fully offline operation.
- Optional, add-only MCP server merging into `.mcp.json`.
