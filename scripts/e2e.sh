#!/usr/bin/env bash
# End-to-end check of the packed artefacts. Requires: packages in $PACKAGES (default artifacts/packages).
#  1. Installs AgentStandards.Tool as a .NET tool from the local folder feed (no network for the tool itself).
#  2. Runs init/status/validate/update/rollback in a fixture repository using the bundled templates, offline.
#  3. Repacks the templates at a different version and installs it through the package path, offline.
#  4. Proves that referencing AgentStandards.Templates from an application adds no files or runtime dependencies.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PACKAGES="${PACKAGES:-$ROOT/artifacts/packages}"
WORK="$(mktemp -d "${TMPDIR:-/tmp}/agent-standards-e2e.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

VERSION="$(ls "$PACKAGES" | sed -nE 's/^AgentStandards\.Tool\.(.+)\.nupkg$/\1/p' | head -1)"
[ -n "$VERSION" ] || { echo "AgentStandards.Tool package not found in $PACKAGES" >&2; exit 1; }
echo "== Using AgentStandards.Tool $VERSION from $PACKAGES"

export AGENT_STANDARDS_HOME="$WORK/home"
export NUGET_PACKAGES="$WORK/nuget-packages"
mkdir -p "$AGENT_STANDARDS_HOME"
# Native path for nuget.config (Git Bash on Windows uses /c/... paths).
PACKAGES_NATIVE="$PACKAGES"
if command -v cygpath >/dev/null 2>&1; then PACKAGES_NATIVE="$(cygpath -w "$PACKAGES")"; fi
cat > "$WORK/nuget.config" <<XML
<configuration><packageSources><clear /><add key="local" value="$PACKAGES_NATIVE" /></packageSources></configuration>
XML

echo "== Package metadata"
for id in AgentStandards.Tool AgentStandards.Templates; do
  nuspec="$(unzip -p "$PACKAGES/$id.$VERSION.nupkg" "$id.nuspec")"
  for tag in '<license type="expression">MIT</license>' '<readme>README.md</readme>' '<authors>' '<description>'; do
    grep -qF "$tag" <<<"$nuspec" || { echo "$id: missing $tag in nuspec" >&2; exit 1; }
  done
done
if unzip -l "$PACKAGES/AgentStandards.Templates.$VERSION.nupkg" | grep -qE ' (content|contentFiles|lib|build|buildTransitive)/'; then
  echo "templates package must not contain content/, lib/ or build assets" >&2; exit 1
fi

dotnet tool install AgentStandards.Tool --version "$VERSION" --tool-path "$WORK/tools" --configfile "$WORK/nuget.config" >/dev/null
TOOL="$WORK/tools/agent-standards"
"$TOOL" --version

echo "== Fixture repository (.NET MAUI + existing CLAUDE.md)"
REPO="$WORK/repo"
mkdir -p "$REPO/src/App"
printf '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><UseMaui>true</UseMaui></PropertyGroup></Project>\n' > "$REPO/src/App/App.csproj"
printf '# Team notes\n' > "$REPO/CLAUDE.md"
cp "$WORK/nuget.config" "$REPO/nuget.config"
cd "$REPO"

"$TOOL" init --dry-run --offline >/dev/null
[ ! -e .agent-standards ] || { echo "dry-run wrote files" >&2; exit 1; }
"$TOOL" init --offline --yes
grep -q '^# Team notes' CLAUDE.md
grep -q '@AGENTS.md' CLAUDE.md
test -f .claude/agents/maui-architect.md
test -f .github/instructions/maui-xaml.instructions.md
"$TOOL" status
"$TOOL" validate
"$TOOL" doctor
"$TOOL" update --offline

echo "== Templates package consumed through the package path at a different version"
dotnet pack "$ROOT/templates/AgentStandards.Templates/AgentStandards.Templates.csproj" -c Release \
  -p:VersionPrefix=99.0.0 -p:VersionSuffix=e2e -o "$WORK/feed" >/dev/null
"$TOOL" update --version 99.0.0-e2e --source "$WORK/feed" --offline --yes
"$TOOL" status --json | grep -q '"version": "99.0.0-e2e"'
"$TOOL" status --json | grep -q '"pinned": true'
"$TOOL" rollback --yes
"$TOOL" status --json | grep -q "\"version\": \"$VERSION\""

echo "== Referencing the templates package adds nothing to an application"
APP="$WORK/app"
dotnet new console -o "$APP" --framework net10.0 >/dev/null
cp "$WORK/nuget.config" "$APP/nuget.config"
dotnet add "$APP" package AgentStandards.Templates --version "$VERSION" >/dev/null
dotnet build "$APP" -c Release >/dev/null
if find "$APP/bin" \( -name '*.md' -o -name 'agent-standards' -o -name 'catalog.json' \) | grep -q .; then
  echo "templates leaked into the application" >&2; exit 1
fi
if grep -q 'AgentStandards' "$APP"/bin/Release/net10.0/*.deps.json; then
  echo "runtime dependency introduced" >&2; exit 1
fi
grep -q 'PrivateAssets' "$APP/app.csproj" || { echo "package was not added as a development dependency" >&2; exit 1; }

echo "== E2E passed"
