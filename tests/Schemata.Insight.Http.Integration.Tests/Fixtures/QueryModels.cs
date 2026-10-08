using System.Collections.Generic;

namespace Schemata.Insight.Http.Integration.Tests.Fixtures;

public sealed class StudentQuery { public string? FullName { get; set; } public int Age { get; set; } }
public sealed class BuyerQuery { public int Id { get; set; } public string? FullName { get; set; } }
public sealed class PurchaseQuery { public int BuyerId { get; set; } public int Amount { get; set; } public string? Status { get; set; } }
public sealed class CustomerQuery { public string? FullName { get; set; } public List<OrderQuery> Orders { get; set; } = []; }
public sealed class OrderQuery { public int Number { get; set; } public string? Status { get; set; } public int Amount { get; set; } public int Placed { get; set; } }
