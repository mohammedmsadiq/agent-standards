---
name: review-pull-request
description: Review a pull request as a whole — purpose, scope, code, tests, risk and release impact — and produce a structured review. Use when asked to review a pull request or merge request.
---
# Review a pull request

Read-only: do not push commits, approve, merge or post comments unless the user asks.

1. **Read the description** and linked issues: what problem it solves and how it was tested.
2. **Check scope:** the change does what it says and nothing unrelated; it is reviewable in size.
3. **Review the code** with the `review-code` approach; use `review-security` for sensitive areas.
4. **Check tests and CI:** new behaviour is tested; CI results are green or failures are explained.
5. **Assess risk:** breaking changes, migrations, configuration or infrastructure changes, rollback plan, and
   release-note needs.
6. **Report:** summary, blocking issues, suggestions, questions, and a recommendation (approve, request changes,
   or comment). If asked to post the review, confirm where it will be posted first.
