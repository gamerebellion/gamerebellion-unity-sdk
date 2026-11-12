# Contributing to the Unity SDK

This guide covers the Unity package `com.gamerebellion.sdk`. Shared rules for every GameRebellion repository are in the organization [contributing guide](https://github.com/gamerebellion/.github/blob/main/CONTRIBUTING.md).

## Reporting a bug

Open an issue on this repository. Include:

- SDK version and engine version, for example Unity 2022.3 and `com.gamerebellion.sdk` v0.1.0
- Target platform: iOS, Android, or the Editor
- Steps to reproduce
- Expected behavior and actual behavior
- Relevant logs. Enable `GRLogDrainer` for SDK-level logs.

## Changes

Branch from `upm`. The name starts with `feat/`, `fix/`, `docs/`, `chore/`, `ci/`, or `refactor/`.

Test the change on at least one target platform.

C# follows the Microsoft C# conventions. Use `#region` sparingly.

Commits follow [Conventional Commits](https://www.conventionalcommits.org/). Examples:

```text
feat(unity): add WebGL transport adapter
fix(batcher): flush events on application pause
docs: update Quick Start for UPM installation
```

Open the pull request against `upm` and use the organization pull request template.
