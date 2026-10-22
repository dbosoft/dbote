# This Dockerfile is for local testing only. You must run dotnet publish first.
FROM mcr.microsoft.com/dotnet/runtime:10.0-noble
WORKDIR /app

COPY ./bin/Release/net10.0/linux-x64/publish .

EXPOSE 8080
USER $APP_UID

ENTRYPOINT ["./Dbosoft.Bote.Samples.Chat.Cloud"]
