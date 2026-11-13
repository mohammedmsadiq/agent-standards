# Contributing

Thank you for helping improve Agent Standards.

## Prerequisites

- .NET SDK 10.0.100 or later (see `global.json`).
- Bash for `scripts/e2e.sh` (Git Bash works on Windows).

## Build and test

```bash
dotnet build AgentStandards.slnx
dotnet test AgentStandards.slnx
dotnet run --project src/AgentStandards.Tool -- validate --templates templates/AgentStandards.Templates/agent-standards
dotnet pack AgentStandards.slnx -c Release -o artifacts/packages
scripts/e2e.sh
```

`scripts/e2e.sh` installs the packed tool from `artifacts/packages` into a temporary folder, runs it against a fixture
repository offline, consumes the templates package at a different version, and checks that referencing the templates
package adds nothing to an application.

## Repository layout

| Path | Contents |
|---|---|
| `src/AgentStandards.Core` | Engine: templates, profiles, detection, rendering, adapters, planner, manifest, backups, validation |
| `src/AgentStandards.Tool` | `agent-standards` CLI (System.CommandLine) |
| `templates/AgentStandards.Templates/agent-standards` | Canonical template content (single source for both packages) |
| `tests/AgentStandards.Tests` | xUnit tests using temporary fixture repositories |
| `docs/` | Architecture, assistant integration, profile schema and publishing guides |

## Changing templates

- Keep shared knowledge in one place: standards in `standards/`, rules in `rules/`, skills in `skills/`, agents in
  `agents/`. Adapters render assistant-specific formats; do not write per-assistant copies by hand.
- Generic content must stay language-agnostic. Put framework guidance in a profile.
- Run `agent-standards validate --templates <dir>` and the tests.
- See [docs/profiles.md](docs/profiles.md) for the schema.

## Adding an assistant

Implement `IAssistantAdapter` in `src/AgentStandards.Core/Adapters`, register it in `AdapterRegistry`, add
validation for its file formats in `FileValidators`, document its discovery behaviour with sources in
[docs/assistants.md](docs/assistants.md), and add tests.

## Pull requests

- Keep changes focused and include tests.
- Update `CHANGELOG.md` under *Unreleased* and the documentation for user-visible changes.
- Do not claim support for behaviour that is not implemented and tested.
- Never commit secrets or API keys.
