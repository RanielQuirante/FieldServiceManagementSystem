# 0002. Solution file format: slnx

## Status

Accepted, 2026-10-20.

## Context

The four .NET projects need one solution file, so that editors open them together and `dotnet build` can build them in one command. The classic `.sln` format is verbose, full of project-type GUIDs and prone to merge conflicts. The .NET 10 SDK reads and writes the XML-based `.slnx` format natively.

## Decision

- `FieldService.slnx` at the repository root, created with `dotnet new sln` on the .NET 10 SDK, lists the API, the technician app and the two .NET test projects.
- The three npm packages are not part of the solution. Each keeps its own `package.json` and `package-lock.json`.
- Build jobs for the API, the notification service and the dashboard build their own projects directly and never restore the whole solution, so they need no MAUI workload.

## Consequences

- The solution file is short, readable and easy to review in a diff.
- Tools that open the solution must support `.slnx`. The .NET 10 SDK does, and so do current releases of the common .NET editors.
- `dotnet sln FieldService.slnx list` shows the four projects; adding or removing a .NET project changes one line.
