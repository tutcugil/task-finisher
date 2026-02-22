# ── Build stage ──────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY task-finisher.sln ./
COPY src/TaskFinisher/TaskFinisher.csproj src/TaskFinisher/

RUN dotnet restore src/TaskFinisher/TaskFinisher.csproj

COPY src/TaskFinisher/ src/TaskFinisher/

RUN dotnet publish src/TaskFinisher/TaskFinisher.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# ── Runtime stage ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app

# Install git (required for clone/push operations)
RUN apt-get update && apt-get install -y --no-install-recommends git \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Credentials and config passed at runtime via environment variables:
#   GITHUB_TOKEN, GITHUB_REPO, ANTHROPIC_API_KEY
# Example:
#   docker run --rm \
#     -e GITHUB_TOKEN=ghp_xxx \
#     -e GITHUB_REPO=owner/repo \
#     -e ANTHROPIC_API_KEY=sk-ant-xxx \
#     task-finisher --non-interactive

ENTRYPOINT ["./task-finisher", "--non-interactive"]
