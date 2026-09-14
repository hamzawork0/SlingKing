# Build stage: the SDK image restores and publishes, and is then thrown away.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy the project files first so a source-only change does not invalidate the restore layer.
COPY SlingKing.sln Directory.Build.props ./
COPY src/SlingKing.Domain/SlingKing.Domain.csproj             src/SlingKing.Domain/
COPY src/SlingKing.Application/SlingKing.Application.csproj   src/SlingKing.Application/
COPY src/SlingKing.Infrastructure/SlingKing.Infrastructure.csproj src/SlingKing.Infrastructure/
COPY src/SlingKing.Api/SlingKing.Api.csproj                   src/SlingKing.Api/

RUN dotnet restore src/SlingKing.Api/SlingKing.Api.csproj

COPY src/ src/
RUN dotnet publish src/SlingKing.Api/SlingKing.Api.csproj \
    -c Release -o /app/publish --no-restore

# Runtime stage: only the published output ships.
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# The aspnet image already provides a non-root 'app' user. Running as root in a container
# reachable from the internet buys nothing.
USER app

COPY --from=build --chown=app:app /app/publish .

# Render assigns a port through $PORT and expects the process to bind it. An ENV cannot expand
# a variable that does not exist at build time, so the entrypoint is shell form. The default
# keeps the image runnable locally with no PORT set.
ENTRYPOINT ["sh", "-c", "dotnet SlingKing.Api.dll --urls http://0.0.0.0:${PORT:-10000}"]
