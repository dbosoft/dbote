using System.Xml.Serialization;

namespace ServiceBusApiEmulator.Models
{
    [XmlRoot(ElementName = "entry", Namespace = "http://www.w3.org/2005/Atom")]
    public class CreateSubscriptionBody
    {
        [XmlElement(ElementName = "content", Namespace = "http://www.w3.org/2005/Atom")]
        public CreateSubscriptionContent Content { get; set; }
    }

    public class CreateSubscriptionContent
    {
        [XmlAttribute(AttributeName = "type")]
        public string Type { get; set; } = "application/xml";

        [XmlElement(ElementName = "SubscriptionDescription", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public SubscriptionDescription SubscriptionDescription { get; set; }
    }

    [XmlRoot(ElementName = "SubscriptionDescription", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
    public class SubscriptionDescription
    {
        [XmlElement(ElementName = "LockDuration", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public string LockDuration { get; set; }

        [XmlElement(ElementName = "RequiresSession", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public bool? RequiresSession { get; set; }

        [XmlElement(ElementName = "DefaultMessageTimeToLive", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public string DefaultMessageTimeToLive { get; set; }

        [XmlElement(ElementName = "DeadLetteringOnMessageExpiration", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public bool? DeadLetteringOnMessageExpiration { get; set; }

        [XmlElement(ElementName = "DeadLetteringOnFilterEvaluationExceptions", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public bool? DeadLetteringOnFilterEvaluationExceptions { get; set; }

        [XmlElement(ElementName = "MessageCount", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public int? MessageCount { get; set; }

        [XmlElement(ElementName = "MaxDeliveryCount", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public int? MaxDeliveryCount { get; set; }

        [XmlElement(ElementName = "EnableBatchedOperations", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public bool? EnableBatchedOperations { get; set; }

        [XmlElement(ElementName = "Status", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public EntityStatus? Status { get; set; }

        [XmlElement(ElementName = "CreatedAt", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public DateTime? CreatedAt { get; set; }

        [XmlElement(ElementName = "UpdatedAt", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public DateTime? UpdatedAt { get; set; }

        [XmlElement(ElementName = "AccessedAt", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public DateTime? AccessedAt { get; set; }

        [XmlElement(ElementName = "ForwardTo", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public string ForwardTo { get; set; }

        [XmlElement(ElementName = "UserMetadata", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public string UserMetadata { get; set; }

        [XmlElement(ElementName = "MessageCountDetails", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public MessageCountDetails MessageCountDetails { get; set; }

        [XmlElement(ElementName = "AutoDeleteOnIdle", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public string AutoDeleteOnIdle { get; set; }

        [XmlElement(ElementName = "ForwardDeadLetteredMessagesTo", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public string ForwardDeadLetteredMessagesTo { get; set; }
    }
}
