# Documentation

| Document | What it covers |
|---|---|
| [architecture.md](architecture.md) | The system and container diagrams, trust zones, each application's responsibilities, the patterns used and why, and the main request and real-time flows |
| [vertical-slices.md](vertical-slices.md) | One feature slice walked through in each application |
| [demo.md](demo.md) | A guided walkthrough with the seeded demo accounts, and how to reset the demo data |
| [development.md](development.md) | Local setup on macOS and Windows: prerequisites, the local secrets, the database, running and debugging the four applications, tests and resets |
| [configuration.md](configuration.md) | Every setting and environment variable per application, with defaults, sources and secret handling |
| [deployment.md](deployment.md) | The container images, running the Staging stack with Docker Compose, the release step that applies the migrations, and guidance for a production deployment |
| [logging.md](logging.md) | Log locations, file naming, retention, levels, correlation ids and redaction |
| [testing.md](testing.md) | How to run each test suite locally and in the build pipeline, with their prerequisites |
| [api.md](api.md) | The REST API: conventions, the error model and its codes, and every endpoint with examples |
| [database.md](database.md) | Database roles and rights, the schema, constraints, reference data, the demo data, the migration and how to check an applied database |
| [security.md](security.md) | Security objectives, the authentication and session model, real-time connections, request controls, headers, secrets, operator procedures and accepted limitations |
| [limitations.md](limitations.md) | Known limits of the technician app, the dashboard and the API, the small display differences still open, and the improvements planned next |
| [ui.md](ui.md) | The design tokens, fonts, icons, shared components, screens, accessibility and supported browsers of the dashboard and the technician app |
| [screenshots/](screenshots/) | Screenshots of the dashboard, and of the technician app on Android and Windows |
| [third-party-notices.md](third-party-notices.md) | Notices for the fonts, icons and platform packages the applications include |
| [contracts/README.md](contracts/README.md) | The REST and event contracts and how to change them safely |
| [decisions/README.md](decisions/README.md) | Architecture decision records |
