# Version control

- **Small, focused commits** that each leave the build working. Separate refactoring from behaviour changes.
- **Commit messages** follow the project's convention (for example Conventional Commits). Summary line in the
  imperative mood, then the reason for the change.
- **Branches** follow the project's model; never commit directly to protected branches.
- **Never rewrite shared history** (force-push, rebase of pushed branches) without explicit approval.
- **Never commit** secrets, credentials, build output, local configuration, or large binaries that belong in
  artefact storage. Keep `.gitignore` current.
- **Pull requests** describe what changed, why, how it was tested, and any risk or follow-up.
- AI assistants commit, push, tag or open pull requests only when the user asks.
