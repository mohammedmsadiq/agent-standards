---
description: Rules protecting the public API of reusable .NET libraries.
globs: ["**/*.cs"]
---
# Library public API

- Keep new types `internal` unless they are intended for consumers.
- Every public member has XML documentation.
- Changing or removing public members, or changing their behaviour or exceptions, is a breaking change: flag it
  and get approval.
