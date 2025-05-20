using System.Xml.Linq;
using System.Xml.Serialization;

namespace ServiceBusApiEmulator;

public static class ResultExtensions
{
    private const string AtomNamespace = "http://www.w3.org/2005/Atom";

    public static IResult AtomXml(
        this IResultExtensions resultExtensions,
        string xml)
    {
        ArgumentNullException.ThrowIfNull(resultExtensions);
        return new AtomXmlResult(xml);
    }

    public static IResult AtomXml<T>(
        this IResultExtensions resultExtensions,
        string title,
        T entity)
    {
        
        var content = new XElement(
            XName.Get("content", AtomNamespace),
            new XAttribute("type", "application/xml"));
        using (var writer = content.CreateWriter())
        {
            // Prevent exception which occurs when writing an XML fragment
            // See https://stackoverflow.com/a/19047355
            writer.WriteString("");
            var serializer = new XmlSerializer(typeof(T));
            serializer.Serialize(writer, entity);
        }

        var document = new XDocument(
            new XElement(
                XName.Get("entry", AtomNamespace),
                new XElement(
                    XName.Get("title", AtomNamespace),
                    new XAttribute("type", "text"),
                    title),
                content));

        return AtomXml(resultExtensions, document.ToString());
    }
}
