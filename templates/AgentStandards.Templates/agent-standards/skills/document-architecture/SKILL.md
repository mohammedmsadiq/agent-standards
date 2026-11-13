---
name: document-architecture
description: Produce or update accurate architecture documentation (overview, components, data flow, decisions) from the actual code. Use when asked to document, explain or diagram a system's architecture.
---
# Document architecture

1. **Survey the code:** solution/project structure, entry points, components, dependencies, data stores, external
   integrations, deployment configuration.
2. **Describe the current implementation** factually: context (users and external systems), containers/components,
   key flows, cross-cutting concerns (configuration, logging, security, error handling).
3. **Separate clearly:** *current implementation*, *recommended standards*, and *proposed changes*. Never present a
   proposal as existing behaviour.
4. **Record decisions** as ADRs (context, decision, consequences) when the reasons are known; mark unknown
   reasons as open questions rather than inventing them.
5. **Use the project's formats:** existing docs folder, Markdown, diagrams as code (for example Mermaid) where used.
6. **Verify** names, paths and versions against the code; link to source locations.
7. Offer to add a short summary to the project context section of `AGENTS.md`.
