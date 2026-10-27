# Known limitations

This page lists the known limits of FieldService as it stands, and the small display differences that are still open. Each entry says what happens and, where it helps, what to do instead.

## Technician app

### Offline detection on Windows

The technician app relies on the operating system to report whether the device is online. On Windows it counts the device as online only while Windows reports internet access, and as offline otherwise. On a network that Windows reports as having no internet access, the app therefore shows its offline banner even when the FieldService API can be reached on that network, for example an API hosted on an office network with no route to the internet, or a network that blocks the connectivity check Windows uses. While offline, the app shows the jobs it loaded earlier in the session and keeps checklist answers and notes on the device, but it cannot sign in, start a job, change a job's status, or review and submit a job. It returns to normal by itself as soon as Windows reports internet access again. On Android the app counts the device as offline only when it has no network connection.

### No Call button on Windows

On Windows the technician app does not offer a Call button. The contact's phone number is shown on the job and can be selected and copied.

### Starting with a saved session on Windows

On Windows the technician app has no splash screen. When it starts with a saved session, the sign-in screen shows until the session is restored, then My jobs opens; on a local network this takes under a second. Anything typed on the sign-in screen in that moment is discarded.

### The app's data folder on Windows

The Windows app is not packaged, so its data folder is `AppData\Local\User Name\io.github.ranielquirante.fieldservice.tech`. "User Name" is the framework's default publisher name for an unpackaged app.

### Screen readers on Android 7.0 to 8.1

On Android 7.0 to 8.1 (API 24 to 27), TalkBack reads the My jobs header (the date, the title and the sync line) as one item, and reads the day groups without the heading role, because the Android property that gives text inside a list item its own screen-reader stop exists only from Android 9 (API 28). All text is read and every control can be reached.

### Signing out while the API cannot be reached

When the technician signs out and the sign-out request fails (no connection, a timeout or a server error), the app still removes its session from the device and returns to sign-in. The session on the server then stays valid until it expires (after 7 days without use, and 30 days at most). Only someone who already holds that session's token could use it. To end it at once, an administrator can revoke the technician's sessions as described in [security.md](security.md#revoking-a-users-sessions).

### Very narrow windows on Windows

The Windows layouts are designed for windows at least 320 units wide. Narrower windows keep working, and every control stays reachable by scrolling and Tab, but the layout is not designed for them.

### Small display differences still open

These do not block any task and are planned as improvements:

- On Windows, the first line of text in multi-line fields sits about 3 pixels lower than in the design.
- When offline, the job pane's disabled Start job keeps its normal label colour instead of the disabled colour.
- In Android's keyboard mode, Job details, the checklist and Review and submit open with focus on the bottom action rather than on Back.
- In the two-pane layout on Windows, the account sheet opens at full width instead of as a centred panel.
- At the largest Android font size, the inspection's count pills keep a fixed height, so their labels sit low.
- On narrow Android phones, the placeholder of a two-up reading field can be cut.
- After a job is submitted from a list that was scrolled down, the success message sits above the first group, out of view.

## Dashboard and API

### Personal data in client error reports

The dashboard sends a short report to the API when an unexpected error happens in the browser. The dashboard and the API remove quoted text, email addresses, tokens and query strings before a report is logged, and the dashboard never puts form values or API response data into a report. No pattern can reliably find a name or an address that appears without quotes, so if a browser's own error message contains one, it can reach the API log. See [security.md](security.md#client-error-reports).
