# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ModulePlatform.sln ./
COPY src/Shared/SharedKernel/*.csproj src/Shared/SharedKernel/
COPY src/Modules/Loyalty/Loyalty.Api/*.csproj src/Modules/Loyalty/Loyalty.Api/
RUN dotnet restore src/Modules/Loyalty/Loyalty.Api/Loyalty.Api.csproj
COPY . .
RUN dotnet publish src/Modules/Loyalty/Loyalty.Api/Loyalty.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Loyalty.Api.dll"]
