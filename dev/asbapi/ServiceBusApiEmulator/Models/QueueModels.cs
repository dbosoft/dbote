using System.Xml.Serialization;

namespace ServiceBusApiEmulator.Models
{
    [XmlRoot(ElementName = "entry", Namespace = "http://www.w3.org/2005/Atom")]
    public class CreateQueueBody
    {
        [XmlElement(ElementName = "content", Namespace = "http://www.w3.org/2005/Atom")]
        public CreateQueueContent Content { get; set; }
    }


    public class CreateQueueContent
    {
        [XmlAttribute(AttributeName = "type")]
        public string Type { get; set; } = "application/xml";

        [XmlElement(ElementName = "QueueDescription", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public QueueDescription QueueDescription { get; set; }
    }

    [XmlRoot(ElementName = "QueueDescription", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
    public class QueueDescription
    {
        [XmlElement(ElementName = "LockDuration", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public string LockDuration { get; set; }

        [XmlElement(ElementName = "MaxSizeInMegabytes", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public long? MaxSizeInMegabytes { get; set; }

        [XmlElement(ElementName = "RequiresDuplicateDetection", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public bool? RequiresDuplicateDetection { get; set; }

        [XmlElement(ElementName = "RequiresSession", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public bool? RequiresSession { get; set; }

        [XmlElement(ElementName = "DefaultMessageTimeToLive", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public string DefaultMessageTimeToLive { get; set; }

        [XmlElement(ElementName = "DeadLetteringOnMessageExpiration", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public bool? DeadLetteringOnMessageExpiration { get; set; }

        [XmlElement(ElementName = "DuplicateDetectionHistoryTimeWindow", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public string DuplicateDetectionHistoryTimeWindow { get; set; }

        [XmlElement(ElementName = "MaxDeliveryCount", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public int? MaxDeliveryCount { get; set; }

        [XmlElement(ElementName = "EnableBatchedOperations", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public bool? EnableBatchedOperations { get; set; }

        [XmlElement(ElementName = "SizeInBytes", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public long? SizeInBytes { get; set; }

        [XmlElement(ElementName = "MessageCount", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public int? MessageCount { get; set; }

        [XmlElement(ElementName = "IsAnonymousAccessible", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public bool? IsAnonymousAccessible { get; set; }

        [XmlArray(ElementName = "AuthorizationRules", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        [XmlArrayItem(ElementName = "AuthorizationRule", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public List<AuthorizationRule> AuthorizationRules { get; set; }

        [XmlElement(ElementName = "Status", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public EntityStatus? Status { get; set; }

        [XmlElement(ElementName = "ForwardTo", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public string ForwardTo { get; set; }

        [XmlElement(ElementName = "UserMetadata", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public string UserMetadata { get; set; }

        [XmlElement(ElementName = "CreatedAt", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public DateTime? CreatedAt { get; set; }

        [XmlElement(ElementName = "UpdatedAt", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public DateTime? UpdatedAt { get; set; }

        [XmlElement(ElementName = "AccessedAt", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public DateTime? AccessedAt { get; set; }

        [XmlElement(ElementName = "SupportOrdering", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public bool? SupportOrdering { get; set; }

        [XmlElement(ElementName = "MessageCountDetails", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public MessageCountDetails MessageCountDetails { get; set; }

        [XmlElement(ElementName = "AutoDeleteOnIdle", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public string AutoDeleteOnIdle { get; set; }

        [XmlElement(ElementName = "EnablePartitioning", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public bool? EnablePartitioning { get; set; }

        [XmlElement(ElementName = "EntityAvailabilityStatus", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public EntityAvailabilityStatus? EntityAvailabilityStatus { get; set; }

        [XmlElement(ElementName = "ForwardDeadLetteredMessagesTo", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public string ForwardDeadLetteredMessagesTo { get; set; }

        [XmlElement(ElementName = "EnableExpress", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public bool? EnableExpress { get; set; }

        [XmlElement(ElementName = "MaxMessageSizeInKilobytes", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public long? MaxMessageSizeInKilobytes { get; set; }
    }

    public class AuthorizationRule
    {
        [XmlAttribute(AttributeName = "type", Namespace = "http://www.w3.org/2001/XMLSchema-instance")]
        public string Type { get; set; }

        [XmlElement(ElementName = "ClaimType", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public string ClaimType { get; set; }

        [XmlElement(ElementName = "ClaimValue", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public string ClaimValue { get; set; }

        [XmlArray(ElementName = "Rights", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        [XmlArrayItem(ElementName = "AccessRights", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public List<AccessRights> Rights { get; set; }

        [XmlElement(ElementName = "CreatedTime", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public DateTime? CreatedTime { get; set; }

        [XmlElement(ElementName = "ModifiedTime", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public DateTime? ModifiedTime { get; set; }

        [XmlElement(ElementName = "KeyName", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public string KeyName { get; set; }

        [XmlElement(ElementName = "PrimaryKey", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public string PrimaryKey { get; set; }

        [XmlElement(ElementName = "SecondaryKey", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public string SecondaryKey { get; set; }
    }

    public enum AccessRights
    {
        [XmlEnum(Name = "Manage")]
        Manage,
        [XmlEnum(Name = "Send")]
        Send,
        [XmlEnum(Name = "Listen")]
        Listen
    }

    public enum EntityStatus
    {
        [XmlEnum(Name = "Active")]
        Active,
        [XmlEnum(Name = "Creating")]
        Creating,
        [XmlEnum(Name = "Deleting")]
        Deleting,
        [XmlEnum(Name = "Disabled")]
        Disabled,
        [XmlEnum(Name = "ReceiveDisabled")]
        ReceiveDisabled,
        [XmlEnum(Name = "Renaming")]
        Renaming,
        [XmlEnum(Name = "Restoring")]
        Restoring,
        [XmlEnum(Name = "SendDisabled")]
        SendDisabled,
        [XmlEnum(Name = "Unknown")]
        Unknown
    }

    public enum EntityAvailabilityStatus
    {
        [XmlEnum(Name = "Available")]
        Available,
        [XmlEnum(Name = "Limited")]
        Limited,
        [XmlEnum(Name = "Renaming")]
        Renaming,
        [XmlEnum(Name = "Restoring")]
        Restoring,
        [XmlEnum(Name = "Unknown")]
        Unknown
    }

    public class MessageCountDetails
    {
        [XmlElement(ElementName = "ActiveMessageCount", Namespace = "http://schemas.microsoft.com/netservices/2011/06/servicebus")]
        public int? ActiveMessageCount { get; set; }

        [XmlElement(ElementName = "DeadLetterMessageCount", Namespace = "http://schemas.microsoft.com/netservices/2011/06/servicebus")]
        public int? DeadLetterMessageCount { get; set; }

        [XmlElement(ElementName = "ScheduledMessageCount", Namespace = "http://schemas.microsoft.com/netservices/2011/06/servicebus")]
        public int? ScheduledMessageCount { get; set; }

        [XmlElement(ElementName = "TransferDeadLetterMessageCount", Namespace = "http://schemas.microsoft.com/netservices/2011/06/servicebus")]
        public int? TransferDeadLetterMessageCount { get; set; }

        [XmlElement(ElementName = "TransferMessageCount", Namespace = "http://schemas.microsoft.com/netservices/2011/06/servicebus")]
        public int? TransferMessageCount { get; set; }
    }
}
