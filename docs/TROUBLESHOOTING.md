# Troubleshooting

## The appearance test works, but new mail does not create a card

The appearance test checks the window. It does not create an Outlook notification.

1. Confirm the new message actually arrived in Outlook.
2. Check whether Outlook's ordinary desktop banner appeared.
3. If it did not, check Outlook's Mail/account notification settings and **Windows Settings → System → Notifications → Outlook**.
4. If it did, open Outlook Alert settings and check that monitoring is ready.
5. Check notification access under **Windows Settings → Privacy & security → Notifications**.
6. Send a new message with a different subject after monitoring is ready.

A shared mailbox must generate an Outlook desktop notification. Access through a shared folder alone may behave differently from adding the mailbox as an account.

## The wrong Outlook mailbox triggers the card

The app filters the source application, not the receiving mailbox. Choose the accounts in **New Outlook → Settings → General → Notifications**. Those choices also control Outlook's normal desktop notifications on that profile.

Turn off Calendar and Documents notifications when you want email notifications only.

## The card arrives after Outlook's ordinary banner

Windows controls delivery of notification events. The app's local diagnostic log records separate callback, lookup, UI-queue and popup-construction durations. Creation-to-callback age is approximate and does not measure the exact moment the native Outlook banner became visible.

Use the local log to investigate a continuing delay. Do not post message content or complete mailbox screenshots in a public issue. The source contains no idle scan of every desktop window.

## Nothing opens after signing in

Open **Outlook Alert** from Start once in the Windows account that is signed in. Version 3.2 registers startup independently of notification-monitoring setup. Check the actual state shown in settings:

- **Enabled**: the task is registered for that account. Look for the tray icon after a new Windows sign-in; its settings window can remain hidden.
- **Disabled by user**: use **Open Windows Startup settings** and enable Outlook Alert. Windows does not allow the app to reverse this state itself.
- **Disabled by organization**: the displayed restriction comes from Windows policy.
- **Unavailable**: launch the installed app from Start, then check its diagnostic log and the installer result.

Complete notification permission separately in that account. A registered startup task does not itself grant notification access.

If you selected Exit from the tray, open the app again or sign in again with startup enabled. Closing the settings window leaves the app running; Exit stops it.

## The alert is visible but silent

In settings, choose a tone instead of **Silent** and select **Test sound**. Check the Windows output device, system volume and the app's volume in the volume mixer. Tones are included locally, so playback needs no internet access.

Real alerts have a two-second sound cooldown during bursts. Acknowledging the cards stops playback. A sound failure is recorded in the local diagnostic log and does not stop visual alerts. A successful preview does not verify Outlook notification delivery; send a new email while monitoring is ready.

## Only one monitor shows a card

Use the appearance test with Windows displays configured as **Extend**. Mirrored displays share a desktop surface. Check that Windows detects the other monitors and that they are part of the current session.

If you connected or disconnected a display while an alert was open, acknowledge it and run the test again. Include the display arrangement and scaling values in an issue, rather than screenshots containing private mail.

## Windows blocks installation

Confirm you downloaded the release from this repository and review the publisher notice. The community certificate is not a publicly trusted certificate-authority signature. An organization may require IT approval or prohibit the required package capability.

Do not disable signature checking, change security policy or trust an unrelated certificate to make the installer run. Include the exact installer error when requesting support, after removing personal paths if appropriate.

## Using another Windows account

Notification permission is per user. Sign in to that account, configure New Outlook, open Outlook Alert and complete its first-run permission. See [Installation and Windows users](INSTALLATION.md).
