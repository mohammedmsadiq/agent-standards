---
name: review-public-api
description: Review changes to a reusable library's public API for breaking changes, design-guideline compliance, documentation and versioning impact. Use before releasing a library or when public types or members change.
---
# Review a library's public API

1. **List public API changes** (added, removed, changed types and members), using API baseline or package
   validation tooling where the project has it.
2. **Classify compatibility:** source-breaking, binary-breaking or behavioural changes; additive changes.
3. **Check design:** naming and overloads follow the .NET design guidelines; minimal surface; sealed/internal by
   default; asynchronous APIs accept `CancellationToken`.
4. **Check documentation:** XML docs on every public member; examples where helpful.
5. **Determine the version bump** under semantic versioning and the release-note entries needed.
6. **Report** breaking changes first, with migration guidance for consumers.
