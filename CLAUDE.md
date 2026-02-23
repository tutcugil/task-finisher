# CLAUDE.md

This file provides guidance to Claude when working with the task-finisher codebase.

## Project Overview

**task-finisher** is a .NET 10 CLI tool that automatically resolves GitHub issues using Claude (Anthropic). It clones a repository, runs an agentic loop to implement changes, commits them to a new branch, and opens a Pull Request.

## Build & Run Commands

```bash
# Run from source
dotnet run --project src/TaskFinisher

# Run tests
dotnet test task-finisher.sln

# Build release
dotnet build task-finisher.sln --configuration Release

# Publish self-contained binary (replace RID as needed)
dotnet publish src/TaskFinisher/TaskFinisher.csproj \
  --configuration Release \
  --runtime linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:PublishTrimmed=true \
  --output ./publish/linux-x64

# Docker
docker build -t task-finisher .
docker run --rm \
  -e GITHUB_TOKEN=ghp_xxx \
  -e GITHUB_REPO=owner/repo \
  -e ANTHROPIC_API_KEY=sk-ant-xxx \
  task-finisher
```

## Project Structure

```
task-finisher/
├── src/
│   └── TaskFinisher/
│       ├── Commands/          # Spectre.Console.Cli command definitions (RunCommand)
│       ├── Configuration/     # AppSettings — runtime configuration model
│       ├── Models/            # Domain models and data transfer objects
│       │   └── Data/          # Data transfer objects (DataGitHubIssue, DataGitHubRepo, etc.)
│       ├── Services/          # Core services (GitHub, Git, Claude, IssueProcessor, Credentials)
│       │   └── Interfaces/    # Service abstractions
│       ├── Tools/             # AgentTools — file-system & shell tools exposed to Claude
│       ├── UI/                # Terminal UI components (repo/issue selectors, result table)
│       ├── Program.cs         # Entry point and DI wiring
│       └── appsettings.json   # Default configuration (model, max iterations, version)
├── tests/
│   └── TaskFinisher.Tests/    # xUnit unit tests (FilesystemToolsTests.cs → AgentToolsTests)
├── .claude/
│   └── launch.json            # Claude launch configuration
├── .github/
│   └── workflows/
│       └── release.yml        # CI/CD: builds and publishes releases on merge to prod
├── Dockerfile                 # Multi-stage Docker build
└── task-finisher.sln          # Solution file
```

## Architecture

- **Entry point**: `Program.cs` — wires up DI (`Microsoft.Extensions.DependencyInjection`) and runs `CommandApp<RunCommand>` via Spectre.Console.Cli.
- **RunCommand**: Orchestrates credential gathering, repo/issue selection, and delegates to `IIssueProcessor`.
- **IssueProcessor**: Coordinates the full workflow — creates GitHub branch, clones repo, runs agent loop, commits, pushes, opens PR, and cleans up.
- **ClaudeAgentService**: Manages the agentic conversation loop with the Anthropic API. Uses exponential back-off for rate-limit retries.
- **AgentTools**: Implements the tools Claude can call (`Read`, `Write`, `Edit`, `MultiEdit`, `Glob`, `Grep`, `Bash`). All paths are sandboxed to the working directory.
- **GitHubService**: Wraps Octokit for branch creation, issue fetching, and PR creation.
- **GitService**: Wraps the `git` CLI for clone, checkout, stage, commit, and push.
- **CredentialService**: Resolves credentials (CLI arg → env var → `~/.config/task-finisher/credentials.json` → interactive prompt).

## Key Dependencies

| Package | Version | Purpose |
|---|---|---|
| `Anthropic` | 12.8.0 | Anthropic Claude API client |
| `Octokit` | 14.0.0 | GitHub API client |
| `Spectre.Console` | 0.49.1 | Rich terminal UI |
| `Spectre.Console.Cli` | 0.49.1 | CLI command framework |
| `Microsoft.Extensions.Hosting` | 9.0.0 | DI and hosting |
| `Microsoft.Extensions.DependencyInjection` | 9.0.0 | Dependency injection |

Test project uses **xUnit**.

## Coding Conventions

- **Language**: C# with `<Nullable>enable</Nullable>` and `<ImplicitUsings>enable</ImplicitUsings>`.
- **Target framework**: `net10.0`.
- **Style**: PascalCase for types and members; `_camelCase` for private fields. Primary constructors used throughout (e.g. `public sealed class GitService(ILogger<GitService> logger)`).
- **Sealed classes**: All service and command classes are `sealed`.
- **Interfaces**: Services are registered and consumed via interfaces in `Services/Interfaces/`.
- **Async**: All I/O is async; methods use `CancellationToken` where appropriate.
- **Logging**: `ILogger<T>` injected via DI; minimum level `Warning` globally, `Information` for `TaskFinisher` namespace.
- **Error handling**: Exceptions propagate to `RunCommand.ExecuteAsync` which renders them with Spectre.Console markup. `MissingCredentialsException` is handled gracefully (non-crash exit).
- **File headers**: No file-level copyright headers; namespace declarations use file-scoped style (`namespace TaskFinisher.Services;`).
- **Alignment**: Vertical alignment of similar declarations is common (e.g. initialiser lists, `switch` expressions).

## Configuration

`src/TaskFinisher/appsettings.json` contains defaults:

```json
{
  "App": {
    "Project": "tutcugil",
    "Name": "task-finisher",
    "Version": "0.0.1",
    "Operation": "ALFA"
  },
  "AppSettings": {
    "MaxAgentIterations": 50,
    "Model": "claude-sonnet-4-5-20250929"
  }
}
```

The `App.Version` value is read by the GitHub Actions release workflow to tag the release.

## Environment Variables

| Variable | Description |
|---|---|
| `GITHUB_TOKEN` | GitHub Personal Access Token (needs `repo` scope) |
| `ANTHROPIC_API_KEY` | Anthropic API key |
| `GITHUB_REPO` | Target repository in `owner/repo` format |
| `ANTHROPIC_MODEL` | Claude model ID (optional override) |

## Testing

Tests live in `tests/TaskFinisher.Tests/`. The main test file is `FilesystemToolsTests.cs` (class `AgentToolsTests`) covering all `AgentTools` methods.

```bash
dotnet test task-finisher.sln
```

Each test creates a temporary directory, runs tool operations, and asserts on the output. The `Dispose()` method cleans up the temp directory.

## Branch & PR Conventions

- Feature branches are named: `task-finisher/issue-{number}-{slug}`
- Releases are triggered by merging a PR into the `prod` branch via `.github/workflows/release.yml`.
- Development work targets the `dev` branch.
