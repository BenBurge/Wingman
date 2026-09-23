using Wingman.Core.Models;

namespace Wingman.Core.Updates;

/// <summary>One row of the Updates tab: a package with an available upgrade and its resolved policy.</summary>
public sealed record UpdateRow(PackageRow Row, UpdatePolicyKind Policy);
