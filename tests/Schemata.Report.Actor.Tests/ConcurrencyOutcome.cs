using System;
using System.Collections.Generic;

namespace Schemata.Report.Actor.Tests;

internal sealed record ConcurrencyOutcome(IReadOnlyList<Exception> Conflicts);