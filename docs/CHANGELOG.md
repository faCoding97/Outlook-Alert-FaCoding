# Changelog

## 3.0.1 — 2026-11-24 (pre-release)

- Simplifies the alert and settings copy for general use.
- Removes redundant alert captions and keeps a small facoding signature below the acknowledgement button.
- Shortens setup copy and removes mailbox-specific guidance from the installer.
- Replaces the unpublished 3.0.0 release candidate.

Verification: [Windows CI run 45408497239](https://github.com/faCoding97/Outlook-Alert-FaCoding/actions/runs/45408497239) passed source builds, 24 policy checks, 9 installer layout cases and rendering of the actual alert markup for source commit `3dd4400`. Real multi-user installation remains a device-validation item.

## 3.0.0 — 2026-11-24 (pre-release)

- Adds a small facoding signature to the alert and creator details to setup.
- Replaces fixed installer positions with wrapping, scrolling layouts and a visible action footer.
- Adds an Administrator installation flow for the computer, with per-user app registration at launch.
- Adds a common Start menu launcher that activates/updates the app in each caller's own account.
- Adds explicit all-account uninstall and documents separate per-user notification/startup permission.
- Replaces organization-specific guidance with generic account selection instructions.
- Adds public installation/privacy/build documentation and Windows layout checks.
- Publishes source without an open-source license, as selected by the author.

Verification: [Windows CI run 45404539302](https://github.com/faCoding97/Outlook-Alert-FaCoding/actions/runs/45404539302) passed the app/launcher/installer
source builds, all 24 notification-policy checks and all 9 installer layout cases
for commit `94f348c` on Windows Server 2025.

Real Windows 11 multi-profile installation, notification consent, sign-in/startup,
profile updates and uninstall remain device-validation items. The release stays a
pre-release; CI did not exercise those privileged installation paths.

## 2.1 — earlier desktop build

- Replaces the ordinary per-event full notification-list read with lookup of the specific Added notification ID.
- Removes the intentional 150 ms event debounce.
- Bounds and coalesces event handling and recent duplicate history.
- Adds metadata-only timing records for investigating processing delays.
- Changes the card text to “You have a new email.” and “Please check your Outlook inbox.”

## 2.0 — earlier desktop build

- Introduces the packaged native Windows notification listener and exact New Outlook identity filter.
- Adds the light, rounded multi-display alert and shared acknowledgement.
- Adds setup, notification permission, tray controls and native automatic-startup support.
