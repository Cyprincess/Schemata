using System.Collections.Generic;

namespace Schemata.Entity.LinqToDB.Integration.Tests;

internal interface ITagged
{
    ICollection<int>? Tags { get; set; }
}