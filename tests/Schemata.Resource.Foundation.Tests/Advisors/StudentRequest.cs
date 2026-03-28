using Schemata.Abstractions.Resource;
using Schemata.Resource.Foundation.Tests.Fixtures;

namespace Schemata.Resource.Foundation.Tests.Advisors;

public class StudentRequest : Student, IRequestIdentification
{
    #region IRequestIdentification Members

    public string? RequestId { get; set; }

    #endregion
}
