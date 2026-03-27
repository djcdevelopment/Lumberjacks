# Multi-stage build for all .NET game services
# Build stage: compiles all projects from the solution
# Runtime stages: one per service, copies only the needed binaries

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy solution and project files first for layer caching
COPY Game.sln Directory.Build.props Directory.Packages.props ./
COPY src/Game.Contracts/Game.Contracts.csproj src/Game.Contracts/
COPY src/Game.Persistence/Game.Persistence.csproj src/Game.Persistence/
COPY src/Game.ServiceDefaults/Game.ServiceDefaults.csproj src/Game.ServiceDefaults/
COPY src/Game.Gateway/Game.Gateway.csproj src/Game.Gateway/
COPY src/Game.Simulation/Game.Simulation.csproj src/Game.Simulation/
COPY src/Game.EventLog/Game.EventLog.csproj src/Game.EventLog/
COPY src/Game.Progression/Game.Progression.csproj src/Game.Progression/
COPY src/Game.OperatorApi/Game.OperatorApi.csproj src/Game.OperatorApi/

# Restore only the src projects (tests excluded from Docker build)
RUN dotnet restore src/Game.Gateway \
 && dotnet restore src/Game.Simulation \
 && dotnet restore src/Game.EventLog \
 && dotnet restore src/Game.Progression \
 && dotnet restore src/Game.OperatorApi

# Copy everything and publish each service
COPY src/ src/
RUN dotnet publish src/Game.Gateway -c Release -o /app/gateway --no-restore
RUN dotnet publish src/Game.Simulation -c Release -o /app/simulation --no-restore
RUN dotnet publish src/Game.EventLog -c Release -o /app/eventlog --no-restore
RUN dotnet publish src/Game.Progression -c Release -o /app/progression --no-restore
RUN dotnet publish src/Game.OperatorApi -c Release -o /app/operatorapi --no-restore

# --- Runtime images ---
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS gateway
WORKDIR /app
COPY --from=build /app/gateway .
ENV ASPNETCORE_URLS=http://+:4000
EXPOSE 4000
ENTRYPOINT ["dotnet", "Game.Gateway.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS simulation
WORKDIR /app
COPY --from=build /app/simulation .
ENV ASPNETCORE_URLS=http://+:4001
EXPOSE 4001
ENTRYPOINT ["dotnet", "Game.Simulation.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS eventlog
WORKDIR /app
COPY --from=build /app/eventlog .
ENV ASPNETCORE_URLS=http://+:4002
EXPOSE 4002
ENTRYPOINT ["dotnet", "Game.EventLog.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS progression
WORKDIR /app
COPY --from=build /app/progression .
ENV ASPNETCORE_URLS=http://+:4003
EXPOSE 4003
ENTRYPOINT ["dotnet", "Game.Progression.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS operatorapi
WORKDIR /app
COPY --from=build /app/operatorapi .
ENV ASPNETCORE_URLS=http://+:4004
EXPOSE 4004
ENTRYPOINT ["dotnet", "Game.OperatorApi.dll"]
