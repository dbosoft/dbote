using System;
using System.Xml.Serialization;

namespace ServiceBusApiEmulator.Models
{
    [XmlRoot(ElementName = "entry", Namespace = "http://www.w3.org/2005/Atom")]
    public class CreateRuleBody
    {
        [XmlElement(ElementName = "content", Namespace = "http://www.w3.org/2005/Atom")]
        public CreateRuleContent Content { get; set; }
    }

    public class CreateRuleContent
    {
        [XmlAttribute(AttributeName = "type")]
        public string Type { get; set; } = "application/xml";

        [XmlElement(ElementName = "RuleDescription", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public RuleDescription RuleDescription { get; set; }
    }

    public class RuleDescription
    {
        [XmlElement(ElementName = "Filter", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public Filter Filter { get; set; }

        [XmlElement(ElementName = "Action", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public Action Action { get; set; }

        [XmlElement(ElementName = "Name", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public string Name { get; set; }
    }

    public class Filter
    {
        [XmlAttribute(AttributeName = "type", Namespace = "http://www.w3.org/2001/XMLSchema-instance")]
        public string Type { get; set; }

        [XmlElement(ElementName = "SqlExpression", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public string SqlExpression { get; set; }
    }

    public class Action
    {
        [XmlAttribute(AttributeName = "type", Namespace = "http://www.w3.org/2001/XMLSchema-instance")]
        public string Type { get; set; }

        [XmlElement(ElementName = "SqlExpression", Namespace = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect")]
        public string SqlExpression { get; set; }
    }
}
