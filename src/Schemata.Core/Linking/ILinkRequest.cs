namespace Schemata.Core.Linking;

public interface ILinkRequest
{
    string? GetInput(string name);
}
