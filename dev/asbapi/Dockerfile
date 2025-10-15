FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0-noble AS build
ARG TARGETARCH
WORKDIR /source

# Copy source code and publish app
COPY . .
RUN dotnet restore -a $TARGETARCH ServiceBusApiEmulator/ServiceBusApiEmulator.csproj
RUN dotnet publish -a $TARGETARCH --no-restore -o /app ServiceBusApiEmulator/ServiceBusApiEmulator.csproj

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0-noble
ENV ASPNETCORE_HTTP_PORTS=80
EXPOSE 80
WORKDIR /app
COPY --link --from=build /app .
USER $APP_UID
ENTRYPOINT ["./ServiceBusApiEmulator"]