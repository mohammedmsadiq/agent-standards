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

1. Update `VersionPrefix`/`VersionSuffix` in `Directory.Build.props` (both packages always share one version; CI and
   `scripts/e2e.sh` fail otherwise).
2. Move *Unreleased* entries in `CHANGELOG.md` to the new version with the date.
3. Merge to `main` and confirm CI is green on Linux, Windows and macOS.
4. Tag the release commit on `main` and push the tag: `git tag -a v<version> -m "v<version>"` then
   `git push origin v<version>`. Tags are the immutable link between a release and its commit; consider a tag
   protection rule for `v*`.
5. Run **Actions → Release to NuGet.org** with the tag and the word `publish`.
   - The *build* job (no secrets) checks out the tag, verifies it points to a commit on `main` and that the package
     version equals the tag, then builds, validates templates, packs, tests, runs `scripts/e2e.sh` and uploads the
     packages with SHA-256 checksums.
   - The *publish* job waits for approval of the `nuget-org` environment, verifies the checksums, and checks which
     versions already exist on nuget.org: if both exist it fails (bump the version); if one exists (a previous
     partial publication) it pushes only the missing one; otherwise it pushes the templates package, then the tool.
     It never uses `--skip-duplicate`.
   - The run succeeds only after both packages are visible on nuget.org (it waits up to 20 minutes for validation).
6. Create the GitHub release notes from the changelog.

Actions are pinned to full commit SHAs; update them deliberately.

## Manual publishing (alternative)

```bash
dotnet pack AgentStandards.slnx -c Release -o artifacts/packages
scripts/e2e.sh
# NUGET_API_KEY must come from a secure source (password manager, CI secret); do not type it into shell history.
dotnet nuget push artifacts/packages/AgentStandards.Templates.<version>.nupkg --api-key "$NUGET_API_KEY" --source https://api.nuget.org/v3/index.json
dotnet nuget push artifacts/packages/AgentStandards.Tool.<version>.nupkg --api-key "$NUGET_API_KEY" --source https://api.nuget.org/v3/index.json
```

Published versions cannot be deleted from nuget.org, only unlisted. Validate before pushing.
