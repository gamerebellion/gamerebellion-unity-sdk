# Contributing to the Unity SDK

This guide covers the Unity package `com.gamerebellion.sdk`. Shared rules for every GameRebellion repository are in the organization [contributing guide](https://github.com/gamerebellion/.github/blob/main/CONTRIBUTING.md).

## Reporting a bug

Open an issue on this repository with the bug form. Include:

- SDK version and Unity version, for example `com.gamerebellion.sdk` 1.1.0 on Unity 6000.3. The package supports Unity 2021.3 and later.
- How you installed the SDK: UPM from the git URL, or the `.unitypackage`
- Target platform: Android, iOS, Windows, macOS, Linux, or the Editor
- Steps to reproduce
- Expected behavior and actual behavior
- Relevant logs. For SDK-level logs, initialize with `isDebug: true` and add the `GRLogDrainer` component to a GameObject in your first scene. The SDK's messages then appear in the Console. Remove your API key before you paste them.
- For an event that doesn't arrive or arrives wrong: the event in **Window → GameRebellion → Live events**, with the SDK's answer.
- For the AI agent tools (MCP): the agent you use, and `Library/GameRebellion/relay.log` from your project.

## Changes

Branch from `upm`. The name starts with `feat/`, `fix/`, `docs/`, `chore/`, `ci/`, or `refactor/`.

`upm` holds the released package, and each release replaces its contents with a package built from the SDK source. A maintainer carries an accepted change into that source, and it ships with the next release.

Test the change on at least one target platform. If it touches the GameRebellion window or the MCP tools, test it in the Editor too.

Keep the code compiling on Unity 2021.3, the oldest version the package supports.

Add a `.meta` file for every new file and folder. A package installed from git is read-only, so Unity can't create them.

C# follows the Microsoft C# conventions. Use `#region` sparingly.

Commits follow [Conventional Commits](https://www.conventionalcommits.org/). Examples:

` ` `text
feat(editor): add a copy button to Live events
fix(ios): report purchases restored at launch
docs: document isDebug in the README
` ` `

Open the pull request against `upm` and use the organization pull request template.
Two things before merging:

upm is replaced on every release, so this file also has to go into the package source (unity/Assets/GameRebellion/), or the next release deletes it.
It needs a CONTRIBUTING.md.meta next to it. Without one, every project that installs the package from git shows a "no meta file" warning.
