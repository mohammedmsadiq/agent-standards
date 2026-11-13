# Publishing to NuGet.org

Nothing is published automatically. CI builds, tests and packs on every change; publishing is a separate, manual
workflow that requires confirmation and an approved environment.

## One-time setup

1. **Reserve ownership.** Sign in to nuget.org with the account (or organisation) that will own
   `AgentStandards.Tool` and `AgentStandards.Templates`. Consider requesting an ID prefix reservation for
   `AgentStandards.*`.
2. **Create a scoped API key** on nuget.org: *Push new packages and package versions*, glob `AgentStandards.*`,
   shortest practical expiry. Never paste it into files, commits, issues or logs.
3. **Store it as an environment secret.** In the GitHub repository settings create an environment named
   `nuget-org`, add required reviewers, restrict it to the `main` branch, and add the secret `NUGET_API_KEY` to the
   environment (not to the repository).
4. **Check metadata before the first release:** `RepositoryUrl` and `PackageProjectUrl` in `Directory.Build.props`
   point to the GitHub repository; add a package icon if desired.

NuGet.org also supports trusted publishing with short-lived tokens from GitHub Actions OIDC, which removes the
long-lived API key; consider switching to it once the repository is public.

## Release checklist

1. Update `VersionPrefix`/`VersionSuffix` in `Directory.Build.props` (both packages share one version).
2. Move *Unreleased* entries in `CHANGELOG.md` to the new version with the date.
3. Merge to `main` and confirm CI is green on Linux, Windows and macOS.
4. Download the `packages` artefact from CI and inspect it locally if desired:
   `dotnet tool install AgentStandards.Tool --tool-path ./t --add-source <folder> --version <version>`.
5. Run **Actions → Release to NuGet.org**, enter the version and type `publish`. A reviewer approves the
   `nuget-org` environment; the workflow rebuilds, tests, runs `scripts/e2e.sh`, and pushes the templates package
   first, then the tool.
6. Tag the release commit (`v<version>`) and create release notes from the changelog.

## Manual publishing (alternative)

```bash
dotnet pack AgentStandards.slnx -c Release -o artifacts/packages
scripts/e2e.sh
# NUGET_API_KEY must come from a secure source (password manager, CI secret); do not type it into shell history.
dotnet nuget push artifacts/packages/AgentStandards.Templates.<version>.nupkg --api-key "$NUGET_API_KEY" --source https://api.nuget.org/v3/index.json
dotnet nuget push artifacts/packages/AgentStandards.Tool.<version>.nupkg --api-key "$NUGET_API_KEY" --source https://api.nuget.org/v3/index.json
```

Published versions cannot be deleted from nuget.org, only unlisted. Validate before pushing.
