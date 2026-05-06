namespace Schemata.Flow.Skeleton.Models;

public sealed class StartProcessInstanceRequest
{
    public string DefinitionName { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? Variables { get; set; }
}
