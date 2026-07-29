using System.Text;
using System.Xml.Linq;

namespace TiaProjectExporter.Export.Stages;

public static class StructuredTextReconstructor
{
    public static StructuredTextReconstructionResult Reconstruct(string? exportXml, string? programmingLanguage = null)
    {
        if (string.IsNullOrWhiteSpace(exportXml))
        {
            return new StructuredTextReconstructionResult(null, "NoStructuredText", "No exportXml content available.");
        }

        try
        {
            var document = XDocument.Parse(exportXml, LoadOptions.PreserveWhitespace);
            var structuredTextNodes = document
                .Descendants()
                .Where(element => element.Name.LocalName.Equals("StructuredText", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (structuredTextNodes.Length == 0)
            {
                return new StructuredTextReconstructionResult(null, "NoStructuredText", "No <StructuredText> node found in exportXml.");
            }

            var fragments = new List<string>();
            var unsupportedElements = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var mode = DetermineMode(programmingLanguage);

            foreach (var structuredText in structuredTextNodes)
            {
                var builder = new StringBuilder();
                var context = new ReconstructionContext(unsupportedElements, mode);

                foreach (var node in structuredText.Nodes())
                {
                    AppendNode(node, builder, context);
                }

                var normalized = NormalizeOutput(builder.ToString());
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    fragments.Add(normalized);
                }
            }

            var reconstructed = string.Join(Environment.NewLine + Environment.NewLine, fragments);

            if (!string.IsNullOrWhiteSpace(reconstructed))
            {
                var diagnosticsPrefix = mode == ReconstructionMode.Awl
                    ? "AWL reconstructed successfully."
                    : "StructuredText reconstructed successfully.";

                var diagnostics = unsupportedElements.Count == 0
                    ? diagnosticsPrefix
                    : $"{diagnosticsPrefix} Unsupported elements ignored: {string.Join(", ", unsupportedElements.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))}.";

                return new StructuredTextReconstructionResult(reconstructed, "Success", diagnostics);
            }

            if (unsupportedElements.Count > 0)
            {
                return new StructuredTextReconstructionResult(
                    null,
                    "UnsupportedPattern",
                    $"No printable reconstruction result. Unsupported elements: {string.Join(", ", unsupportedElements.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))}.");
            }

            return new StructuredTextReconstructionResult(null, "NoStructuredText", "StructuredText exists but contains no reconstructable tokens.");
        }
        catch (Exception exception)
        {
            return new StructuredTextReconstructionResult(null, "ParseError", TruncateDiagnostics(exception.Message));
        }
    }

    private static void AppendNode(XNode node, StringBuilder builder, ReconstructionContext context)
    {
        if (node is XText textNode)
        {
            var decoded = Decode(textNode.Value);
            if (!string.IsNullOrWhiteSpace(decoded))
            {
                builder.Append(decoded);
            }

            return;
        }

        if (node is not XElement element)
        {
            return;
        }

        var localName = element.Name.LocalName;

        switch (localName)
        {
            case "Token":
                builder.Append(ResolveText(element));
                return;
            case "Blank":
                var count = TryParseBlankCount(element);
                builder.Append(' ', count);
                return;
            case "NewLine":
                builder.AppendLine();
                return;
            case "ConstantValue":
                builder.Append(ResolveText(element));
                return;
            case "LineComment":
                AppendLineComment(element, builder, context.Mode);
                return;
            case "Access":
                var accessPath = ResolveAccessPath(element);
                if (!string.IsNullOrWhiteSpace(accessPath))
                {
                    builder.Append(accessPath);
                    return;
                }

                context.UnsupportedElements.Add("Access");
                return;
            default:
                var hasElementChildren = element.Elements().Any();
                var hasNodeChildren = element.Nodes().Any();

                if (hasNodeChildren)
                {
                    foreach (var child in element.Nodes())
                    {
                        AppendNode(child, builder, context);
                    }

                    if (hasElementChildren && !IsKnownContainer(localName))
                    {
                        context.UnsupportedElements.Add(localName);
                    }

                    return;
                }

                var fallbackText = ResolveText(element);
                if (!string.IsNullOrWhiteSpace(fallbackText))
                {
                    builder.Append(fallbackText);
                    return;
                }

                context.UnsupportedElements.Add(localName);
                return;
        }
    }

    private static string ResolveAccessPath(XElement accessElement)
    {
        var symbol = accessElement
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName.Equals("Symbol", StringComparison.OrdinalIgnoreCase));

        if (symbol is null)
        {
            return string.Empty;
        }

        var components = symbol
            .Descendants()
            .Where(element => element.Name.LocalName.Equals("Component", StringComparison.OrdinalIgnoreCase))
            .Select(component =>
            {
                var name = component.Attribute("Name")?.Value;
                return string.IsNullOrWhiteSpace(name)
                    ? ResolveText(component)
                    : Decode(name);
            })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        return components.Length == 0
            ? string.Empty
            : string.Join(".", components);
    }

