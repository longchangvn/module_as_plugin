# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ModulePlatform.sln ./
COPY src/Aspire/ModulePlatform.ServiceDefaults/ModulePlatform.ServiceDefaults.csproj src/Aspire/ModulePlatform.ServiceDefaults/
COPY src/Shared/SharedKernel/*.csproj src/Shared/SharedKernel/
COPY src/Shared/ModulePlatform.Plugin.Abstractions/*.csproj src/Shared/ModulePlatform.Plugin.Abstractions/
COPY src/PluginHost/PluginHost.Api/*.csproj src/PluginHost/PluginHost.Api/
RUN dotnet restore src/PluginHost/PluginHost.Api/PluginHost.Api.csproj
COPY . .
RUN dotnet publish src/PluginHost/PluginHost.Api/PluginHost.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
# Extracted plugin packages (download cache); mount a volume to keep it across restarts.
ENV PluginHost__CacheRoot=/app/plugin-cache
ENTRYPOINT ["dotnet", "PluginHost.Api.dll"]
