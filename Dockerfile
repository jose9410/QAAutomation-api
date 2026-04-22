# 1. Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Copy csproj and restore dependencies
COPY *.csproj ./
RUN dotnet restore

# Copy remaining code and publish
COPY . ./
RUN dotnet publish -c Release -o out

# 2. Execution stage
# We use the Playwright-specific image that includes the necessary browsers and OS libraries
FROM mcr.microsoft.com/playwright/dotnet:v1.59.0-jammy
WORKDIR /app

# Copy published binaries from build stage
COPY --from=build /app/out .

# Create directory for logs/temporary files if needed
RUN mkdir -p /app/Logs && chmod -R 777 /app/Logs

# Default port
EXPOSE 8080

# The Playwright image already has the browsers installed in the standard location
# so we don't need to run 'playwright install' at runtime if the version matches.
# However, if you update the NuGet package, you might need to run it.

ENTRYPOINT ["dotnet", "QAAutomation.Api.dll"]
