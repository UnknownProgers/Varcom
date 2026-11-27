# Publishing Varcom

Varcom is one NuGet package: the net8.0 runtime and source generator. The version is set in
Core/Core.csproj; keep README and CHANGELOG in sync. assets/icon.png is embedded in the package.

## Build locally

```sh
dotnet pack Core/Core.csproj -c Release -o artifacts/packages
```

This creates Varcom.<version>.nupkg and matching .snupkg symbols.

## One-time setup

1. Confirm your nuget.org account may publish the Varcom package ID.
2. Set the GitHub repository variable NUGET_USER to your NuGet username.
3. In NuGet Trusted Publishing, configure repository owner UnknownProgers, repository Varcom
   and workflow filename publish.yml. Leave the environment unset; this workflow does not use one.

Authentication uses GitHub OIDC and a short-lived NuGet key, as described in
[NuGet Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing).

## Release

The single .github/workflows/publish.yml workflow runs tests and builds packages on pushes
to master and on pull requests, making them available as workflow artifacts.

After committing and pushing a release candidate, create and push a matching version tag:

```sh
git tag v0.5.0-alpha.1
git push origin v0.5.0-alpha.1
```

A v* tag must exactly match the project version. The workflow tests and packs the tagged
commit, publishes the same packages to NuGet, and creates a GitHub Release with .nupkg and
.snupkg attachments. Versions containing a hyphen create a GitHub prerelease. Reruns skip
existing NuGet versions and update GitHub Release attachments, matching the Injectra workflow.

Ordinary branch pushes and pull requests never publish. No tag or remote release is created
by the local preparation itself.
