# Install for every Windows account

Version 3.2 installs Outlook Alert for the computer, with a separate app session and
notification permission for each person. Windows 11 x64 and New Outlook are required.

## Install once as Administrator

1. Download the installer from [GitHub Releases](https://github.com/faCoding97/Outlook-Alert-FaCoding/releases).
2. Double-click it and accept Windows administrator approval, or use **Run as administrator**.
3. Read the publisher notice, select the consent checkbox, and choose **Install for all users**.
4. Finish setup. In your own normal Windows session, open **Outlook Alert** from Start.

Setup verifies its embedded signed package, publisher certificate and launcher. It
trusts the exact publisher leaf in the computer's Trusted People store, provisions
the package with Windows deployment services, and creates a common Start menu entry.
It does not install the app only into the administrator profile used for approval.
The self-contained app needs no separate .NET runtime download.

The publisher certificate is self-signed rather than publicly CA-trusted. Setup does
not add a root certificate authority or disable Windows signature checks. On a managed
computer an administrator or organizational policy may prevent installation.

## First use in each account

Each person opens **Outlook Alert** normally, without Run as administrator.
The small launcher registers or updates the already staged package for that caller,
then opens the app. New and existing profiles can also receive the provisioned app at
their next sign-in. If activation is deferred by Windows, sign out and sign in again.
Other already signed-in sessions may keep their older app until that person opens
the shared launcher or signs in again.

Then, separately in each Windows account:

1. Install/configure New Outlook for that account if it is not already available.
2. Open Outlook Alert and select **Enable monitoring**.
3. Allow Windows notification access and wait for the ready status.
4. Check that the displayed automatic-startup state is enabled. Startup registration is attempted even if monitoring setup is incomplete.
5. Choose a sound and select **Test sound**. The choice is saved for that account.
6. Send a fresh email to verify detection. The appearance test checks the window only.

The app declares automatic startup enabled and registers the task on its first launch. It must be opened once in each account. If Windows shows **Disabled by user**, use **Open Windows Startup settings** and enable Outlook Alert there. If organization policy disables it, the app reports that restriction. Its enable action does not override either restriction. The disable action turns startup off for the current account.

Notification permission, Outlook sign-in, selected mailboxes, app settings and startup
state belong to each user. Administrator installation cannot approve notification
access for another person. There is no SYSTEM notification service or shared mailbox
password store. The resident app belongs to that user's interactive session.

## Choose which mail accounts notify

In Outlook, open **Settings > General > Notifications**. Expand account selection,
enable Mail for the desired accounts, and turn off Calendar/Documents for email-only
alerts. The app filters the exact New Outlook source identity; Windows notifications
do not reliably identify the receiving mailbox or distinguish mail from calendar.

A shared mailbox must produce its own Outlook desktop notifications. See Microsoft's
[shared mailbox guidance](https://support.microsoft.com/en-us/outlook/sharing/manage-shared-mailbox-settings-in-new-outlook).

## Update

Run the newer machine installer as Administrator; no manual uninstall is needed.
Each user then opens the common Start menu entry to activate the new staged release
for their account. Compatible package identity preserves supported per-user settings.
The native startup task remains controlled by that person and Windows policy.

Version 3.1 uses **Outlook Alert** as the shared Start menu name and replaces the
older **Outlook Alert by facoding** shortcut. A separately pinned shortcut may retain
its previous label; use the current Start entry when creating a new pin.

## Remove for everyone

In **Windows Settings > Apps > Installed apps**, choose **Outlook Alert**
(the machine installer entry), then Uninstall. Approve Administrator access and the
explicit all-account removal confirmation. This removes provisioning, requests removal
of the exact registered package family for every user, and removes the common Start
entry and machine support files. Other sessions may need to sign out for Windows to
finish package removal.

Removing only the per-user MSIX entry does not remove machine provisioning; the common
launcher or a later sign-in can register it again. To stop alerts for one person while
others keep using the app, disable that person's Startup entry and exit their tray app.

The publisher certificate remains in Trusted People. An administrator may remove its
exact fingerprint when it is no longer needed; see [privacy and signing](PRIVACY.md).

## Verification scope

[Windows CI run 45411439320](https://github.com/faCoding97/Outlook-Alert-FaCoding/actions/runs/45411439320) passed for
[source commit 713e339](https://github.com/faCoding97/Outlook-Alert-FaCoding/commit/713e3392c7976cd5afc00fdb5eaf29da2642a7ea) on a Windows Server 2025 runner. It built the app,
launcher and installer source, passed 24 notification-policy checks, and passed all
9 installer viewport/font-stress cases. It also rendered the actual alert markup
and loaded the settings button template.

Those checks did not install the release or exercise UAC, package provisioning,
several Windows accounts, notification consent, startup after sign-in, shortcut
migration or machine uninstall. Those paths still need a Windows 11 device test.
Those results apply to the earlier 3.1.0 source. Version 3.2.0 adds sound and startup checks; its actual Windows audio and sign-in behavior still require device validation and it remains a pre-release.
