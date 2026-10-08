using LinqToDB;
using LinqToDB.Data;

namespace Schemata.Scheduling.Integration.Tests.Fixtures;

public class SchedulingDataConnection(DataOptions options) : DataConnection(options);