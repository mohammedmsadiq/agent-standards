# Dependency management

## Before adding a dependency

Ask for approval, and include this justification:

1. **Is it built in?** Prefer the language, standard library, platform or framework capability.
2. **Is it already in the project?** Reuse an existing dependency that does the job.
3. **Is it healthy?** Active maintenance, recent releases, responsive issue tracker, reasonable adoption.
4. **Is the licence compatible** with the project's licence and distribution model?
5. **What does it cost?** Transitive dependencies, binary size, startup time, platform support, security surface.

## Versions

- Pin or lock versions using the ecosystem's lock mechanism (for example central package management,
  lock files, `requirements.txt` with hashes). Commit lock files.
- Prefer stable releases. Use prereleases only with a stated reason.
- Upgrade deliberately: read release notes, especially for major versions; upgrade one major dependency at a
  time; run the full test suite.
- Do not mix multiple versions of the same library unless the ecosystem requires it.

## Security

- Enable vulnerability auditing in builds and CI where available, and treat known vulnerabilities as defects.
- Remove unused dependencies.
- Never add packages from untrusted or typo-squatted sources; verify package names and publishers.
