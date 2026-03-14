# Required environment variables (pass with -e or --env-file):
#   AzureWebJobsStorage         - Storage connection string. Use "UseDevelopmentStorage=true" for Azurite.
#   FUNCTIONS_WORKER_RUNTIME    - Must be "dotnet-isolated"
#   RateLimit__WindowSeconds    - Rate limit window in seconds (e.g. 60)
#   RateLimit__MaxRequests      - Max requests per window per session (e.g. 30)
#
# Note: ASPNETCORE_ENVIRONMENT is set to "Development" in this image, which disables API key
# authentication on /fetch and /token endpoints. This image is intended for local development
# only. Do NOT use in production — deploy via Azure Functions directly for production workloads.

# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy solution and project files first for optimal layer caching
COPY Safetch.sln .
COPY Safetch.Api/Safetch.Api.csproj Safetch.Api/
COPY Safetch.Core/Safetch.Core.csproj Safetch.Core/

# Restore dependencies
RUN dotnet restore Safetch.sln

# Copy all source code
COPY . .

# Publish the Azure Functions app
RUN dotnet publish Safetch.Api/Safetch.Api.csproj -c Release -o /app/publish --no-restore

# Stage 2: Runtime
FROM mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated9.0

# Set required environment variables for Azure Functions runtime
ENV AzureWebJobsScriptRoot=/home/site/wwwroot
ENV AzureFunctionsJobHost__Logging__Console__IsEnabled=true
ENV ASPNETCORE_ENVIRONMENT=Development

# Copy published output from build stage
COPY --from=build /app/publish /home/site/wwwroot