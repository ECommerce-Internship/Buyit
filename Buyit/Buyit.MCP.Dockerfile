# ---- Build stage: compile with the .NET SDK ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy the whole solution so project-to-project references resolve, then publish.
# (A finer-grained copy would restore faster via layer caching, but this is simpler
# and correct — optimize later if build times hurt.)
COPY . .
RUN dotnet restore Buyit.MCP/Buyit.MCP.csproj
RUN dotnet publish Buyit.MCP/Buyit.MCP.csproj -c Release -o /app --no-restore

# ---- Runtime stage: small image, no SDK ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# curl is used by the docker-compose health check to hit /health.
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app .

# The aspnet image listens on 8080 by default; be explicit for readers.
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

ENTRYPOINT ["dotnet", "Buyit.MCP.dll"]
