# ---- Stage 1: BUILD (has the full .NET SDK / compilers) ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy the WHOLE solution into the build context folder so that the API's
# project-to-project references (Application, Infrastructure, Domain) resolve.
COPY . .

# Resilient restore for flaky connections: auto-retry dropped downloads, one at a time.
ENV NUGET_ENABLE_ENHANCED_HTTP_RETRY=true
ENV NUGET_ENHANCED_MAX_NETWORK_TRY_COUNT=10
ENV NUGET_ENHANCED_NETWORK_RETRY_DELAY_MILLISECONDS=1000
RUN dotnet restore Buyit.Api/Buyit.Api.csproj --disable-parallel

# Compile & publish in Release to /app. --no-restore because we just restored.
RUN dotnet publish Buyit.Api/Buyit.Api.csproj -c Release -o /app --no-restore

# ---- Stage 2: RUNTIME (small image, no SDK, no source code) ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Bring ONLY the published output from the build stage into this lean image.
COPY --from=build /app .

# Run in Development so migrations + seed + Swagger happen automatically.
# NOTE: deliberate demo shortcut — exposes Swagger + detailed errors. NOT for real prod.
ENV ASPNETCORE_ENVIRONMENT=Development

# Document the default port (Render overrides it at runtime via $PORT).
EXPOSE 8080

# SHELL-FORM entrypoint so ${PORT} expands at RUNTIME (when Render injects it),
# falling back to 8080 locally. An ENV line or exec-form CMD would expand $PORT at
# BUILD time (empty) and bind to http://+: — builds but never responds.
ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://+:${PORT:-8080} dotnet Buyit.Api.dll"]