---
name: review-dependencies
description: Audit project dependencies for vulnerabilities, outdated or unused packages, licence concerns and unnecessary additions, and recommend safe actions. Use when reviewing dependencies or before adding/upgrading one.
---
# Review dependencies

Read-only unless asked: changing dependencies requires approval.

1. **Inventory** direct and important transitive dependencies from the project's manifests and lock files.
2. **Check vulnerabilities** with the ecosystem's audit tool (for example `dotnet list package --vulnerable`,
   `npm audit`, `pip-audit`) where available, and report the actual output.
3. **Check currency:** outdated packages, end-of-life frameworks and major versions behind.
4. **Check necessity:** unused packages, duplicates, and packages replaceable by built-in functionality.
5. **Check licences** for compatibility with the project's licence and distribution.
6. **Recommend** actions ranked by risk: security fixes first, then end-of-life, then the rest. For each upgrade,
   note breaking changes from release notes and the testing needed.
