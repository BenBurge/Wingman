namespace Wingman.Core.Notifications;

/// <summary>
/// One button on a toast, deep-linking back into Wingman through the <c>wingman:</c> protocol, or
/// opening a web page through an <c>https:</c> link.
/// </summary>
public sealed record ToastAction(string Content, string ProtocolUri);

/// <summary>
/// A toast <see cref="ToastBuilder"/> wants shown, independent of how it is rendered.
/// <paramref name="Tag"/> and <paramref name="Group"/> identify the toast to Windows so a later
/// one of the same kind replaces it instead of stacking.
/// </summary>
public sealed record ToastContent(string Title, string Body, IReadOnlyList<ToastAction> Actions, string Tag, string Group);
