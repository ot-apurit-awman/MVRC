# AGENTS

## Build Validation Rule
When validating that the project compiles, use VS Code Tasks (the `/task` workflow) instead of running ad-hoc build commands.

Use one of these tasks:
- `Build MVRC (Debug)`
- `Build MVRC (Release)`

## Why
- Tasks are the canonical build entry points for this repo.
- Tasks use `.vscode/build-mvrc.ps1`, which handles MSBuild discovery and required build properties.
- This avoids command-line quoting and post-build-event issues that can appear with direct MSBuild invocations.

## Agent Checklist
1. Run a build task via `/task`.
2. Confirm task exit code is `0`.
3. Report warnings/errors from task output.
