# For local testing only. Run dotnet publish -c Release -r linux-x64 first.
FROM mcr.microsoft.com/dotnet/aspnet:8.0-noble
WORKDIR /app

COPY ./bin/Release/net8.0/linux-x64/publish .

# The self-contained apphost loses its execute bit when published on Windows and copied into a
# Linux image, which fails with "permission denied" on start. Restore it (as root, before USER).
RUN chmod +x ./Dbosoft.Bote.Samples.LoadTest.Controller

EXPOSE 8080
USER $APP_UID

ENTRYPOINT ["./Dbosoft.Bote.Samples.LoadTest.Controller"]
