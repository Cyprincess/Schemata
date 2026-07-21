namespace Schemata.Common;

/// <summary>Supplies offset pagination values to a composed query.</summary>
public interface IPagination
{
    /// <summary>The number of rows to skip before the current page.</summary>
    int Skip { get; }

    /// <summary>The number of rows to include in the current page.</summary>
    int PageSize { get; }
}
