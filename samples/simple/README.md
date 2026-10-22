# Simple example
This is simple example for using dbote with basic Rebus infrastructure.

## Run tests
Just run `Start-Services.ps1`. It will build the necessary project and start everything with docker compose.
## Cloud side as Azure Functions
`Start-Services.ps1 -Functions` runs the cloud side as an Azure Functions app
(`Dbosoft.Bote.Samples.Simple.Functions`) that uses `Dbosoft.Bote.Cloud` instead of Rebus: a Service
Bus trigger on the cloud queue answers each ping with a pong, and a timer pushes to the clients. The
clients are the same Rebus clients in both cases.
