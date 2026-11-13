# Publishing to NuGet.org

Nothing is published automatically. CI builds, tests and packs on every change; publishing is a separate, manual
workflow that requires confirmation and an approved environment.

## One-time setup

1. **Reserve ownership.** Sign in to nuget.org with the account (or organisation) that will own
   `AgentStandards.Tool` and `AgentStandards.Templates`. Consider requesting an ID prefix reservation for
   `AgentStandards.*`.
2. **Configure a Trusted Publishing policy** on nuget.org (*Trusted Publishing* in the account menu) for owner
   `mohammed.sadiq`: repository `mohammedmsadiq/agent-standards`, workflow file `release.yml`, environment
   `nuget-org`, package scope `AgentStandards.*`. No API key is created or stored.
3. **Create the GitHub environment.** In the repository settings create an environment named `nuget-org`, add
   required reviewers and restrict it to the `main` branch. It needs no secrets.
4. **Check metadata before the first release:** `RepositoryUrl` and `PackageProjectUrl` in `Directory.Build.props`
   point to the GitHub repository; add a package icon if desired.

### Authentication

The publish job (and only that job) has `id-token: write`. After environment approval, the pinned
[`NuGet/login`](https://github.com/NuGet/login) action exchanges the run's GitHub OIDC token for a short-lived
NuGet API key (user `mohammed.sadiq`), which `dotnet nuget push` consumes. NuGet validates the repository, workflow
file and environment against the policy above. The legacy `NUGET_API_KEY` secret is no longer used; delete it from
the `nuget-org` environment if present. The build job has no token access.

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
   - The *publish* job waits for approval of the `nuget-org` environment, obtains a short-lived key via Trusted Publishing, verifies the checksums, and checks which
     versions already exist on nuget.org: if both exist it fails (bump the version); if one exists (a previous
     partial publication) it pushes only the missing one; otherwise it pushes the templates package, then the tool.
     It never uses `--skip-duplicate`.
   - The run succeeds only after both packages are visible on nuget.org (it waits up to 20 minutes for validation).
6. Create the GitHub release notes from the changelog.

Actions are pinned to full commit SHAs; update them deliberately.

## Re-running a failed release

The workflow file comes from the branch it is dispatched from (use `main`); the tag input only selects the source
that is checked out and built. After a failed publish, fix the workflow on `main` and re-run with the same tag if no
package reached nuget.org: the tag's commit need not contain the workflow fix, and the duplicate check pushes only
what is missing. Never move or delete a tag; if the source must change, bump the version.

## Manual publishing (alternative)

```bash
dotnet pack AgentStandards.slnx -c Release -o artifacts/packages
scripts/e2e.sh
# Manual pushes need a NuGet API key from a secure source (password manager); do not type it into shell history.
# Prefer the release workflow, which uses Trusted Publishing.
dotnet nuget push artifacts/packages/AgentStandards.Templates.<version>.nupkg --api-key "$NUGET_API_KEY" --source https://api.nuget.org/v3/index.json
dotnet nuget push artifacts/packages/AgentStandards.Tool.<version>.nupkg --api-key "$NUGET_API_KEY" --source https://api.nuget.org/v3/index.json
```

Published versions cannot be deleted from nuget.org, only unlisted. Validate before pushing.
