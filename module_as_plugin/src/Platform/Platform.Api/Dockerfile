# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ModulePlatform.sln ./
COPY src/Shared/SharedKernel/*.csproj src/Shared/SharedKernel/
COPY src/Platform/Platform.Domain/*.csproj src/Platform/Platform.Domain/
COPY src/Platform/Platform.Application/*.csproj src/Platform/Platform.Application/
COPY src/Platform/Platform.Infrastructure/*.csproj src/Platform/Platform.Infrastructure/
COPY src/Platform/Platform.Api/*.csproj src/Platform/Platform.Api/
RUN dotnet restore src/Platform/Platform.Api/Platform.Api.csproj
COPY . .
RUN dotnet publish src/Platform/Platform.Api/Platform.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Platform.Api.dll"]
