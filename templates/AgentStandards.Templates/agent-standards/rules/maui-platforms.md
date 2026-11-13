---
description: Rules for platform-specific .NET MAUI code and manifests.
globs: ["**/Platforms/**"]
---
# MAUI platform code

- Prefer cross-platform APIs; keep platform code behind an interface consumed by shared code.
- Changes to `AndroidManifest.xml`, `Info.plist`, entitlements, signing or provisioning require approval.
- Request runtime permissions with a clear, user-facing reason, and handle denial gracefully.
