using System.Xml.Serialization;

namespace ServiceBusApiEmulator.Models
{
    public class ServiceBusManagementError
    {
        [XmlElement(ElementName = "Code")]
        public int Code { get; set; }

        [XmlElement(ElementName = "Detail")]
        public string Detail { get; set; }
    }
}
