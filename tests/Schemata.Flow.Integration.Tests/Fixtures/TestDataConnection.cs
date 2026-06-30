using LinqToDB;
using LinqToDB.Data;
using Schemata.Flow.Skeleton.Entities;

namespace Schemata.Flow.Integration.Tests.Fixtures;

public sealed class TestDataConnection : DataConnection
{
    public TestDataConnection(DataOptions options) : base(options) { }

    public ITable<Order>                     Orders      => this.GetTable<Order>();
    public ITable<SchemataProcess>           Processes   => this.GetTable<SchemataProcess>();
    public ITable<SchemataProcessToken>      Tokens      => this.GetTable<SchemataProcessToken>();
    public ITable<SchemataProcessTransition> Transitions => this.GetTable<SchemataProcessTransition>();
    public ITable<SchemataProcessSource>     Sources     => this.GetTable<SchemataProcessSource>();
}
