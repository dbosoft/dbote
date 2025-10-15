using Rebus.Subscriptions;

namespace Dbosoft.Bote.Rebus.Subscriptions;

/// <summary>
/// Subscription storage for Bote that uses SignalR groups for topic subscriptions
/// </summary>
internal class BoteSubscriptionStorage(ISignalRClient signalRClient) : ISubscriptionStorage
{
    public async Task<IReadOnlyList<string>> GetSubscriberAddresses(string topic)
    {
        // system handles routing, so we don't need to return subscriber addresses
        return await Task.FromResult(Array.Empty<string>());
    }

    public async Task RegisterSubscriber(string topic, string subscriberAddress)
    {
        // Subscribe to the SignalR topic group for broadcast notifications
        await signalRClient.SubscribeToTopic(topic);
    }

    public async Task UnregisterSubscriber(string topic, string subscriberAddress)
    {
        // Unsubscribe from the SignalR topic group
        await signalRClient.UnsubscribeFromTopic(topic);
    }

    public bool IsCentralized => true; // Prevents Rebus from sending subscription requests to publisher
}
