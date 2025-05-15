namespace SuperBus.Models;

public record QueueConnection(Uri Endpoint, string QueueName, DateTimeOffset ExpiresOn);