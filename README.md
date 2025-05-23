# SuperBus


## Design

### Flow of a message

#### Cloud -> Connector
1. Cloud: send message to `superbus-connectors-connector1` with tenant header
2. Rebus pipeline: attach connector ID header
3. Rebus ASB name formatter: change queue to `superbus-connectors`
4. SuperBus worker: take message from `superbus-connectors` and copy it to Azure Queue Storage `superbus-tenant1-connector1`
5. Connector: fetch message from AQS `superbus-tenant1-connector-1`

## Important Technica Decisions

### Use single ASB queue for all connectors
The messages for all connectors of all tenants are handled by a single ASB queue.

#### Reasons
- The ASB trigger in Azure functions requires a fixed name. We want to use the ASB trigger
  to ensure that we can make use of the automatic scaling and function activation by Azure.
- Polling dynamically defined ASB queues would require the activation of the function with
  event grid triggers. Event grid triggers are only available in the premium SKU of ASB.