# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS builder
WORKDIR /src

# Copy only the projects the Backend actually depends on (Backend references Frontend
# to embed the built Blazor WASM output, plus the two Shared libs and Xenne.RCON).
COPY ["RustRconServerManager.Backend/", "RustRconServerManager.Backend/"]
COPY ["RustRconServerManager.Frontend/", "RustRconServerManager.Frontend/"]
COPY ["RustRconServerManager.Shared/", "RustRconServerManager.Shared/"]
COPY ["RustRconServerManager.Shared.Configuration/", "RustRconServerManager.Shared.Configuration/"]
COPY ["Xenne.RCON/", "Xenne.RCON/"]

# Restore and build (restoring the Backend project pulls in its project references above)
RUN dotnet restore "RustRconServerManager.Backend/RustRconServerManager.Backend.csproj"
RUN dotnet publish "RustRconServerManager.Backend/RustRconServerManager.Backend.csproj" -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0
ARG VERSION=dev
WORKDIR /app

# Install curl (health checks) and jq (parsing GitHub release JSON for auto-update)
RUN apt-get update && apt-get install -y curl jq && rm -rf /var/lib/apt/lists/*

# Copy published application (includes frontend served by backend)
COPY --from=builder /app/publish .

# Store version
RUN echo "${VERSION}" > /app/.version

# Auto-update checker, run by docker-entrypoint.sh before the app starts
COPY docker-entrypoint.sh check-update.sh /app/
RUN chmod +x /app/docker-entrypoint.sh /app/check-update.sh

# Expose port
EXPOSE 5000

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=40s --retries=3 \
    CMD curl -f http://localhost:5000/health || exit 1

# Set environment variables
ENV ASPNETCORE_URLS=http://+:5000
ENV ASPNETCORE_ENVIRONMENT=Production

# Checks for an update, then starts the application
ENTRYPOINT ["/app/docker-entrypoint.sh"]
