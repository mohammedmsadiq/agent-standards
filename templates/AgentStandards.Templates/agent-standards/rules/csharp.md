---
description: C# coding conventions for .NET projects.
globs: ["**/*.cs"]
---
# C# files

- Follow `.editorconfig` and the surrounding code; do not reformat unrelated code.
- Respect nullable annotations; avoid the null-forgiving operator unless provably safe.
- Use `async`/`await` end to end and pass `CancellationToken`; never block on tasks.
- Use `ILogger<T>` with structured message templates; never log secrets or personal data.
- Prefer base class library and `Microsoft.Extensions.*` APIs over new third-party packages.
