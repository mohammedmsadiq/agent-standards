# Assistant integration

The adapters were designed from the assistants' documentation as of November 2026. Assistants change quickly; re-check
these sources when updating an adapter.

## Claude Code (`claude`)

Sources: <https://code.claude.com/docs/en/memory>, <https://code.claude.com/docs/en/skills>,
<https://code.claude.com/docs/en/sub-agents>, <https://code.claude.com/docs/en/mcp>.

| Behaviour (documented) | Consequence in this toolkit |
|---|---|
| Loads `CLAUDE.md`/`.claude/CLAUDE.md` and `CLAUDE.local.md` from the working directory and its parents; user file `~/.claude/CLAUDE.md`. | Project block goes in `CLAUDE.md`; global block in `~/.claude/CLAUDE.md`. |
| Reads `AGENTS.md` only when no `CLAUDE.md` exists; recommends `@AGENTS.md` at the top of `CLAUDE.md`. | The managed `CLAUDE.md` block starts with `@AGENTS.md`. `validate` and `doctor` warn when a `CLAUDE.md` lacks it. |
| `@path` imports resolve relative to the importing file, recurse up to four hops, and are ignored inside code spans and fences; imports outside the project prompt for approval. | Only `@AGENTS.md` is imported. Detailed standards are linked, not imported, to keep context small. The global block uses links (`~/.agent-standards/…`) rather than imports to avoid approval prompts. |
| `.claude/rules/*.md` (and `~/.claude/rules/`) with optional `paths:` list of globs; rules without `paths` always load. | Rules render with a `paths:` list. |
| Skills: `.claude/skills/<name>/SKILL.md`, `~/.claude/skills/`; frontmatter `description` recommended, `name` defaults to the folder. | Skills keep `name` = folder and a `description`. |
| Subagents: `.claude/agents/*.md`, `~/.claude/agents/`; `name` and `description` required; `tools` is a comma-separated allowlist. | Read-only agents get `tools: Read, Grep, Glob` (no Bash, Edit or Write). Read-write agents inherit tools. |
| Project MCP servers: `.mcp.json` with `mcpServers`; `${VAR}` expansion; user approval for project servers. | `--mcp` merges profile-declared servers into `.mcp.json`, add-only. User-level MCP settings are never touched. |

## GitHub Copilot (`copilot`)

Sources: <https://docs.github.com/en/copilot/how-tos/configure-custom-instructions/add-repository-instructions>,
<https://docs.github.com/en/copilot/reference/custom-instructions-support>,
<https://code.visualstudio.com/docs/copilot/customization/custom-instructions>,
<https://code.visualstudio.com/docs/copilot/customization/custom-agents>,
<https://code.visualstudio.com/docs/copilot/customization/agent-skills>.

| Behaviour (documented) | Consequence in this toolkit |
|---|---|
| `.github/copilot-instructions.md` is used across Copilot Chat, cloud agent and code review in most IDEs and GitHub.com. | The core policy is rendered into a managed block there, so surfaces that do not read `AGENTS.md` still get it. |
| `AGENTS.md` is read by the cloud agent, code review, Copilot CLI and VS Code chat, but not by every surface (for example GitHub.com chat). Nearest `AGENTS.md` wins. | Project context lives in `AGENTS.md`; `copilot-instructions.md` links to it. |
| `.github/instructions/**/*.instructions.md` with `applyTo` (comma-separated globs) and optional `description`. | Rules render with `applyTo` and `description`. |
| Markdown links in instructions are not guaranteed to be loaded. | Links are treated as on-demand reading; essential rules are in the always-loaded block. |
| Custom agents: `.github/agents/*.agent.md`; `description` required; `tools` list using aliases such as `read`, `search`, `edit`, `execute`. `.chatmode.md` is legacy. | Read-only agents get `tools: ['read', 'search']`. |
| Skills: `.github/skills/<name>/SKILL.md`, personal `~/.copilot/skills/`; `name` lowercase letters, digits and hyphens, ≤ 64 characters, equal to the folder; `description` ≤ 1024 characters. | `validate` enforces these rules for all skills. |
| Personal instructions: `~/.copilot/copilot-instructions.md`, `~/.copilot/instructions/**/*.instructions.md`; personal agents `~/.copilot/agents`. | Global scope writes to these locations. |
| Workspace-root `.mcp.json` is read by VS Code only behind an experimental setting; `.vscode/mcp.json` is the legacy editor format. | `.mcp.json` is shared with Claude Code; `.vscode/mcp.json` is not generated. |

## Other assistants

Any tool that reads `AGENTS.md` (for example, tools following the AGENTS.md convention) receives the shared policy,
project context and links without an adapter. To add first-class support, implement `IAssistantAdapter` and document
its verified behaviour here.
