# Manifests

Draft winget manifests for `BenBurge.Wingman`. Not submitted to
`microsoft/winget-pkgs` yet — there is no tagged release for them to point at.

Each release publishes a `.sha256` file alongside the `x64` and `arm64` zips
in the GitHub release. Copy those values into `InstallerSha256` in
`BenBurge.Wingman.installer.yaml`, replacing the placeholder zeros, before
submitting.

Validate locally with:

```
winget validate --manifest manifests/BenBurge.Wingman
```
