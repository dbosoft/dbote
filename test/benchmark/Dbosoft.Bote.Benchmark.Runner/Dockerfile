# This Dockerfile is for local testing only. You must run dotnet publish first.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble
WORKDIR /app

COPY ./bin/Release/net10.0/linux-x64/publish .

EXPOSE 7312
USER $APP_UID

ENTRYPOINT ["./Dbosoft.Bote.Benchmark.Runner"]