FROM mcr.microsoft.com/dotnet/runtime:8.0-noble AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0-noble AS build
WORKDIR /source
COPY . .
RUN dotnet publish -a amd64 -o /app ./SuperBus.StorageInit.csproj

FROM base AS final
WORKDIR /app
COPY --link --from=build /app .
ENTRYPOINT ["dotnet", "SuperBus.StorageInit.dll"]
