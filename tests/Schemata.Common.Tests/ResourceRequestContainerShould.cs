using System.Linq;
using Schemata.Abstractions.Entities;
using Xunit;

namespace Schemata.Common.Tests;

public sealed class ResourceRequestContainerShould
{
    [Fact]
    public void Compose_Parent_Predicate_And_Pagination() {
        var container = new ResourceRequestContainer<ResourceRow>();
        container.ApplyWhere(row => row.Parent == "publishers/acme");
        container.ApplyPaginating(new Pagination { Skip = 1, PageSize = 1 });

        var rows = new[] {
            new ResourceRow { Name = "one", Parent = "publishers/acme" },
            new ResourceRow { Name = "two", Parent = "publishers/acme" },
            new ResourceRow { Name = "three", Parent = "publishers/other" },
        };
        var result = container.Query(rows.AsQueryable())
                              .Single();

        Assert.Equal("two", result.Name);
    }

    private sealed class Pagination : IPagination
    {
        public int Skip { get; init; }

        public int PageSize { get; init; }
    }

    private sealed class ResourceRow : ICanonicalName
    {
        public string? Name { get; set; }

        public string? CanonicalName { get; set; }

        public string? Parent { get; set; }
    }
}
