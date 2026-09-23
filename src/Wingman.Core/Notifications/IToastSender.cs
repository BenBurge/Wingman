namespace Wingman.Core.Notifications;

/// <summary>
/// Shows a toast built by <see cref="ToastBuilder"/>. The implementation is Windows-only and
/// never throws for a toast that could not be shown, so a toast cannot fail the command that
/// sent it; callers check the notification settings before calling.
/// </summary>
public interface IToastSender
{
    Task SendAsync(ToastContent content, CancellationToken ct);
}
