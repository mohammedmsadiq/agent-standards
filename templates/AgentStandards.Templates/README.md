# AgentStandards.Templates

Versioned standards content for the `agent-standards` CLI ([AgentStandards.Tool](https://www.nuget.org/packages/AgentStandards.Tool)):
engineering standards, path-scoped rules, skills (`SKILL.md`), specialist agents and project profiles.

You normally do **not** reference this package from a project. The CLI bundles the templates matching its own
version and can fetch other versions of this package for pinning:

```bash
agent-standards update --version 0.2.0                     # from your NuGet sources
agent-standards update --version 0.2.0 --source ./packages  # offline, from a folder containing the .nupkg
```

Files are stored under `agent-standards/` in the package (not `content/`), so referencing the package by mistake
adds no files, assets or runtime dependencies to an application.
