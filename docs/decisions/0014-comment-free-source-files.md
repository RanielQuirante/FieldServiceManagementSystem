# 0014. Comment-free source files

## Status

Accepted, 2026-10-20.

## Context

The repository keeps every source and configuration file free of comments. Names, small functions, typed results and tests carry the intent of the code, and explanations live where they are maintained as text: the documents in `docs/`, the decision records and the commit history. Comments that drift from the code are not possible when there are none.

Such a rule only holds if a tool checks it, and if every file a generator writes complies too.

## Decision

- **The rule.** No line, block, documentation or markup comments in any tracked source or configuration file: C#, XAML, XML, csproj and props files, JSON, TypeScript, Vue, JavaScript, CSS, SQL, YAML, shell, Dockerfiles, `.editorconfig`, `.gitignore` and `.gitattributes`.
- **Allowed constructs.** C# preprocessor directives such as `#if DEBUG`, `#nullable` and `#pragma warning`, and XML declarations. They are code, not comments.
- **Flagged constructs.** Suppression markers such as `eslint-disable`, `@ts-ignore`, `@ts-expect-error` and `prettier-ignore`, and `#region`.
- **The check.** `infrastructure/scripts/check-no-comments.mjs` applies the rule per file type, ignores comment markers inside string literals (such as `http://`), and prints one line per finding as `path:line:column kind`. It runs locally and in the build pipeline's hygiene job. Licence files and zero-byte files are skipped.
- **Generated files comply by construction:**
  - Entity Framework migrations come from a custom migrations generator, registered through `IDesignTimeServices`, that writes the migration, designer and snapshot files without the generator's header and documentation comments. Regenerating a migration reproduces identical files apart from the timestamp.
  - Hand-added SQL inside migrations carries no `--` comment.
  - The dashboard's generated API types are written to an ignored file and never committed.
  - Every scaffold is stripped of its template comments before it is committed.

## Consequences

- Reviewers read intent from names, tests and documents, and a pull request that adds a comment fails the hygiene job.
- Writing explanatory text means updating a document or a decision record, which keeps it findable.
- Tools that write suppression comments are not used; a rule that needs an exception is changed in its configuration instead.
- New file types must be taught to the scanner before they can be committed, because an unknown type is reported rather than skipped.
