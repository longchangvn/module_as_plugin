# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ModulePlatform.sln ./
COPY src/Aspire/ModulePlatform.ServiceDefaults/ModulePlatform.ServiceDefaults.csproj src/Aspire/ModulePlatform.ServiceDefaults/
COPY src/Shared/SharedKernel/*.csproj src/Shared/SharedKernel/
COPY src/Auth/Auth.Api/*.csproj src/Auth/Auth.Api/
RUN dotnet restore src/Auth/Auth.Api/Auth.Api.csproj
COPY . .
RUN dotnet publish src/Auth/Auth.Api/Auth.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Auth.Api.dll"]
