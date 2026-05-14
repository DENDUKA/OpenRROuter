# syntax=docker/dockerfile:1.7
ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet:8.0
ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:8.0

FROM ${SDK_IMAGE} AS build
WORKDIR /src

COPY NuGet.Config ./
COPY src/ src/
COPY db/ db/
RUN dotnet restore src/OpenRussiaRouter.ControlPlane/OpenRussiaRouter.ControlPlane.csproj --configfile NuGet.Config

WORKDIR /src/src/OpenRussiaRouter.ControlPlane
RUN dotnet publish -c Release -o /app/publish --no-restore

FROM ${RUNTIME_IMAGE} AS runtime
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=build /src/db ./db

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "OpenRussiaRouter.ControlPlane.dll"]
