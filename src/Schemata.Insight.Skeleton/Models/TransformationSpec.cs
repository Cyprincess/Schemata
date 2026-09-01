namespace Schemata.Insight.Skeleton.Models;

/// <summary>One transformation in the pipeline; exactly one member is set.</summary>
public sealed class TransformationSpec
{
    public FilterTransform?  Filter  { get; set; }
    public ComputeTransform? Compute { get; set; }
    public GroupByTransform? GroupBy { get; set; }
    public OrderByTransform? OrderBy { get; set; }
    public TopTransform?     Top     { get; set; }
    public SkipTransform?    Skip    { get; set; }
}