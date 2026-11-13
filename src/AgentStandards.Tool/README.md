# AgentStandards.Tool

`agent-standards` is a .NET tool that installs, updates and validates shared engineering standards, skills and
specialist agents for AI coding assistants (Claude Code and GitHub Copilot) in any repository — .NET or not.

```bash
dotnet tool install --global AgentStandards.Tool --prerelease
cd your-repo
agent-standards init --dry-run   # preview
agent-standards init             # install (profile detected from repository files)
agent-standards status
agent-standards update --dry-run
```

- Profiles: `generic`, `dotnet`, `maui`, `aspnetcore`, `webapi`, `blazor`, `classlib`.
- Generates `AGENTS.md`, `CLAUDE.md`, `.claude/{rules,skills,agents}`, `.github/copilot-instructions.md`,
  `.github/{instructions,skills,agents}`.
- Tracks every managed file in `.agent-standards/manifest.json`; local edits are never overwritten without `--force`,
  and every change is backed up for `agent-standards rollback`.
- Works offline with the templates bundled in the tool; other template versions come from
  the `AgentStandards.Templates` package via your NuGet sources or `--source <folder>`.

The tool is a development-time utility. It adds nothing to your application's build or runtime.
See the project README for full documentation.
