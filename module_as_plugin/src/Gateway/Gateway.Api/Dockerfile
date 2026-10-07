# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ModulePlatform.sln ./
COPY src/Gateway/Gateway.Api/*.csproj src/Gateway/Gateway.Api/
RUN dotnet restore src/Gateway/Gateway.Api/Gateway.Api.csproj
COPY . .
RUN dotnet publish src/Gateway/Gateway.Api/Gateway.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
# File stores (ocelot.json, gateway-services.json, gateway-modules.json) live in
# /app. For persistence across restarts, bind-mount the individual files
# (see docker-compose.yml); do NOT mount a volume over all of /app.
ENTRYPOINT ["dotnet", "Gateway.Api.dll"]
