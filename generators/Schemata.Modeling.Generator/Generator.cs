using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

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
                               .Select(static (input, _) => (input.Path, Document: input.Source is null ? null : Parser.Document.Parse(input.Source)));

        context.RegisterSourceOutput(documents.Combine(context.CompilationProvider), static (spc, pair) => {
            var (input, compilation) = pair;
            var location = Location.Create(input.Path, new TextSpan(0, 0), new LinePositionSpan(new(0, 0), new(0, 0)));
            if (input.Document is not { } doc) {
                spc.ReportDiagnostic(Diagnostic.Create(InvalidDocument, location));
                return;
            }
            DocumentGenerator.Generate(spc, doc, compilation, location);
        });
    }

    #endregion
}
