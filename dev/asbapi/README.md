# Azure Service Bus API emulator
This emulator emulates the administration API of the Azure Service Bus data plane.
This API is XML-based and provides clients with CRUD operations for queues, topics,
and subscriptions. This API is accessed with the `ServiceBusAdministrationClient`.
Note, that this API completely independent from the Azure Resource Manager REST API.

The Azure Service Bus emulator by Microsoft only emulates the actual service bus,
but does not provide XML API. We need that API as the Rebus client expects at least
read-only access to the management API.

## Development
The API models have been generated using GitHub Copilot based on this specification:
https://github.com/Azure/azure-rest-api-specs/blob/main/specification/servicebus/data-plane/Microsoft.ServiceBus/stable/2021-05/servicebus.json.
The serialization logic is based on the corresponding code in `ServiceBusAdministrationClient`.

The configuration mmodel have been generated using GitHub copilot based on this specification:
https://github.com/Azure/azure-service-bus-emulator-installer/blob/main/ServiceBus-Emulator/Schema/Config-schema.json.
The same specification is used by Microsoft's Azure Service Bus emulator.
