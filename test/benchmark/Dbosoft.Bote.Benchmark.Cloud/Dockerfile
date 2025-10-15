# This Dockerfile is for local testing only. You must run dotnet publish first.
FROM mcr.microsoft.com/dotnet/runtime:8.0-noble
WORKDIR /app

COPY ./bin/Release/net8.0/linux-x64/publish .

EXPOSE 8080
USER $APP_UID

ENTRYPOINT ["./Dbosoft.Bote.Benchmark.Cloud"]