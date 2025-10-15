FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0-noble AS build
ARG TARGETARCH
WORKDIR /source

# Copy source code and publish app
COPY . .
RUN dotnet publish -a $TARGETARCH -o /app ./ClientSetupTool/ClientSetupTool.csproj

# Runtime stage
FROM mcr.microsoft.com/dotnet/runtime:8.0-noble
WORKDIR /app
COPY --link --from=build /app .
USER $APP_UID
ENTRYPOINT ["./ClientSetupTool"]