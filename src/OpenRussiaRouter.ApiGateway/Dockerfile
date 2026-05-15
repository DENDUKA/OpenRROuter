# syntax=docker/dockerfile:1.7
ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet:8.0
ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:8.0

FROM ${SDK_IMAGE} AS build
WORKDIR /src

COPY NuGet.Config ./
COPY src/OpenRussiaRouter.BuildingBlocks/OpenRussiaRouter.BuildingBlocks.csproj src/OpenRussiaRouter.BuildingBlocks/
COPY src/OpenRussiaRouter.Contracts/OpenRussiaRouter.Contracts.csproj src/OpenRussiaRouter.Contracts/
COPY src/OpenRussiaRouter.ApiGateway/OpenRussiaRouter.ApiGateway.csproj src/OpenRussiaRouter.ApiGateway/
RUN dotnet restore src/OpenRussiaRouter.ApiGateway/OpenRussiaRouter.ApiGateway.csproj --configfile NuGet.Config

COPY src/ src/
RUN dotnet publish src/OpenRussiaRouter.ApiGateway/OpenRussiaRouter.ApiGateway.csproj \
    -c Release -o /app/publish --no-restore

FROM ${RUNTIME_IMAGE} AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "OpenRussiaRouter.ApiGateway.dll"]
