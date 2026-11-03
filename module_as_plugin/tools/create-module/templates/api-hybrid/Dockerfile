# syntax=docker/dockerfile:1
# Build context: the folder containing both __PROJECT_DIR__/ and modules/ (see Dockerfile.dockerignore).
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY __PROJECT_DIR__/src/Aspire/ModulePlatform.ServiceDefaults/ModulePlatform.ServiceDefaults.csproj __PROJECT_DIR__/src/Aspire/ModulePlatform.ServiceDefaults/
COPY __PROJECT_DIR__/src/Shared/SharedKernel/*.csproj __PROJECT_DIR__/src/Shared/SharedKernel/
COPY modules/__MODULE_NAME__/api/__PASCAL__.Api/*.csproj modules/__MODULE_NAME__/api/__PASCAL__.Api/
RUN dotnet restore modules/__MODULE_NAME__/api/__PASCAL__.Api/__PASCAL__.Api.csproj
COPY __PROJECT_DIR__/src/Aspire/ModulePlatform.ServiceDefaults/ __PROJECT_DIR__/src/Aspire/ModulePlatform.ServiceDefaults/
COPY __PROJECT_DIR__/src/Shared/SharedKernel/ __PROJECT_DIR__/src/Shared/SharedKernel/
COPY modules/__MODULE_NAME__/api/ modules/__MODULE_NAME__/api/
RUN dotnet publish modules/__MODULE_NAME__/api/__PASCAL__.Api/__PASCAL__.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "__PASCAL__.Api.dll"]
