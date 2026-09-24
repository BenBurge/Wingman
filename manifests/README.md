# Manifests

Draft winget manifests for `BenBurge.Wingman`. Not submitted to
`microsoft/winget-pkgs` yet.

The installer manifest points at the per-user Inno Setup installers
(`wingman-v<version>-<rid>-setup.exe`, built by `tools/Build-Installer.ps1`),
so winget installs Wingman the same way running the setup by hand does.

Each release tag runs `tools/Update-Manifest.ps1`, which rewrites
`PackageVersion` in all three manifest files and, in
`BenBurge.Wingman.installer.yaml`, the `InstallerUrl` and `InstallerSha256`
for each architecture from the installers' `.sha256` files the release produced. Run it
by hand the same way:

```
pwsh tools/Update-Manifest.ps1 -Version 0.1.0 -Sha256Dir .\artifacts
```

`-Sha256Dir` is a folder containing `wingman-v<version>-win-x64-setup.exe.sha256`
and `wingman-v<version>-win-arm64-setup.exe.sha256`, each one line of the form
`<hash>  <filename>` as GitHub Actions writes them. The script is pure
PowerShell (no YAML module, no `winget validate`), so it also runs on Linux.

The `release` workflow (`.github/workflows/release.yml`) runs this script
against the hash files it just downloaded and uploads the updated
`manifests/` folder as the `winget-manifest` artifact. It does not commit the
changes back to the repo. Submission to `microsoft/winget-pkgs` stays
manual: download that artifact from the release run, review it, and open the
PR yourself.

Validate locally with:

```
winget validate --manifest manifests/BenBurge.Wingman
```
