# User interface

This document describes the dashboard's user interface: its design tokens, fonts, icons and shared components, the accessibility target and how it is checked, and the supported browsers. The technician app follows the same design language; its theme, controls and screens are described in [Technician app](#technician-app).

## Contents

- [Design language](#design-language)
- [Design tokens](#design-tokens)
- [Fonts](#fonts)
- [Icons](#icons)
- [Shared components](#shared-components)
- [Pages and routes](#pages-and-routes)
- [Dashboard](#dashboard)
- [Technicians](#technicians)
- [Cancelling a job](#cancelling-a-job)
- [Inspection report](#inspection-report)
- [Live updates](#live-updates)
- [Accessibility](#accessibility)
- [Supported browsers](#supported-browsers)
- [Technician app](#technician-app)

## Design language

The dashboard uses one light theme: deep navy and teal gradients for the brand surfaces, frosted glass cards over a soft backdrop, and amber as the single accent. Colour never carries meaning alone: every status and priority also has a text label, and every icon sits next to text or inside a control with an accessible name.

## Design tokens

Every colour, gradient, shadow, radius, spacing step and type role is a CSS custom property in `src/FieldService.Web/src/app/styles/tokens.css`. Components use only these properties in scoped component styles; there is no CSS framework.

| Family | Count | Holds |
|---|---|---|
| `--color-fs-*` | 90 | Ink, surface, brand, status and priority colours, including the focus colours |
| `--text-fs-*` | 68 | The 15 type roles, each as size, line height, weight and letter spacing |
| `--shadow-fs-*` | 61 | Elevation and component shadows |
| `--fs-gradient-*` | 26 | Brand, hero, chip and tile gradients |
| `--fs-glass-*` | 18 | Glass surfaces: fill, border, highlight and blur per surface |
| `--spacing-fs-*` | 16 | The spacing scale in pixels |
| `--radius-fs-*` | 13 | Corner radii |
| `--blur-fs-*` | 4 | Backdrop blur strengths |
| Others | 14 | The page backdrop, gloss and glow layers, motion durations, the skeleton and row-flash effects, and the two font stacks |

`fonts.css` declares the font faces and `base.css` sets the document defaults: the type scale, the focus ring, reduced-motion and forced-colour rules, and the fallbacks for browsers without `backdrop-filter`.

Two test suites guard the tokens:

- `token-usage.spec.ts` fails on any literal colour, gradient or shadow colour outside `tokens.css`, on any reference to an undefined property, and on any `url()` other than the local font files.
- `contrast.spec.ts` computes the contrast of every text and icon pair the design uses, compositing glass layers over the darkest backdrop they can sit on, and fails below the WCAG 2.2 AA minimums.

## Fonts

| Family | Weights | Files |
|---|---|---|
| Geist | 400, 500, 600, 700 | `src/FieldService.Web/src/app/fonts/Geist-*.woff2` |
| Geist Mono | 400, 500, 600 | `src/FieldService.Web/src/app/fonts/GeistMono-*.woff2` |

Geist Mono sets job numbers, references and other tabular values. The fonts come from release `v1.8.1` of the Geist font project, are used unmodified, and are licensed under the SIL Open Font License 1.1; the licence text is `src/FieldService.Web/src/app/fonts/OFL.txt`, and the notices are in [third-party-notices.md](third-party-notices.md). The files are served from the dashboard's own origin with `font-display: swap`; the dashboard requests no font from another site.

## Icons

The 42 icons are stroke drawings on a 24-unit grid, kept as path data in `src/FieldService.Web/src/shared/ui/icons.ts` and drawn by `AppIcon`. Icons are decorative (`aria-hidden="true"`): the text beside them or the control around them carries the meaning. Eleven icons share path data with Lucide or Feather; their notices are in [third-party-notices.md](third-party-notices.md).

## Shared components

The components live in `src/FieldService.Web/src/shared/ui`.

| Component | Role |
|---|---|
| AppShell | The page frame: skip link, sidebar with the brand lockup and navigation, account panel with sign-out, top bar with breadcrumbs, and the main region |
| PageHeader | Page title, subline and actions, with a loading form |
| SectionCard | A content card; glass by default, with a plain variant |
| AppButton | Primary, secondary, quiet and danger buttons, with a busy state that keeps its look |
| IconButton | A button with an icon only and an accessible name |
| TextField, TextArea, SelectField | Form controls with label, optional marker, help text, error text and their `aria-describedby` links |
| SegmentedControl | A single choice among a few options, such as priority |
| FilterChipGroup | Status filter chips with counts |
| DataTable | A table that scrolls inside its own region, with loading, empty and no-match rows |
| Pagination | Previous and next with the current range |
| StatusChip, PriorityLabel | A job's status and priority, each with text and colour |
| CountCapsule | A small count beside a label |
| Avatar | Initials for a person |
| IconTile | An icon on a tinted tile |
| AppIcon | One icon from the icon set |
| Banner | Error and information messages inside a page |
| LoadingState, EmptyState, ErrorState | The three page states, with skeleton blocks, a message and a retry action |
| Toast, ToastHost | Short notices at the bottom of the window; an error toast shows a reference only when the API's log holds it (the failed request's own id, or the error report's id once the API has accepted it), and an event toast tells what changed on a job |
| LivePill | The live-update connection state in the top bar |
| ConnectionBanner | The Reconnecting, Paused, Back online and Refresh failed messages of live updates |
| KpiTile | A summary figure with its icon, label and subline, optionally a link; used on the dashboard and the inspection report |
| ConfirmDialog | A modal dialog for an action that needs confirmation, with the page kept behind it; used to cancel a job |

## Pages and routes

| Route | Page | Access |
|---|---|---|
| `/sign-in` | Sign-in, with an optional return path | Public |
| `/` | Dashboard | Administrators |
| `/jobs` | Job list with search, status and priority filters and paging, 12 jobs per page | Administrators |
| `/jobs/new` | New job form | Administrators |
| `/jobs/:jobId` | Job details with the assignee and the status history | Administrators |
| `/jobs/:jobId/inspection` | The read-only inspection report | Administrators |
| `/technicians` | Technicians with their shift and workload, filters and search | Administrators |
| Any other path | Not found, with a link to the dashboard | Administrators |

Signing in without a return path opens the dashboard. Page titles read "<page> · FieldService Admin". Every page has loading, empty, error and not-found states as they apply, and focus moves to the page heading after each navigation.

## Dashboard

The dashboard at `/` gives the day at a glance. Its figures come from one summary request for the dispatcher's local day, from midnight to the next midnight, and from the technician list.

- **Hero band:** a greeting for the time of day with the administrator's name, the date, and one sentence on the open jobs and how many still need a technician, beside two figure tiles. Below 560 pixels of width it becomes a compact band.
- **Jobs by status:** six cards (Unassigned, Assigned, In progress, On hold, Completed today and Cancelled today), each with a subline such as the next start, the number of technicians at work or the oldest hold, and each opening the job list filtered to its status. The cards always form full rows: six, three or two across.
- **Needs a technician:** the ten unassigned jobs that start first, each with a link to assign it, and "View all" when more are waiting.
- **Live activity:** the six newest history entries of the day as sentences, such as "Tom Becker put JOB-1044 on hold", with the job number linking to the job. Entries that a live update adds are marked New for 12 seconds.
- **Technician workload:** each technician with their shift, open jobs and current or next job. Below 640 pixels the table becomes a list.
- **States:** each part has its loading, empty and error form. One Try again retries the summary, and a separate one retries the workload.

The dashboard refreshes both requests on every live event and reconnect ([Live updates](#live-updates)), and the summary again at local midnight.

## Technicians

`/technicians` lists every technician with their trade, shift, open jobs, current or next job, and phone. An inactive technician reads "Inactive".

- **Filters:** All, On shift and Off shift, each with its count. The counts ignore the search.
- **Search:** by name, email, trade, or three or more digits of the phone number.
- **Address:** the filter and the search are kept in the page address, so a link reopens the same view.
- **Narrow screens:** below 900 pixels the table becomes a list, and the phone number is a link that starts a call.

## Cancelling a job

An open job's page offers Cancel job. The dialog shows the job's status and technician and what cancelling means, and asks for a reason of up to 500 characters, with a count from 400.

- A reason typed in the dialog is kept if the dialog closes without sending. A click outside the dialog does nothing, and Escape is ignored while the request is running.
- After a cancellation, the page becomes read-only, and a notice under the title says when and by whom the job was cancelled, with the reason. Every cancelled job's page shows this notice.
- When the job changed in the meantime, the dialog stays open with the reason and the refreshed status, and a second press sends the new version. When someone else cancelled the job first, or the job was completed, the page says so above the notice.

The technician app shows a cancelled job under Completed, read-only, with the reason under "Cancelled on …", or "Cancelled at …" for a job cancelled today ([Screens](#screens)).

## Inspection report

The report shows a job's checklist as the technician answered it. In the job list, a completed job's title opens its report; a completed job's page links to it with "Inspection report", and the report links back with "Job details".

- **Summary tiles:** the result counts ("8 Pass", "1 Fail", "1 N/A" with the template and item count), the time on site from the first start to the completion, and the completion time and technician. For a job that is not completed, the tiles show the answers saved so far, the time since the start, and the last save.
- **Results table:** one row per checklist item, in order, with the result, every reading with its label and unit ("Suction 118 psi" over "Discharge 285 psi"), and the note. An unanswered item reads "Not answered". The table scrolls inside its own region on narrow screens.
- **Technician notes**, a **Job details** card and the full **Status history**.
- **Not completed:** a notice says the job has not started, has not been submitted yet, or was cancelled.
- **Live:** when the job's status changes, for example when the technician submits the inspection, the report refreshes in place with the rest of the page's live updates ([Live updates](#live-updates)), with no reload. Draft saves send no event, so a report open on a job in progress shows new answers after the next status change, a reload or a reconnect.

Everything on the report is read-only and rendered as text.

## Live updates

While an administrator is signed in, the dashboard keeps a live connection to the notification service and refreshes what it shows when a job changes elsewhere. Every change is fetched from the API; the dashboard never copies data from the notification itself ([ADR 0010](decisions/0010-refetch-on-event-and-reconnect.md)).

**Where each part shows**

| Page | Live pill | Connection banner | Event toasts | Row flash |
|---|---|---|---|---|
| Dashboard | Yes | Yes | Yes | No; new Live activity entries are marked New |
| Jobs | Yes | Yes | Yes | Yes |
| Job details, Inspection report | Yes | Yes | Yes | No; the page refreshes in place |
| Technicians | Yes | Yes | Yes | No; the rows refresh in place |
| New job, Not found | Yes | No | No | No |

**Connection states**

| State | Live pill | Banner | When |
|---|---|---|---|
| Connecting | "Connecting…" | None | After sign-in or a page load, until the first connection |
| Live | "Live · updated" with the time of the last refresh | None, or Back online for 8 seconds after a reconnect that had shown a banner | Connected, and the page's data refreshed |
| Reconnecting | "Reconnecting…" at once | "Reconnecting" after 3 seconds without a connection | The connection dropped or an attempt failed |
| Paused | "Live updates paused" | "Live updates are paused. Showing data from {time}." with Refresh now | 30 seconds without a connection |

- The 3-second delay keeps routine reconnects quiet: the notification service ends each connection after 15 minutes, and a reconnect usually takes well under a second, so only the pill changes.
- One banner shows at a time, in a slot at the top of the page that announces each new banner politely. Refresh failed appears when a refresh of the whole page fails and leaves when a later one succeeds; everything already shown stays.
- Refresh now and Try again refresh everything the page shows and reconnect when the page is not connected. Focus moves only when the banner that holds it is replaced.

**Background refreshes** replace rows in place, keyed by job, with no skeleton, busy state or loading announcement, so focus, hover and the scroll position stay. A failed background refresh keeps what is shown.

**Event toasts**

- After the changed job is fetched, its newest history entry gives one toast, for example "Priya Natarajan started work" with "Standby generator load test is now In progress." and the meta line "JOB-1049 · just now". A hold or cancellation reason follows in quotation marks, cut at 80 characters.
- No toast shows for the signed-in administrator's own changes, for an entry already shown, after a failed or `404` fetch, or above the limit of 60 event requests a minute.
- Toasts appear at the bottom of the window. From 600 pixels wide, up to three stack, oldest first. Below 600 pixels, one event toast shows at a time and a new one replaces it, with at most two toasts in all. Each stays 8 seconds of visible time, pauses while hovered or focused, and can be dismissed. Each is announced once, politely, as sentences; event toasts carry no alert role.

**Row flash.** On the job list, the changed job's row flashes for 12 seconds of visible time, 4 seconds longer than its toast, so the whole row with its new status is visible once the toast has gone. On a job's own page, the header, details, status history and assign panel refresh in place instead.

**Preferences.** With reduced motion, toasts and the flash appear and leave without animation. In forced colours, a toast is a solid surface with a border and no blur.

## Accessibility

The target is WCAG 2.2 level AA. The dashboard is built and tested for:

- **Keyboard use:** every control is reachable in a logical order, a skip link leads to the main region, and focus never lands on the page body after an action such as clearing filters or retrying a load.
- **Visible focus:** a 2-pixel focus ring with an offset on every control, which wins over the error look of an invalid field and stays visible on dark surfaces and in forced colours. A notice never covers the focused element entirely.
- **Contrast:** text and icons meet the AA minimums on every surface, glass included.
- **Reflow:** every page works at 320 CSS pixels wide without horizontal page scrolling; wide tables scroll inside their own region and keep their messages in view.
- **Forms:** labels, required and optional markers, help text and server errors are linked to their fields, and focus moves to the first invalid field.
- **Status messages:** errors are announced as alerts, once each, and loading states are announced without moving focus.
- **Dialogs:** the cancel dialog moves focus to its reason field and keeps the page behind it inert. Closed without sending, it returns focus to Cancel job; after a cancellation, or when a live refresh removes the button, focus moves to the page heading.
- **Text that must stay whole:** times, dates, durations and job numbers never break inside.
- **User preferences:** reduced motion stops animations, reduced transparency replaces glass with solid surfaces, and forced colours keep text, focus and gradient titles visible.

Automated checks run an accessibility audit, keyboard, reflow and preference checks on every page and state in Chrome. Checks with Firefox, Safari, VoiceOver and browser zoom at 200 and 400 percent are carried out by hand.

## Supported browsers

The dashboard supports the current and previous major versions of Chrome, Edge, Firefox and Safari. It relies on `backdrop-filter` (with the prefixed form for older Safari), `:has()`, container queries and `color-scheme`; where `backdrop-filter` is missing, glass surfaces fall back to solid fills. Balanced and pretty text wrapping are progressive enhancements.

The dashboard runs under a strict content security policy described in [security.md](security.md#headers-and-content-security-policy): no inline styles or scripts and no third-party origin.

## Technician app

The technician app uses the same tokens, type roles, fonts and icons as the dashboard, expressed as .NET MAUI resources.

### Theme

| File in `src/FieldService.Mobile/Shared/Theme` | Holds |
|---|---|
| `Colors.xaml`, `Brushes.xaml` | The colour tokens and the gradient and glass brushes built from them |
| `Typography.xaml` | The type roles as label styles, with the Geist and Geist Mono families |
| `Metrics.xaml` | Spacing, radii, control heights and stroke widths |
| `Styles.xaml` | Implicit and named styles for the controls |
| `Icons.xaml` | The icon path data, the same drawings as the dashboard |

Icons are drawn by the `Icon` control on a 24-unit grid, through a strict parser of the SVG path data, so the app and the dashboard render the same shapes. The six font files are listed in [third-party-notices.md](third-party-notices.md).

The splash screen shows the FieldService mark on deep navy, and the launcher icon is the mark on the brand gradient; both are SVG files in `src/FieldService.Mobile/Resources`. Each screen's dark header runs under the status bar; once the content scrolls, an opaque navy band keeps the status-bar icons readable.

### Controls

The controls live in `src/FieldService.Mobile/Shared/Controls`: `AppHeader` (list, pushed and desktop top-bar variants), `PrimaryButton`, `FormField` (single-line and multi-line), `Banner` (information, offline, error and conflict), `JobCard`, `StatusChip`, `PriorityLabel`, `SegmentedControl`, `CountCapsule`, `Avatar`, `InfoRow`, `IconTile`, `LinkCard`, `Timeline`, `EmptyState`, `BottomSheet` (a fixed header above its scrolling content), `OptionRow`, `ActionDock`, `AnswerControl` (Pass, Fail and N/A), `CheckboxRow` and `Icon`. `FormField` also has a reading variant with a unit suffix and an "Optional" marker.

### Screens

| Screen | Route | Content |
|---|---|---|
| Sign in | `//sign-in` | Email and password with field errors, an error banner, and the "Your session ended." notice after an expiry or a revocation |
| My jobs | `//jobs` | Active and Completed segments, jobs grouped by day, pull to refresh, the account sheet with sign-out. Completed holds completed and cancelled jobs, newest first; a card adds the date to its time when the slot falls on another day than its group |
| Job details | `jobs/details` | Status, priority, schedule, customer, site, access notes, contact, equipment, description, timeline and checklist; an Assigned job shows the schedule first, with Navigate and, on Android where the device can dial and the job has a contact phone, Call, and Start job; on Windows the contact's phone number can be selected and copied instead; an In progress or On hold job offers Update status, and an On hold job shows its hold reason under the status; an In progress job also offers Start inspection or Continue inspection with the checklist's progress; a cancelled job shows "Cancelled on …" (or "Cancelled at …" when cancelled today) with the reason under it and offers no action, only Navigate and, on Android, Call |
| Update status | sheet over Job details | Only the changes the server allows for the job: Put on hold, with a required reason of up to 500 characters and a count from 400, or Resume. A conflict closes the sheet, keeps the reason, reloads the job and explains what changed. Complete job appears when the checklist is complete and opens Review and submit |
| Inspection | `jobs/inspection` | Every checklist item with Pass, Fail and N/A, readings with their units (two decimals at most), and a required fault note on Fail; N/A hides the readings. The progress ("6 of 10 answered") and the result counts sit at the top. Answers are kept in memory while the app runs and saved when the technician leaves the screen and before Review and submit; a conflict reloads and keeps the technician's changes. On hold, cancelled and submitted checklists are read-only |
| Review and submit | `jobs/review` | The time on site, the result counts, the failed items with their notes, optional technician notes of up to 2,000 characters, and the site-safety confirmation, unchecked each time the screen opens. Submitting completes the job, returns to My jobs with a confirmation, and leaves the job and its checklist read-only |

Every screen has loading, empty, error and offline states. An error shows a reference for support and a Try again button. When the API cannot be reached, the app keeps the jobs it has loaded and says when they were loaded. On Windows, the app counts the device as online only while Windows reports internet access.

On Windows, at 900 units wide or more, My jobs shows the list and the selected job's details side by side. Crossing 900 in either direction closes an open sheet without sending a change, or, while a change is being sent, once its request ends, and moves keyboard focus to the account button, or to the in-page Back on a job opened at phone width.

### Accessibility

- Every tappable element is at least 48 units high and wide. A test reads the XAML and fails on any interactive style or control below 48.
- Every control that carries meaning has a screen-reader description, such as "Active, 3, selected" for a segment. Icons beside text are hidden from screen readers.
- A job card is one screen-reader item with the role and action of a button, named by its job number, title, status, priority, schedule, customer and site.
- Times, short dates, durations and job numbers never break across lines; a time range breaks only after its dash.
- A cancelled job's status line and reason are read as one item.
- Status and priority always have a text label, as in the dashboard.
- Each option in the Update status sheet is one screen-reader item with the role of a radio button and its checked state. A status change is announced when it succeeds, and focus moves to the next action.
- When a sheet closes, its keyboard closes with it, its fields stop taking input, and the sheet's title stays in view while the keyboard is open.
- On Android, the page behind an open sheet is hidden from screen readers and is read again once the sheet closes. Closing a sheet without a change, with Cancel, a tap outside it or Back, returns focus to the button that opened it. On a phone, the action bar stays above an open keyboard, so focus can return to Update status.
- My jobs' title and each day group are headings. On Android 9 and later, the list header's date, title, account button, sync line and Refresh are separate screen-reader items, in the order they appear.
- On Android, choosing a segment or Refresh keeps screen-reader focus on it while the list reloads, and touching a button's icon reads the button.
- Both My jobs segments stay usable while a list loads, and a banner is not read again while it clears.
- On Windows, an open sheet keeps keyboard and Narrator focus inside it: focus moves into the sheet when it opens, Tab and Shift+Tab stay in the sheet, and Esc closes it and keeps a typed reason. The account sheet cannot be closed while a sign-out runs, on either platform.
- On Windows, Narrator reads each job card by its content and each day group as a heading, the arrow keys move between cards without selecting them, and the status options and the site-safety confirmation work from the keyboard as radio buttons and a check box.
- When a conflict or a refused submission moves focus, keyboard focus moves with it.
- Keyboard focus shows as one 2-unit ring outside the control, on Windows and, in Android's keyboard mode, on option rows, check box rows and job cards.
- Each answer option is one screen-reader item with its selected state. Choosing a result announces it with the new progress, and a submission is announced on My jobs.
- A field error moves focus to its field and scrolls it, with its message, into view above the bottom action bar.
- Icons beside text that can wrap stay level with the first line, at every text size.
- At large text sizes on narrow phones, the answer options drop their icons together and then stack as full-width rows, the My jobs segments, the two dock buttons and the status and priority chips stack in rows, and button labels wrap instead of being cut.
