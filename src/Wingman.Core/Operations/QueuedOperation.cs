using Wingman.Core.Models;

namespace Wingman.Core.Operations;

/// <summary>
/// One entry in an <see cref="OperationQueue"/>: the package it targets and the plan built for it.
/// </summary>
public sealed record QueuedOperation(OperationKind Kind, PackageRow Row, OperationPlan Plan);
