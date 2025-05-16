FROM mcr.microsoft.com/dotnet/sdk:8.0
WORKDIR /emulator
RUN dotnet tool install -g Microsoft.Azure.SignalR.Emulator
ENV PATH="/root/.dotnet/tools:${PATH}"
ENTRYPOINT ["asrs-emulator", "start", "-i", "0.0.0.0"]
