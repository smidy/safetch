# Optional environment variables (pass with -e or --env-file):
#   ASPNETCORE_ENVIRONMENT                          - Set to Development for in-memory rate limiting (default: Production)
#   FetchOptions__MaxResponseBytes                  - Max upstream response body size in bytes (default: 10485760)
#   FetchOptions__TimeoutSeconds                    - Per-request HTTP timeout in seconds (default: 15)
#   Safetch__RateLimit__Limits__0__MaxFetchesPerWindow - Max requests per window per caller identity (default: 100)
#
# Note: This image ships with no authentication and uses InMemoryRateLimiter with a fixed "local"
# caller identity. It is safe for local development. Before exposing to any network, add your own
# authentication layer.

# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy only the projects needed for build
COPY Safetch.Api/Safetch.Api.csproj Safetch.Api/
COPY Safetch.Core/Safetch.Core.csproj Safetch.Core/

# Restore dependencies
RUN dotnet restore Safetch.Api/Safetch.Api.csproj

# Copy all source code
COPY . .

# Publish as framework-dependent (not self-contained) — matches aspnet:9.0 base
RUN dotnet publish Safetch.Api/Safetch.Api.csproj \
  -c Release \
  -o /app/publish \
  --no-restore

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0

# Set default URL binding
ENV ASPNETCORE_URLS=http://+:8080

# Copy published output from build stage
COPY --from=build /app/publish /app

# Expose port
EXPOSE 8080

# Set working directory to published output
WORKDIR /app

# Run the app
ENTRYPOINT ["dotnet", "Safetch.Api.dll"]