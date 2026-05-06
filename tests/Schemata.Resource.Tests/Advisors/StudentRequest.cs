using Schemata.Abstractions.Resource;
using Schemata.Resource.Tests.Fixtures;

namespace Schemata.Resource.Tests.Advisors;

public class StudentRequest : Student, IRequestIdentification
{
    #region IRequestIdentification Members

    public string? RequestId { get; set; }

    #endregion
}
