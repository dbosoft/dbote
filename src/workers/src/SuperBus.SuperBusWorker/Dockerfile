# This Dockerfile is for local testing only. You must run dotnet publish first.
FROM mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated8.0
ENV AzureWebJobsScriptRoot=/home/site/wwwroot \
    AzureFunctionsJobHost__Logging__Console__IsEnabled=true

COPY ./bin/Release/net8.0/linux-x64/publish  /home/site/wwwroot