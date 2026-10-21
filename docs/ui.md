# User interface

This document describes the dashboard's user interface: its design tokens, fonts, icons and shared components, the accessibility target and how it is checked, and the supported browsers. The technician app follows the same design language; its notes are added when its screens are built.

## Contents

- [Design language](#design-language)
- [Design tokens](#design-tokens)
- [Fonts](#fonts)
- [Icons](#icons)
- [Shared components](#shared-components)
- [Pages and routes](#pages-and-routes)
- [Accessibility](#accessibility)
- [Supported browsers](#supported-browsers)

## Design language

The dashboard uses one light theme: deep navy and teal gradients for the brand surfaces, frosted glass cards over a soft backdrop, and amber as the single accent. Colour never carries meaning alone: every status and priority also has a text label, and every icon sits next to text or inside a control with an accessible name.

## Design tokens

Every colour, gradient, shadow, radius, spacing step and type role is a CSS custom property in `src/fieldservice-web/src/app/styles/tokens.css`. Components use only these properties in scoped component styles; there is no CSS framework.

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
| Geist | 400, 500, 600, 700 | `src/fieldservice-web/src/app/fonts/Geist-*.woff2` |
| Geist Mono | 400, 500, 600 | `src/fieldservice-web/src/app/fonts/GeistMono-*.woff2` |

Geist Mono sets job numbers, references and other tabular values. The fonts come from release `v1.8.1` of the Geist font project, are used unmodified, and are licensed under the SIL Open Font License 1.1; the licence text is `src/fieldservice-web/src/app/fonts/OFL.txt`, and the notices are in [third-party-notices.md](third-party-notices.md). The files are served from the dashboard's own origin with `font-display: swap`; the dashboard requests no font from another site.

## Icons

The 42 icons are stroke drawings on a 24-unit grid, kept as path data in `src/fieldservice-web/src/shared/ui/icons.ts` and drawn by `AppIcon`. Icons are decorative (`aria-hidden="true"`): the text beside them or the control around them carries the meaning. Eleven icons share path data with Lucide or Feather; their notices are in [third-party-notices.md](third-party-notices.md).

## Shared components

The components live in `src/fieldservice-web/src/shared/ui`.

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
| Toast, ToastHost | Short notices; an error toast shows the request's reference so support can find it in the logs |

## Pages and routes

| Route | Page | Access |
|---|---|---|
| `/sign-in` | Sign-in, with an optional return path | Public |
| `/jobs` | Job list with search, status and priority filters and paging, 12 jobs per page | Administrators |
| `/jobs/new` | New job form | Administrators |
| `/jobs/:jobId` | Job details with the assignee and the status history | Administrators |
| Any other path | Not found | Administrators |

`/` opens the job list. Page titles read "<page> · FieldService Admin". Every page has loading, empty, error and not-found states as they apply, and focus moves to the page heading after each navigation.

## Accessibility

The target is WCAG 2.2 level AA. The dashboard is built and tested for:

- **Keyboard use:** every control is reachable in a logical order, a skip link leads to the main region, and focus never lands on the page body after an action such as clearing filters or retrying a load.
- **Visible focus:** a 2-pixel focus ring with an offset on every control, which wins over the error look of an invalid field and stays visible on dark surfaces and in forced colours. A notice never covers the focused element entirely.
- **Contrast:** text and icons meet the AA minimums on every surface, glass included.
- **Reflow:** every page works at 320 CSS pixels wide without horizontal page scrolling; wide tables scroll inside their own region and keep their messages in view.
- **Forms:** labels, required and optional markers, help text and server errors are linked to their fields, and focus moves to the first invalid field.
- **Status messages:** errors are announced as alerts, and loading states are announced without moving focus.
- **User preferences:** reduced motion stops animations, reduced transparency replaces glass with solid surfaces, and forced colours keep text, focus and gradient titles visible.

Automated checks run an accessibility audit, keyboard, reflow and preference checks on every page and state in Chrome. Checks with Firefox, Safari, VoiceOver and browser zoom at 200 and 400 percent are carried out by hand.

## Supported browsers

The dashboard supports the current and previous major versions of Chrome, Edge, Firefox and Safari. It relies on `backdrop-filter` (with the prefixed form for older Safari), `:has()`, container queries and `color-scheme`; where `backdrop-filter` is missing, glass surfaces fall back to solid fills. Balanced and pretty text wrapping are progressive enhancements.

The dashboard runs under a strict content security policy described in [security.md](security.md#headers-and-content-security-policy): no inline styles or scripts and no third-party origin.
