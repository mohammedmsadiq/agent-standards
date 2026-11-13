# Profiles and template schema

## Template layout

A template set is a directory (or the `agent-standards/` folder inside a templates package):

```
catalog.json                    { "schemaVersion": 1, "description": "...", "version": "optional, for directories" }
core/policy.md                  always-on operating policy and rules
core/agents.md                  AGENTS.md managed block
core/agents-seed.md             project-context section written once below a new AGENTS.md block
core/claude.md                  CLAUDE.md managed block (starts with @AGENTS.md)
core/copilot.md                 .github/copilot-instructions.md managed block
core/global.md                  global (~/.claude, ~/.copilot) managed block
standards/<id>.md               detailed standards; must start with "# Title"
rules/<id>.md                   path-scoped rules (frontmatter: description, globs)
skills/<name>/SKILL.md          skills (frontmatter: name = folder, description); other files in the folder are copied
agents/<id>.md                  agents (frontmatter: name = file name, description, access: read-only | read-write)
profiles/<id>.json              profiles
```

Core templates may use these placeholders: `{{policy}}`, `{{profiles}}`, `{{standards_index}}`, `{{rules_index}}`,
`{{skills_index}}`, `{{agents_index}}`, `{{templates_version}}`, `{{assistants}}`, `{{skill_locations}}`,
`{{agent_locations}}`, `{{rule_locations}}`, and in `core/global.md` also `{{assistant}}`, `{{skills_dir}}`,
`{{agents_dir}}`. Unknown placeholders fail validation.

## Profile schema

```json
{
  "schemaVersion": 1,
  "id": "webapi",
  "displayName": "Web API",
  "description": "REST/HTTP API services built with ASP.NET Core.",
  "extends": ["aspnetcore"],
  "detect": {
    "all":  [ { "file": "**/*.csproj", "contains": "Microsoft.NET.Sdk.Web" } ],
    "any":  [ { "file": "**/*.cs", "contains": "[ApiController]" } ],
    "none": [ ]
  },
  "standards": ["webapi"],
  "rules": ["api-endpoints"],
  "skills": ["design-api-endpoint"],
  "agents": ["api-designer"],
  "mcpServers": { }
}
```

- `id` must equal the file name. `extends` may list several parents; cycles and unknown parents are errors.
- Items from parents come first; duplicates are removed. All referenced items must exist.
- `detect` is optional. A profile matches when every `all` condition matches, at least one `any` condition matches
  (if any are listed), and no `none` condition matches. A condition is a glob (`**`, `*`, `?`, `{a,b}`) relative to
  the repository root, optionally with case-insensitive `contains` text. Detection scans up to six directory
  levels, skips build output, dependency and VCS folders, and never follows symbolic links. When several profiles
  match, profiles extended by another match are dropped. Without a match, `generic` is used.
- `mcpServers` entries use the Claude Code / Copilot `.mcp.json` server shape. Values in `env` and `headers` must
  reference environment variables (`${VAR}`); `command` must not be an absolute path.

## Custom standards

```bash
cp -r templates/AgentStandards.Templates/agent-standards my-standards
# edit, add profiles/my-team.json, etc.
agent-standards validate --templates my-standards
agent-standards init --templates my-standards --profile my-team
```

To distribute them, pack the folder like `templates/AgentStandards.Templates` under your own package ID and use
`--package-id <id> --source <feed>`. The manifest remembers the package ID or directory for later updates.
