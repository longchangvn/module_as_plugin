# syntax=docker/dockerfile:1
# Build context: the folder containing both module_as_plugin/ and modules/ (see Dockerfile.dockerignore).
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY module_as_plugin/src/Aspire/ModulePlatform.ServiceDefaults/ModulePlatform.ServiceDefaults.csproj module_as_plugin/src/Aspire/ModulePlatform.ServiceDefaults/
COPY module_as_plugin/src/Shared/SharedKernel/*.csproj module_as_plugin/src/Shared/SharedKernel/
COPY modules/orders/api/Orders.Api/*.csproj modules/orders/api/Orders.Api/
RUN dotnet restore modules/orders/api/Orders.Api/Orders.Api.csproj
COPY module_as_plugin/src/Aspire/ModulePlatform.ServiceDefaults/ module_as_plugin/src/Aspire/ModulePlatform.ServiceDefaults/
COPY module_as_plugin/src/Shared/SharedKernel/ module_as_plugin/src/Shared/SharedKernel/
COPY modules/orders/api/ modules/orders/api/
RUN dotnet publish modules/orders/api/Orders.Api/Orders.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Orders.Api.dll"]
