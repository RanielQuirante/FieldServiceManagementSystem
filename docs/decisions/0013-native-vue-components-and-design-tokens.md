# 0013. Native Vue components and design tokens

## Status

Accepted, 2026-10-20.

## Context

The dashboard follows a specific visual design: a deep navy and teal palette with amber accents, two gradients, frosted glass surfaces, the Geist type family, and defined radii, shadows and states. A component kit built on another design language would need overriding on almost every screen, and kits that inject styles at run time force a Content Security Policy with `style-src 'unsafe-inline'` or nonces. A utility-first CSS framework would add build tooling for a small set of components.

## Decision

- No component library and no utility CSS framework.
- The design tokens are CSS custom properties in `src/app/styles/tokens.css`, one per design token, named after the design specification (`--color-fs-*`, `--radius-fs-*`, `--shadow-fs-*`, `--text-fs-*` and the gradient and glass tokens).
- Components use scoped CSS that reads only the tokens. A unit test keeps raw colour and size values out of component styles, and another checks the specified text and background contrast pairs.
- The shared components in `src/shared/ui` are built on native elements: a real `table` for data, the native `dialog` element for confirmations, and native form controls with their constraint validation.
- Glass and gradient surfaces are static CSS in the built stylesheet.
- The production Content Security Policy keeps `style-src 'self'`.
- End-to-end tests audit every route and key state with axe against WCAG 2.2 AA, and add keyboard, reflow, reduced-motion and forced-colours checks.

## Consequences

- The dashboard owns about thirty small components and their tests instead of configuring a kit.
- No style is injected at run time, so the strict Content Security Policy holds.
- The tokens map one to one to the design specification, which makes design review a comparison of names and values.
- The dependency tree stays small: the runtime dependencies are Vue, Vue Router, Pinia, the typed API client and the Socket.IO client.
