using Microsoft.CodeAnalysis;

namespace Schemata.Modeling.Generator;

[Generator]
public class Generator : IIncrementalGenerator
{
    #region IIncrementalGenerator Members

    public void Initialize(IncrementalGeneratorInitializationContext context) {
        var documents = context.AdditionalTextsProvider
                               .Where(static file => file.Path.EndsWith(".skm"))
                               .Select(static (text,   ct) => text.GetText(ct)!.ToString())
                               .Select(static (source, ct) => Parser.Document.Parse(source));

        context.RegisterSourceOutput(documents, static (spc, doc) => {
            if (doc is null) return;
            DocumentGenerator.Generate(spc, doc);
        });
    }

    #endregion
}
