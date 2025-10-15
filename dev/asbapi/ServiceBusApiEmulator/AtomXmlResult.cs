using System.Text;

namespace ServiceBusApiEmulator;

public class AtomXmlResult(string xml) : IResult
{
    public Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.ContentType = "application/atom+xml;type=entry;charset=utf-8";
        httpContext.Response.ContentLength = Encoding.UTF8.GetByteCount(xml);
        return httpContext.Response.WriteAsync(xml);
    }
}
