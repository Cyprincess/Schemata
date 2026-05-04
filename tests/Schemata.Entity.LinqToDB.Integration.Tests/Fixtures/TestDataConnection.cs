using LinqToDB;
using LinqToDB.Data;

namespace Schemata.Entity.LinqToDB.Integration.Tests.Fixtures;

public class TestDataConnection : DataConnection
{
    public TestDataConnection(DataOptions options) : base(options) { }

    public ITable<Student> Students => this.GetTable<Student>();

    public ITable<Course> Courses => this.GetTable<Course>();
}
