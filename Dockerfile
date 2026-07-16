# Multi-stage Dockerfile for PerFunTracker API
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj files and restore
COPY src/PerFunTracker.Core/PerFunTracker.Core.csproj src/PerFunTracker.Core/
COPY src/PerFunTracker.Infrastructure/PerFunTracker.Infrastructure.csproj src/PerFunTracker.Infrastructure/
COPY src/PerFunTracker.Api/PerFunTracker.Api.csproj src/PerFunTracker.Api/
RUN dotnet restore src/PerFunTracker.Api/PerFunTracker.Api.csproj

# Copy source and publish
COPY src/ src/
RUN dotnet publish src/PerFunTracker.Api/PerFunTracker.Api.csproj -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "PerFunTracker.Api.dll"]
