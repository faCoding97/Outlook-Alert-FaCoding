# Changelog

## 3.2.0 — 2026-11-25 (pre-release)

- Adds five included alert tones: Chime, Bell, Double beep, Soft ping and Urgent, plus Silent.
- Saves each user's sound choice and adds a sound preview. Real alerts play once per burst, with a two-second cooldown; acknowledgement stops playback.
- Registers automatic startup independently of notification-monitoring setup and declares startup enabled for a newly launched packaged app.
- Displays the actual Windows startup state, refreshes it when settings regain focus, and adds enable, disable and Windows Startup settings actions.
- Respects startup changes made by the Windows user or organization policy.
- Preserves the multi-account installer, shared Start menu entry and per-user notification permission.

Temporary monitoring failures retain the saved monitoring preference so the next launch retries it. The packaged runtime remains on 10.0.15.

Verification: 117 portable notification, startup, settings and sound checks passed; Windows-targeted source compilation passed. Actual Windows audio playback, notification delivery and startup after sign-in remain device-validation items.

## 3.1.0 — 2026-11-24 (pre-release)

- Updates the alert styling and shortens the alert, settings and setup copy.
- Credits facoding and KYCDD while keeping the app available to other New Outlook users.
- Uses **Outlook Alert** as the visible app and shared Start menu name.
- Replaces the older **Outlook Alert by facoding** shared shortcut during upgrade.

Verification: [Windows CI run 45411439320](https://github.com/faCoding97/Outlook-Alert-FaCoding/actions/runs/45411439320) passed source builds, 24 policy checks, 9 installer layout cases, alert rendering and settings button-template loading for source commit `713e339`. The public release upload is pending. Windows 11 multi-account installation, shortcut migration, notification permission, sign-in/startup and uninstall remain device-validation items.

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