    private static void AppendLineComment(XElement commentElement, StringBuilder builder, ReconstructionMode mode)
    {
        var textElement = commentElement
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName.Equals("Text", StringComparison.OrdinalIgnoreCase));

        var text = textElement is null
            ? ResolveText(commentElement)
            : ResolveText(textElement);

        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var normalized = text.Trim();
        var commentPrefix = mode == ReconstructionMode.Awl ? "//" : "//";
        if (!normalized.StartsWith("//", StringComparison.Ordinal) && !normalized.StartsWith(";", StringComparison.Ordinal))
        {
            normalized = $"{commentPrefix} {normalized}";
        }

        builder.Append(normalized);
    }

    private static int TryParseBlankCount(XElement blank)
    {
        var rawCount = blank.Attribute("Count")?.Value;
        if (int.TryParse(rawCount, out var count) && count > 0)
        {
            return count;
        }

        var valueLength = ResolveText(blank).Length;
        return valueLength > 0 ? valueLength : 1;
    }

    private static bool IsKnownContainer(string localName) =>
        localName is "StructuredText"
            or "Parts"
            or "Part"
            or "NetworkSource"
            or "Implementation"
            or "CompileUnit"
            or "FlgNet"
            or "StatementList"
            or "Source"
            or "Text";

    private static string ResolveText(XElement element)
    {
        var attributeText = element.Attribute("Text")?.Value;
        if (!string.IsNullOrWhiteSpace(attributeText))
        {
            return Decode(attributeText);
        }

        var attributeName = element.Attribute("Name")?.Value;
        if (!string.IsNullOrWhiteSpace(attributeName))
        {
            return Decode(attributeName);
        }

        var value = element.Value;
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : Decode(value);
    }

    private static string Decode(string raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? string.Empty
            : System.Net.WebUtility.HtmlDecode(raw);

    private static string NormalizeOutput(string raw)
    {
        var normalized = raw
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("\n\n\n", "\n\n", StringComparison.Ordinal)
            .Trim();

        return normalized;
    }

    private static string TruncateDiagnostics(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "Unable to parse exportXml.";
        }

        return message.Length <= 280
            ? message
            : message[..280];
    }

    private static ReconstructionMode DetermineMode(string? programmingLanguage)
    {
        if (string.Equals(programmingLanguage, "AWL", StringComparison.OrdinalIgnoreCase)
            || string.Equals(programmingLanguage, "STL", StringComparison.OrdinalIgnoreCase))
        {
            return ReconstructionMode.Awl;
        }

        return ReconstructionMode.StructuredText;
    }

    private enum ReconstructionMode
    {
        StructuredText,
        Awl
    }

    private sealed record ReconstructionContext(HashSet<string> UnsupportedElements, ReconstructionMode Mode);
}

public sealed record StructuredTextReconstructionResult(
    string? ReconstructedSourceText,
    string ReconstructionStatus,
    string ReconstructionDiagnostics);
