# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ModulePlatform.sln ./
COPY src/Aspire/ModulePlatform.ServiceDefaults/ModulePlatform.ServiceDefaults.csproj src/Aspire/ModulePlatform.ServiceDefaults/
COPY src/Shared/SharedKernel/*.csproj src/Shared/SharedKernel/
COPY src/Modules/__PASCAL__/__PASCAL__.Api/*.csproj src/Modules/__PASCAL__/__PASCAL__.Api/
RUN dotnet restore src/Modules/__PASCAL__/__PASCAL__.Api/__PASCAL__.Api.csproj
COPY . .
RUN dotnet publish src/Modules/__PASCAL__/__PASCAL__.Api/__PASCAL__.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "__PASCAL__.Api.dll"]
