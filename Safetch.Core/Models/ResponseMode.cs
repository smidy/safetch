namespace Safetch.Core.Models;

public enum ResponseMode
{
    Raw,       // default — return full processed HTML/markdown as today
    Readable,  // extract main article content via SmartReader, return as HTML
    Text,      // extract main article content via SmartReader, return as plain text
    Markdown   // extract main article content via SmartReader, convert to Markdown
}