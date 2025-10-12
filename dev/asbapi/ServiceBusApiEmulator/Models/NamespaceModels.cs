using System.Xml.Serialization;

namespace ServiceBusApiEmulator.Models
{
    [XmlRoot(ElementName = "NamespaceInfo", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
    public class NamespaceProperties
    {
        [XmlElement(ElementName = "Alias", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public string Alias { get; set; }

        [XmlElement(ElementName = "CreatedTime", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public DateTime? CreatedTime { get; set; }

        [XmlElement(ElementName = "MessagingSKU", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public MessagingSku? MessagingSku { get; set; }

        [XmlElement(ElementName = "MessagingUnits", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public int? MessagingUnits { get; set; }

        [XmlElement(ElementName = "ModifiedTime", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public DateTime? ModifiedTime { get; set; }

        [XmlElement(ElementName = "Name", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public string Name { get; set; }

        [XmlElement(ElementName = "NamespaceType", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public NamespaceType? NamespaceType { get; set; }
    }

    public enum MessagingSku
    {
        [XmlEnum(Name = "Basic")]
        Basic,
        [XmlEnum(Name = "Standard")]
        Standard,
        [XmlEnum(Name = "Premium")]
        Premium
    }

    public enum NamespaceType
    {
        [XmlEnum(Name = "Messaging")]
        Messaging,
        [XmlEnum(Name = "NotificationHub")]
        NotificationHub,
        [XmlEnum(Name = "Mixed")]
        Mixed,
        [XmlEnum(Name = "EventHub")]
        EventHub,
        [XmlEnum(Name = "Relay")]
        Relay
    }
}
