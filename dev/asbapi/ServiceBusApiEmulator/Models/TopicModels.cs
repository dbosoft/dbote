using System;
using System.Xml.Serialization;

namespace ServiceBusApiEmulator.Models
{
    [XmlRoot(ElementName = "entry", Namespace = "http://www.w3.org/2005/Atom")]
    public class CreateTopicBody
    {
        [XmlElement(ElementName = "content", Namespace = "http://www.w3.org/2005/Atom")]
        public CreateTopicContent Content { get; set; }
    }

    public class CreateTopicContent
    {
        [XmlAttribute(AttributeName = "type")]
        public string Type { get; set; } = "application/xml";

        [XmlElement(ElementName = "TopicDescription", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public TopicDescription TopicDescription { get; set; }
    }

    [XmlRoot(ElementName = "TopicDescription", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
    public class TopicDescription
    {
        [XmlElement(ElementName = "DefaultMessageTimeToLive", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public string DefaultMessageTimeToLive { get; set; }

        [XmlElement(ElementName = "MaxSizeInMegabytes", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public long? MaxSizeInMegabytes { get; set; }

        [XmlElement(ElementName = "RequiresDuplicateDetection", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public bool? RequiresDuplicateDetection { get; set; }

        [XmlElement(ElementName = "DuplicateDetectionHistoryTimeWindow", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public string DuplicateDetectionHistoryTimeWindow { get; set; }

        [XmlElement(ElementName = "EnableBatchedOperations", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public bool? EnableBatchedOperations { get; set; }

        [XmlElement(ElementName = "SizeInBytes", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public long? SizeInBytes { get; set; }

        [XmlElement(ElementName = "FilteringMessagesBeforePublishing", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public bool? FilteringMessagesBeforePublishing { get; set; }

        [XmlElement(ElementName = "IsAnonymousAccessible", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public bool? IsAnonymousAccessible { get; set; }

        [XmlArray(ElementName = "AuthorizationRules", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        [XmlArrayItem(ElementName = "AuthorizationRule", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public List<AuthorizationRule> AuthorizationRules { get; set; }

        [XmlElement(ElementName = "Status", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public EntityStatus? Status { get; set; }

        [XmlElement(ElementName = "UserMetadata", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public string UserMetadata { get; set; }

        [XmlElement(ElementName = "SupportOrdering", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public bool? SupportOrdering { get; set; }

        [XmlElement(ElementName = "AutoDeleteOnIdle", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public string AutoDeleteOnIdle { get; set; }

        [XmlElement(ElementName = "EnablePartitioning", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public bool? EnablePartitioning { get; set; }

        [XmlElement(ElementName = "EntityAvailabilityStatus", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public EntityAvailabilityStatus? EntityAvailabilityStatus { get; set; }

        [XmlElement(ElementName = "EnableExpress", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public bool? EnableExpress { get; set; }

        [XmlElement(ElementName = "MaxMessageSizeInKilobytes", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public long? MaxMessageSizeInKilobytes { get; set; }
    }
}
