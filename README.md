# Rift Revival Launcher

Rift Revival is an unofficial, open-source launcher and mod development project for Interstellar Rift. It creates a separate managed game copy, verifies every recorded file before launch, and keeps the Steam installation and saves outside this repository.

The project is in early development. Power Glove Battery has been tested in gameplay. Backpack Storage is implemented and passes offline validation, but its final mixed-resource build has not completed gameplay testing because Windows Smart App Control rejected the newly generated game executable before startup.

## Build

Requirements:

- Windows 10 or Windows 11
- .NET Framework 4.x, including the 64-bit C# compiler
- PowerShell 5.1 or later
- Internet access to download the pinned Mono.Cecil NuGet package

Run:

```powershell
./scripts/Build.ps1
```

The script verifies the downloaded package hash and writes the launcher plus Mono.Cecil to `artifacts/`. It does not download, include, or start Interstellar Rift.

## Project boundaries

- No game executable, game asset, save, account credential, server secret, or private server configuration belongs in this repository.
- Users must own and install Interstellar Rift separately through its official distribution channel.
- Patch definitions support specific reviewed game builds and refuse unknown executable hashes.
- The launcher never disables Windows security. A blocked build is treated as a deployment failure.

Interstellar Rift and Aluna are properties of their respective owners. This project is unofficial and is not endorsed by Split Polygon.

## Policies

- [Privacy](PRIVACY.md)
- [Security](SECURITY.md)
- [Code signing](CODE_SIGNING_POLICY.md)
- [Contributing](CONTRIBUTING.md)

## Licence

Rift Revival source code is available under the [MIT License](LICENSE). Third-party components retain their own licences under `launcher/assets/`.
