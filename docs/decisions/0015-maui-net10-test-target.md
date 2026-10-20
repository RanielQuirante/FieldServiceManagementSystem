# 0015. Technician app tests on a plain net10.0 target

## Status

Accepted, 2026-10-20.

## Context

The technician app's ViewModels, session handling and DTOs need fast unit tests. The app's platform targets (`net10.0-android`, and `net10.0-windows10.0.19041.0` on Windows) cannot run in an ordinary test host, and an emulator or device for every test run is slow and fragile. A separate core library would split each feature across two projects (ADR 0001).

## Decision

- The app's `TargetFrameworks` is `net10.0-android;net10.0`, with the Windows target added on Windows hosts. `OutputType` is `Exe` for the platform targets only.
- `tests/FieldService.Mobile.Tests` targets `net10.0` and references the app project, which resolves to the app's `net10.0` build.
- Code under `Platforms/` belongs to the platform targets only and stays out of the `net10.0` build.
- Device services are reached through MAUI's interfaces, such as `ISecureStorage` and `IConnectivity`, injected into ViewModels and replaced with NSubstitute substitutes in tests.
- The tests run on xUnit v3 with Microsoft.Testing.Platform.

### Evidence

A spike on macOS showed that the test build compiles only the app's `net10.0` target, that a ViewModel with substituted `ISecureStorage` and `IConnectivity` runs in the test host, and that the first test set passes (29 tests, including the Release guard for the API base address). The Android build job runs the same tests on its Linux runner, so the pipeline checks the Linux side on every change.

## Consequences

- One project per app: each page stays next to its ViewModel, and tests need no device or emulator.
- Restoring and building the tests needs the MAUI workload, as building the app does.
- Platform-specific code is tested by running the app on an emulator or device, not by unit tests.
