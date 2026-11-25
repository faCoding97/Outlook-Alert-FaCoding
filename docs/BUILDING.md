# Building Outlook Alert

The repository contains the app and installer source. Release binaries are published through GitHub Releases, not committed to Git.

## Project layout

| Path | Purpose |
| --- | --- |
| `App/` | WPF desktop app and notification-identity/deduplication logic |
| `Launcher/` | Small .NET Framework launcher that activates the staged app for the current account |
| `Tests.Windows/` | Installer layout and larger-font stress checks on Windows |
| `Installer/` | Windows setup bootstrapper with embedded signed payloads |
| `Tests/` | Portable behavior checks for the notification gate and installer payload checks |
| `Packaging/` | MSIX manifest, public icon assets and package build/signing instructions |
| `docs/` | Installation, privacy, troubleshooting and development notes |

The app targets `net10.0-windows10.0.19041.0`; the released runtime identifier is `win-x64`. Core and Windows Desktop runtime references are pinned to 10.0.15. The installer targets .NET Framework 4.8, which Windows 11 supplies. Building requires the .NET 10 SDK.

## Compile and test

From the repository root:

```powershell
dotnet restore App/OutlookAlert.csproj
dotnet run --project Tests/NotificationGate.Tests.csproj -c Release
dotnet run --project StartupTests/StartupTests.csproj -c Release
dotnet run --project SoundTests/SoundTests.csproj -c Release
dotnet build App/OutlookAlert.csproj -c Release
dotnet build Launcher/OutlookAlert.Launcher.csproj -c Release
dotnet build Installer/OutlookAlert.Setup.csproj -c Release
```

A setup build without signed payloads is a development build and cannot install the app. The final setup must embed a signed MSIX, signed per-user launcher and the matching public certificate with the correct release fingerprints.

Publish the app and its runtime:

```powershell
dotnet publish App/OutlookAlert.csproj -c Release -r win-x64 --self-contained true -o Publish -p:DebugType=None -p:DebugSymbols=false
```

Include the applicable runtime license and third-party notice files in the package.

## Build on Linux

The projects enable Windows targeting for cross-compilation with the .NET 10 SDK. Compilation, portable tests and package verification can run on Linux. Windows UI rendering, installation, permission prompts, notification-event delivery and actual resource usage require a Windows test.

The packaging helper uses Microsoft's MSIX SDK and `osslsigncode`. Its prerequisites and paths are described in [Packaging/BUILDING.md](../Packaging/BUILDING.md). The signing/packing toolchain is a local build dependency and is not included in this repository.

```bash
python3 Packaging/build_package.py --publish Publish --output Payload/OutlookAlert.msix
```

After producing the final signed package:

1. Build and sign `Launcher/OutlookAlert.Launcher.csproj`; place it at `Payload/OutlookAlert-Launcher.exe`.
2. Put the signed MSIX and its public certificate at `Payload/OutlookAlert.msix` and `Payload/OutlookAlert.cer`.
3. Update all three setup release fingerprints from the actual final files.
4. Build the installer again so that it embeds those exact files.
5. Sign the installer and verify the signature and embedded payloads.
6. Test the complete installer and app on Windows before attaching them to a release.

## Signing and package identity

The MSIX `Publisher` must match the signing certificate's subject. A compatible update must preserve the package family identity and increase the package version.

Use a signing key that you control. Private `.key`, `.pfx`, `.p12` and `.pem` files must stay outside Git and release archives. The `.gitignore` excludes the local signing directory and common private-key formats. Public certificate files may be included when needed for a reviewed release.

A self-signed publisher certificate is not a public-CA signature. Setup must obtain informed consent before trusting a publisher certificate and must allow Windows deployment to verify the signed package. Do not disable signature validation or weaken execution policy.

## Release preparation

A release should include the installer, its SHA-256 checksum and notes that distinguish performed checks from unverified Windows behavior. Keep the installer and package versions aligned. Publish binaries as release assets so that source history remains small.

Before publication, check the source and assets for personal mailbox addresses, organization-specific configuration, local paths, private signing material and screenshots containing private information. Documentation examples should use generic accounts such as `alerts@example.com`.

## Windows layout checks

[Windows CI run 45411439320](https://github.com/faCoding97/Outlook-Alert-FaCoding/actions/runs/45411439320) completed successfully for
[source commit 713e339](https://github.com/faCoding97/Outlook-Alert-FaCoding/commit/713e3392c7976cd5afc00fdb5eaf29da2642a7ea) on Windows Server 2025. The app, launcher and
installer source builds passed, followed by all 24 notification-policy checks and
all 9 installer layout cases. The actual alert markup was rendered and the settings button template loaded successfully.

The layout cases combine viewports of 740×710, 640×560 and 460×440 with font factors
of 1.0×, 1.5× and 2.0×. The harness constructs only the setup form, checks labels
and the action area, and renders owned-form PNGs. It does not invoke setup's
entrypoint, provision packages, trust certificates or install anything. Font
stress is not a real per-monitor DPI-transition test.

To run the same layout harness on Windows:

```powershell
dotnet build Tests.Windows/LayoutSmoke.csproj -c Release
.\Tests.Windows\bin\Release\net48\OutlookAlert.LayoutSmoke.exe artifacts/layout
```

Multi-account installation, first-use notification permission, sign-in/startup,
updates across profiles and machine uninstall remain Windows 11 device-validation
items. CI success does not remove the release's pre-release status.
