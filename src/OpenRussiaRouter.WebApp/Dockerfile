# syntax=docker/dockerfile:1.7
ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet:8.0
ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:8.0

FROM ${SDK_IMAGE} AS build
WORKDIR /src

COPY NuGet.Config Directory.Packages.props ./
COPY src/OpenRussiaRouter.BuildingBlocks/OpenRussiaRouter.BuildingBlocks.csproj src/OpenRussiaRouter.BuildingBlocks/
COPY src/OpenRussiaRouter.Contracts/OpenRussiaRouter.Contracts.csproj src/OpenRussiaRouter.Contracts/
COPY src/OpenRussiaRouter.WebApp/OpenRussiaRouter.WebApp.csproj src/OpenRussiaRouter.WebApp/
RUN dotnet restore src/OpenRussiaRouter.WebApp/OpenRussiaRouter.WebApp.csproj --configfile NuGet.Config

COPY src/ src/
# Legal markdown lives outside src/ and is referenced by the WebApp .csproj via ..\..\docs;
# it must be present in the build context for `dotnet publish` to bundle it into the image.
COPY docs/mandatory/ docs/mandatory/
RUN dotnet publish src/OpenRussiaRouter.WebApp/OpenRussiaRouter.WebApp.csproj \
    -c Release -o /app/publish --no-restore

FROM ${RUNTIME_IMAGE} AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "OpenRussiaRouter.WebApp.dll"]
