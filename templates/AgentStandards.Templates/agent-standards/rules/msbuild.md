---
description: Rules for .NET project, solution and build configuration files.
globs: ["**/*.csproj", "**/*.fsproj", "**/*.vbproj", "**/*.props", "**/*.targets", "**/*.sln", "**/*.slnx", "**/global.json", "**/nuget.config", "**/NuGet.config"]
---
# Build configuration

- Changing target frameworks, SDK versions, package sources or adding/upgrading packages requires approval.
- Use central package management (`Directory.Packages.props`) when the solution already does.
- Do not disable warnings, analyzers or package auditing without a justification comment.
- Never add credentials or private feed tokens to configuration files.
