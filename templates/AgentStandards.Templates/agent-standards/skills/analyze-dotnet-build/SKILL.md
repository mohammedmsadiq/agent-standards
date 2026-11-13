---
name: analyze-dotnet-build
description: Diagnose and fix .NET build, restore and analyzer problems (errors, warnings, package conflicts, SDK and target-framework issues). Use when dotnet build or restore fails or produces warnings.
---
# Analyse a .NET build

1. **Reproduce:** run `dotnet restore` and `dotnet build` (the same configuration as the failure) and capture the
   exact errors and warnings with their codes.
2. **Check the environment:** `dotnet --info`, `global.json`, installed workloads, and target frameworks.
3. **Classify:** restore/package issues (NU*), compiler errors (CS*), analyzer warnings (CA*, IDE*), SDK or
   MSBuild issues (NETSDK*, MSB*), workload issues.
4. **Fix the root cause** in line with the solution's conventions. Do not suppress warnings or downgrade
   analyzers without approval and a justification. Package, SDK or target-framework changes need approval.
5. **Verify** with a clean build and `dotnet test`.
