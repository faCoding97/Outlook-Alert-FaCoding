# Privacy and data handling

Outlook Alert is a local desktop notification helper. It does not connect to an email server or perform mailbox synchronization.

## Windows notification permission

Windows grants the notification listener access at system level. That permission is broader than one application, so the app applies its own exact source-identity filter.

The filter discovers the installed Microsoft New Outlook package and its application user model IDs. A notification can trigger an alert only when its application ID matches that allowlist. Words such as “Outlook” in a Telegram message, WhatsApp message or notification title are not used to identify the source.

The app uses the accepted notification's ID and creation time to suppress duplicates. It does not extract the notification's title or body, and it does not read email content, senders, recipients or attachments.

## Local state

The current implementation keeps each user's monitoring preference, selected sound, startup preference and a bounded diagnostic log in that user's application-data area. Windows manages notification-access and actual startup permissions separately for each profile. The five alert tones are included in the app and play locally; the app does not record microphone audio.

The log contains:

- App startup and monitoring status.
- Counts of discovered Outlook identities and detected notifications.
- Exception types and error codes.
- Processing-duration measurements used to investigate delays.

It does not contain email text, subjects, sender lists, passwords, access tokens or complete notification objects. The implementation bounds recent duplicate-tracking state and rotates the diagnostic log.

## Network and accounts

The app has no mailbox authentication flow and does not request your email password. Its code does not send notification data to a server, include advertising or include an analytics client. Opening Outlook or the project's GitHub page delegates that action to the corresponding installed application or browser.

## Several Windows accounts

Each user grants notification access and configures Outlook in their own Windows session. Machine-wide availability of app files does not grant one account access to another account's notification stream or mailbox.

## Sharing reports

Review a diagnostic report before posting it in an issue. Keep personal mailbox information, account names, access tokens and message screenshots out of public issues. A report from this app should contain only operational metadata, but Windows or installer error messages can contain local file paths.
