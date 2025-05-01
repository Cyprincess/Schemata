using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Schemata.Abstractions.Entities;
using Schemata.Resource.Foundation.Grammars.Terms;
using Schemata.Resource.Foundation.Models;

namespace Schemata.Resource.Foundation;

public class ResourceRequestContainer<T>
{
    public Func<IQueryable<T>, IQueryable<T>> Query { get; private set; } = q => q;

    public void ApplyFiltering(Filter filter) {
        Query = Query.WithFiltering(filter);
    }

    public void ApplyOrdering(Dictionary<Member, Ordering>? order) {
        Query = Query.WithOrdering(order);
    }

    public void ApplyPaginating(PageToken token) {
        Query = Query.WithPaginating(token);
    }

    public void ApplyModification(Expression<Func<T, bool>> predicate) {
        var query = Query;
        Query = q => query(q).Where(predicate);
    }
}
