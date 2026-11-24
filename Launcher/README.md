# Machine installation and per-user launcher

The Setup executable installs Outlook Alert for the computer. Its manifest
requests administrator approval, so both normal double-click/UAC and Run as
administrator are supported. It does not mistake the elevated administrator's
profile for every person's Windows account.

Setup verifies the embedded signed MSIX, public leaf certificate, and signed
launcher against release SHA-256 pins. It trusts only that publisher leaf in
LocalMachine / TrustedPeople and provisions the MSIX through Windows DISM.
Payload files remain read-locked while privileged deployment/copy operations
use them. The private signing key is never part of the installer.

The supporting files live in:

`%ProgramFiles%\facoding\OutlookAlert\<package-version>\`

That directory is writable by Administrators/SYSTEM and readable/executable by
Users. It contains the signed launcher, a cached Setup for uninstall, and an
admin-authored `deployment.xml`. The XML records the actual provisioned package
full name, derived family name, application ID, and required version. Setup
refuses unexpected package identities or junction installation directories.

A common Start menu shortcut named **Outlook Alert by facoding** starts the
small launcher normally in the person who clicks it. The launcher checks that
person's registered package family/version. If missing or outdated, it asks
Windows to register the already staged family for that account, verifies the
result, and opens the app through its AppsFolder identity. It does not request
an administrator password, share another user's token, or download any files.

Existing and new users can also receive the provisioned app at next sign-in.
For a profile with an older registered version, opening the common launcher is
the explicit activation/update path. Other already signed-in sessions may keep
their old app until their user opens the launcher or signs in again; Setup does
not force-kill other people's running native apps.

Each person must have New Outlook installed/configured in their Windows
profile, open Outlook Alert once, allow Windows notification access, and enable
its native startup task. Setup does not approve notification access for other
users, copy Outlook accounts/passwords, run a SYSTEM notification service, or
install a second competing common Startup launcher. Disabling the app's native
Startup entry remains under that user's control.

The machine uninstall entry is:

`HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\facoding.OutlookAlert`

Uninstall explicitly confirms removal for all accounts, deprovisions the exact
family, requests removal of its registered packages for all users, removes its
common shortcut/uninstall entry, and schedules deletion of its own supporting
folder after Setup exits. Other signed-in sessions may need to sign out for
Windows to finish package removal. The trusted publisher certificate remains;
removing it is a separate administrator decision documented in the main README.

Both projects compile on the Linux build host, but actual Windows provisioning,
first-logon registration, multi-account activation, notification consent, and
uninstall require a Windows integration test. No system deployment commands
were executed during development.
