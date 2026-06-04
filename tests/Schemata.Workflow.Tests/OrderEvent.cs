using System;
using Schemata.Abstractions.Entities;

namespace Schemata.Workflow.Tests;

public class OrderEvent : ITransition
{
    #region ITransition Members

    public string Event { get; set; } = null!;

    public string? Note { get; set; }

    public string? UpdatedByName { get; set; }

    #endregion
}
