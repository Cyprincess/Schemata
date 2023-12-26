using Microsoft.CodeAnalysis;

namespace Schemata.DSL;

[Generator]
public class Generator : IIncrementalGenerator
{
    #region IIncrementalGenerator Members

    public void Initialize(IncrementalGeneratorInitializationContext context) {
        var textFiles = context.AdditionalTextsProvider.Where(static file => file.Path.EndsWith(".skm"));
        var contents  = textFiles.Select((text, ct) => text.GetText(ct)!.ToString());
        context.RegisterSourceOutput(contents, ParseAndGenerate);
    }

    #endregion

    private void ParseAndGenerate(SourceProductionContext spc, string text) {
        var parser = Parser.Read(text);
        var mark   = parser.Parse();

        mark?.Generate(spc);
    }
}
