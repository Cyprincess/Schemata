using LinqToDB;
using LinqToDB.Data;

namespace Schemata.Push.Tests.Fixtures;

public class PushDataConnection(DataOptions options) : DataConnection(options);
