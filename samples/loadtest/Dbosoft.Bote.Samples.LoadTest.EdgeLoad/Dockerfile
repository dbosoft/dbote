# For local testing only. Run dotnet publish -c Release -r linux-x64 first.
FROM mcr.microsoft.com/dotnet/runtime:8.0-noble
WORKDIR /app

COPY ./bin/Release/net8.0/linux-x64/publish .

USER $APP_UID

ENTRYPOINT ["./Dbosoft.Bote.Samples.LoadTest.EdgeLoad"]
