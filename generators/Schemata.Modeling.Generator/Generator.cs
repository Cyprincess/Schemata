using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Schemata.Modeling.Generator.Expressions;

namespace Schemata.Modeling.Generator;

[Generator]
public class Generator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor InvalidDocument = new("SKM002", "Invalid modeling document", "Unable to parse modeling document", "Modeling", DiagnosticSeverity.Error, true);
    #region IIncrementalGenerator Members

    public void Initialize(IncrementalGeneratorInitializationContext context) {
        var documents = context.AdditionalTextsProvider
                               .Where(static file => file.Path.EndsWith(".skm"))
                               .Select(static (text, ct) => (text.Path, Source: text.GetText(ct)?.ToString()))
                               .Select(static (input, _) => Parse(input.Path, input.Source));

        context.RegisterSourceOutput(documents.Combine(context.CompilationProvider), static (spc, pair) => {
            var (input, compilation) = pair;
            var location = input.Error.Offset < 0
                ? Location.Create(input.Path, new TextSpan(0, 0), new LinePositionSpan(new(0, 0), new(0, 0)))
                : CreateLocation(input.Path, input.Error);
            if (input.Document is not { } doc) {
                spc.ReportDiagnostic(Diagnostic.Create(InvalidDocument, location));
                return;
            }
            DocumentGenerator.Generate(spc, doc, compilation, location);
        });
    }

    #endregion

    // Unknown field, pointer, and view options are the one user-facing parse
    // rejection: they become an invalid-document result carrying the offending
    // position instead of escaping the generator host callback. Any other
    // exception is a generator bug and keeps propagating.
    private static (string Path, Document? Document, ParseError Error) Parse(string path, string? source) {
        if (source is null) {
            return (path, null, ParseError.None);
        }

        try {
            return (path, Parser.Document.Parse(source), ParseError.None);
        }
        catch (UnknownOptionException exception) {
            return (path, null, Locate(source, exception.Offset));
        }
    }

    private static ParseError Locate(string source, int offset) {
        var line   = 1;
        var column = 1;
        for (var i = 0; i < offset && i < source.Length; i++) {
            if (source[i] == '\n') {
                ++line;
                column = 1;
            }
            else {
                ++column;
            }
        }

        return new(offset, line, column);
    }

    private static Location CreateLocation(string path, ParseError error) {
        var start = new LinePosition(error.Line - 1, error.Column - 1);
        return Location.Create(path, new TextSpan(error.Offset, 0), new LinePositionSpan(start, start));
    }

    private readonly record struct ParseError(int Offset, int Line, int Column)
    {
        internal static readonly ParseError None = new(-1, 0, 0);
    }
}
