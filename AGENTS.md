# Repository Guidelines

## Project Structure & Module Organization
- `src/Helios365.Core`: Domain models, repositories, and services shared across the solution.
- `src/Helios365.Functions`: Azure Functions (isolated) for alert ingestion/orchestration. Config: `host.json`, `local.settings.json.example`.
- `src/Helios365.Web`: Blazor Server web application for dashboard and management UI. Config: `appsettings.json`, `appsettings.Development.json`.
- `tests/*`: xUnit test projects per module (e.g., `Helios365.Core.Tests`, `Helios365.Functions.Tests`).
- `infrastructure/`: Bicep/ARM for cloud resources. Use for provisioning and CI/CD deployments.
- Solution entry: `Helios365.sln`.

## Build, Test, and Development Commands
- Restore: `dotnet restore` (from repo root).
- Build (Debug): `dotnet build Helios365.sln -c Debug`.
- Test (all): `dotnet test Helios365.sln -c Debug`.
- Run Web (Blazor): `dotnet run --project src/Helios365.Web/Helios365.Web.csproj`.
- Run Processor (functions): Prefer Azure Functions Core Tools from `src/Helios365.Functions`: `func start` (requires Core Tools) or `dotnet run` for isolated worker.

## Coding Style & Naming Conventions
- C# 10+; 4‑space indentation; UTF‑8; end files with newline.
- Types/methods: PascalCase. Locals/params: camelCase. Interfaces: `I` prefix (e.g., `IAlertRepository`).
- Organize code by feature folder (Models/Repositories/Services/Activities/Triggers).
- Format: `dotnet format` before commits (optional but recommended).

## Testing Guidelines
- Framework: xUnit with Moq for mocking.
- Name tests by behavior: `MethodName_State_ExpectedResult` (see `tests/Helios365.Core.Tests/Models/AlertTests.cs`).
- Run all tests via `dotnet test`. Aim for meaningful coverage on domain logic and Blazor components.

## Commit & Pull Request Guidelines
- Commits: short, imperative subject (≤72 chars), body for context when needed (e.g., “Refactor repository interfaces for pagination”).
- Link issues in body (`Fixes #123`). Group related changes; avoid mixing refactors with features.
- PRs: include summary, screenshots for UI changes, test evidence (commands/output), and deployment notes if infra changes.

## Security & Configuration Tips
- Do not commit secrets. Use `local.settings.json` (excluded) for local dev and Key Vault/App Settings in cloud.
- Keep environment‑specific settings in `appsettings.Development.json` and deployment parameters in `infrastructure/`.

