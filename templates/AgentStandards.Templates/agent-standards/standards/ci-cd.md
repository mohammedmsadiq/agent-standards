# CI/CD

- **Every change is built and tested automatically** before merge: restore, build, test, static analysis,
  and packaging where relevant.
- **Builds are reproducible:** pinned tool and SDK versions, locked dependencies, deterministic output.
- **Fail fast and visibly.** Do not mark failing steps as allowed-to-fail without a tracked reason.
- **Secrets** come from the CI system's protected secret store, scoped to the jobs and environments that need
  them. Never print them; never pass them to untrusted pull-request builds.
- **Least privilege** for pipeline tokens and service connections.
- **Releases and deployments are separate, explicitly approved steps** using protected environments, not a
  side effect of every merge (unless the project has deliberately adopted continuous deployment).
- **Artefacts** are versioned, traceable to a commit, and promoted between environments rather than rebuilt.
- Changing pipelines, environments or deployment configuration requires approval.
