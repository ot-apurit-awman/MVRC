# Contributing to MVRC

All changes must be submitted through a pull request and reviewed by the owner
before they are merged.

## Developer Certificate of Origin

By contributing, you certify that you have the right to submit the work under
this project's license. Sign every commit with the DCO sign-off line:

    git commit -s

The commit must contain a `Signed-off-by: Full Name <email>` trailer. Pull
requests without a valid sign-off are rejected by the DCO status check.

Do not submit My Winter Car, Unity, PlayMaker, MSCLoader, or other proprietary
game files. Keep local game references outside the repository in the folder
specified by `MVRC_GAME_REFS`.

## Framework and language constraints

The project targets .NET Framework 3.5 and uses `LangVersion` 9.0 in
`MyWinterCarVR.csproj`. Builds use the .NET SDK and restore the .NET Framework
3.5 reference assemblies from NuGet; Visual Studio is not required.

Do not use APIs or language features unavailable to the supported runtime and
codebase conventions. In particular, do not add LINQ, `nameof`,
`IsNullOrWhiteSpace`, `Physics.ClosestPoint`, or `Vector3.SignedAngle`.

The configured C# 9.0 compiler supports the syntax already present in the
source: null-conditional `?.`, interpolated strings such as `$"..."`,
expression-bodied members, and `out var`. These compile as language syntax
when using the configured .NET SDK toolchain; targeting .NET Framework 3.5
still limits the runtime APIs available to the code. A full build also requires
the private reference DLLs listed in `README.md`.

## Build commands

Use the VS Code tasks **Build MVRC (Release)** and **Build MVRC (Debug)**. The
canonical script commands are:

    .\.vscode\build-mvrc.ps1 -Configuration Release
    .\.vscode\build-mvrc.ps1 -Configuration Debug -Deploy

`MVRC_GAME_REFS` must point to the local private reference folder. Set
`MVRC_MODS_DIR` when using `-Deploy`. Game files and proprietary assemblies
must never be submitted in a pull request.
