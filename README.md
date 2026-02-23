# task-finisher

An AI worker for your tasks which described as Github issues on your repository.

**task-finisher** is a CLI tool that automatically resolves GitHub issues using an AI agent (Claude by Anthropic). Point it at a repository, select one or more open issues, and it will clone the repo, implement the required changes, commit them to a new branch, and open a Pull Request — all without any manual coding.

---

## Features

- 🤖 **AI-powered** — Uses Claude (Anthropic) to understand and implement issue requirements
- 🐙 **GitHub integration** — Fetches open issues, creates new issues, and opens Pull Requests via the GitHub API
- 🖥️ **Interactive & non-interactive** — Guided terminal UI for local use; fully headless for CI/CD
- 🔐 **Secure credential management** — Credentials resolved from CLI args → env vars → saved file → interactive prompt; never committed to disk
- 🧠 **Configurable model** — Choose from Claude Sonnet, Opus, or Haiku; or supply a custom model ID
- 🐳 **Docker support** — Pre-built Dockerfile for containerised, non-interactive operation
- 🔁 **Multi-repo sessions** — Switch between repositories without restarting the tool
- ⚡ **Rate-limit resilience** — Automatic exponential back-off on Anthropic API rate limits
- ✏️ **Issue creation** — Create new GitHub issues directly from the interactive menu and process them immediately

---

## Requirements

| Requirement | Details |
|---|---|
| [.NET 10 SDK](https://dotnet.microsoft.com/download) | Required to build and run from source |
| [Git](https://git-scm.com/) | Must be available on `PATH` (used for clone/commit/push) |
| GitHub Personal Access Token | Needs `repo` scope to read issues, create issues, and open PRs |
| Anthropic API Key | Used to call the Claude API |

---

## Installation

### From source

```bash
git clone https://github.com/tutcugil/task-finisher.git
cd task-finisher
dotnet run --project src/TaskFinisher
```

### Build a self-contained binary

```bash
dotnet publish src/TaskFinisher/TaskFinisher.csproj \
  --configuration Release \
  --runtime linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:PublishTrimmed=true \
  --output ./publish/linux-x64
```

Replace `linux-x64` with your target RID (`linux-arm64`, `win-x64`, `osx-x64`, `osx-arm64`).

### Docker

```bash
docker build -t task-finisher .

docker run --rm \
  -e GITHUB_TOKEN=ghp_xxx \
  -e GITHUB_REPO=owner/repo \
  -e ANTHROPIC_API_KEY=sk-ant-xxx \
  task-finisher
```

---

## Usage

### Interactive mode (recommended for local use)

```bash
task-finisher
```

The tool will walk you through:
1. Entering / confirming your GitHub token
2. Entering / confirming your Anthropic API key
3. Picking a Claude model
4. Selecting a repository (filtered to those with open issues)
5. Choosing an action: browse issues, create a new issue, switch repository, or exit
6. Selecting one or more issues to process
7. Reviewing and confirming before work begins

### Non-interactive mode (CI/CD)

```bash
task-finisher --repo owner/repo --non-interactive
```

All credentials must be supplied via environment variables or flags when running non-interactively.

### Common options

| Flag | Description |
|---|---|
| `--repo owner/repo` | Target repository (skips the interactive repo picker) |
| `--token ghp_xxx` | GitHub Personal Access Token |
| `--anthropic-key sk-ant-xxx` | Anthropic API key |
| `--model claude-sonnet-4-5-20250929` | Claude model to use |
| `--max-iterations N` | Maximum agent turns per issue (default: `50`) |
| `--non-interactive` | Disable all interactive prompts |
| `--reset` | Clear saved credentials and start fresh |

### Environment variables

| Variable | Description |
|---|---|
| `GITHUB_TOKEN` | GitHub Personal Access Token |
| `ANTHROPIC_API_KEY` | Anthropic API key |
| `GITHUB_REPO` | Target repository in `owner/repo` format |
| `ANTHROPIC_MODEL` | Claude model ID (optional) |

---

## How it works

1. **Credential resolution** — Token, API key, and model are resolved (CLI arg → env var → saved `~/.config/task-finisher/credentials.json` → interactive prompt).
2. **Issue selection** — Open issues are fetched from the selected GitHub repository and presented in an interactive list. You can also create a new issue directly from the menu.
3. **Branch creation** — A new branch is created on GitHub named `task-finisher/issue-{number}-{slug}`.
4. **Repository clone** — The repository is cloned into a temporary directory.
5. **Agentic loop** — Claude is given the issue title and body, along with a set of file-system and shell tools (`Read`, `Write`, `Edit`, `MultiEdit`, `Glob`, `Grep`, `Bash`). It explores the codebase and implements the required changes autonomously.
6. **Commit & push** — All changes are staged, committed, and pushed to the feature branch.
7. **Pull Request** — A PR is opened against the default branch with an AI-generated description.
8. **Cleanup** — The temporary working directory is removed.

---

## Project structure

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

---

## Configuration

Default settings are stored in `src/TaskFinisher/appsettings.json`:

```json
{
  "App": {
    "Project": "tutcugil",
    "Name": "task-finisher",
    "Version": "0.0.2",
    "Operation": "ALFA"
  },
  "AppSettings": {
    "MaxAgentIterations": 50,
    "Model": "claude-sonnet-4-5-20250929"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Warning",
      "TaskFinisher": "Information"
    }
  }
}
```

These values can be overridden at runtime via `--max-iterations` and `--model` flags, or by setting the `ANTHROPIC_MODEL` environment variable. The `App.Version` value is read by the GitHub Actions release workflow to tag the release.

---

## Available Claude models

| Model | Description |
|---|---|
| `claude-sonnet-4-5-20250929` | Smart & fast — best value *(default)* |
| `claude-sonnet-4-6` | Latest Sonnet — cutting edge |
| `claude-opus-4-5-20251101` | Most capable — slower & pricier |
| `claude-opus-4-6` | Most capable, latest generation |
| `claude-haiku-4-5-20251001` | Fastest — lightweight tasks |

You may also enter any custom model ID via the interactive picker or the `--model` flag.

---

## Running tests

```bash
dotnet test task-finisher.sln
```

Tests live in `tests/TaskFinisher.Tests/`. The main test file is `FilesystemToolsTests.cs` (class `AgentToolsTests`) which covers all `AgentTools` methods. Each test creates a temporary directory, runs tool operations, and asserts on the output.

---

## Release

Releases are built automatically via the [GitHub Actions release workflow](.github/workflows/release.yml) when a PR is merged into the `prod` branch. Pre-built self-contained binaries are published for:

- `linux-x64`
- `linux-arm64`
- `win-x64`
- `osx-x64`
- `osx-arm64`

The release version is read from `src/TaskFinisher/appsettings.json` → `App.Version`.

---

## License

[MIT](LICENSE) — Copyright © 2026 Muhammet Tutcugil
