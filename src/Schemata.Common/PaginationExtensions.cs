namespace System.Linq;

/// <summary>Composes offset pagination onto query functions.</summary>
public static class PaginationExtensions
{
    /// <summary>Extends a query function with skip/take pagination.</summary>
    public static Func<IQueryable<T>, IQueryable<T>> WithPaginating<T>(
        this Func<IQueryable<T>, IQueryable<T>> query,
        Schemata.Common.IPagination?            pagination,
        int                                     lookahead = 0
    ) {
        if (pagination is null) {
            return query;
        }

        var build = query;
        return q => build(q).Skip(pagination.Skip).Take(pagination.PageSize + lookahead);
    }
}
