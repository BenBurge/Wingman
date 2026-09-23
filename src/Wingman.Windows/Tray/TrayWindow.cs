using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;
using Wingman.Core.Settings;
using Wingman.Core.State;
using Wingman.Core.Tray;
using static Wingman.Windows.Tray.NativeMethods;

namespace Wingman.Windows.Tray;

/// <summary>
/// The hidden message-only window behind <see cref="TrayApp"/>: it owns the notification-area
/// icon, redraws it from <c>state.json</c> and <c>settings.json</c>, and launches <c>wingman</c>
/// subcommands from its click and menu actions. Progress is written with <see cref="Trace"/>.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class TrayWindow : IDisposable
{
    private const string WindowClassName = "Wingman.Tray";
    private const uint IconId = 1;
    private const uint IconCallbackMessage = WM_APP + 1;
    private const uint RefreshMessage = WM_APP + 2;
    private const nuint RefreshTimerId = 1;
    private const uint RefreshTimerMilliseconds = 60_000;
    private const int DebounceMilliseconds = 300;

    // Enter on a focused tray icon sends NIN_KEYSELECT twice; one launch per half second absorbs it.
    private const long RepeatOpenGuardMilliseconds = 500;

    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string SystemUsesLightTheme = "SystemUsesLightTheme";

    private readonly string _exePath;
    private readonly string _dataDirectory;
    private readonly string _trayAssetDirectory;
    private readonly StateStore _stateStore;
    private readonly SettingsStore _settingsStore;
    private readonly Dictionary<string, nint> _icons = [];

    // Held in a field so the garbage collector never frees the delegate Windows calls back into.
    private readonly WindowProcedure _windowProcedure;

    private nint _instance;
    private nint _hwnd;
    private bool _classRegistered;
    private bool _iconAdded;
    private bool _notificationsPaused;
    private nint _fallbackIcon;
    private long _lastOpenTicks;
    private FileSystemWatcher? _watcher;
    private Timer? _debounceTimer;

    private enum MenuCommand : uint
    {
        UpdateAll = 1,
        Open,
        CheckNow,
        PauseNotifications,
        Settings,
        Quit,
    }

    public TrayWindow(string exePath, string dataDirectory)
    {
        _exePath = exePath;
        _dataDirectory = dataDirectory;
        _trayAssetDirectory = Path.Combine(Path.GetDirectoryName(exePath) ?? "", "assets", "tray");
        _stateStore = new StateStore(dataDirectory);
        _settingsStore = new SettingsStore(dataDirectory);
        _windowProcedure = WindowProc;
    }

    public void Dispose()
    {
        Console.CancelKeyPress -= OnCancelKeyPress;
        _watcher?.Dispose();
        _debounceTimer?.Dispose();

        if (_hwnd != 0)
        {
            DestroyWindow(_hwnd);
        }

        foreach (var icon in _icons.Values)
        {
            DestroyIcon(icon);
        }

        _icons.Clear();

        if (_fallbackIcon != 0)
        {
            DestroyIcon(_fallbackIcon);
            _fallbackIcon = 0;
        }

        if (_classRegistered)
        {
            UnregisterClassW(WindowClassName, _instance);
            _classRegistered = false;
        }
    }

    /// <summary>Shows the icon and pumps messages until the window is destroyed; returns the exit code.</summary>
    public int RunMessageLoop()
    {
        // Makes SM_CXSMICON report the real small-icon size, so the shell gets the 20, 24, or 32
        // pixel frame from the .ico instead of stretching the 16 pixel one. Fails harmlessly
        // when the host already set an awareness.
        SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

        if (!CreateMessageWindow())
        {
            return 1;
        }

        Refresh("start");
        StartWatching();

        if (SetTimer(_hwnd, RefreshTimerId, RefreshTimerMilliseconds, 0) == 0)
        {
            Log($"SetTimer failed (error {Marshal.GetLastPInvokeError()})");
        }

        Console.CancelKeyPress += OnCancelKeyPress;
        Log("message loop running");

        int result;
        while ((result = GetMessageW(out var message, 0, 0, 0)) != 0)
        {
            if (result == -1)
            {
                Log($"GetMessageW failed (error {Marshal.GetLastPInvokeError()})");
                return 1;
            }

            TranslateMessage(ref message);
            DispatchMessageW(ref message);
        }

        Log("message loop ended");
        return 0;
    }

    private bool CreateMessageWindow()
    {
        _instance = GetModuleHandleW(null);
        var windowClass = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_windowProcedure),
            hInstance = _instance,
            lpszClassName = WindowClassName,
        };

        if (RegisterClassExW(ref windowClass) != 0)
        {
            _classRegistered = true;
        }
        else
        {
            var error = Marshal.GetLastPInvokeError();
            if (error != ERROR_CLASS_ALREADY_EXISTS)
            {
                Log($"RegisterClassExW failed (error {error})");
                return false;
            }
        }

        _hwnd = CreateWindowExW(0, WindowClassName, "Wingman", 0, 0, 0, 0, 0, HWND_MESSAGE, 0, _instance, 0);
        if (_hwnd == 0)
        {
            Log($"CreateWindowExW failed (error {Marshal.GetLastPInvokeError()})");
            return false;
        }

        Log($"message window 0x{_hwnd:X} created");
        return true;
    }

    private nint WindowProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case IconCallbackMessage:
                OnIconEvent(wParam, lParam);
                return 0;
            case RefreshMessage:
                Refresh("data file changed");
                return 0;
            case WM_TIMER when (nuint)wParam == RefreshTimerId:
                Refresh("timer");
                return 0;
            case WM_CLOSE:
                DestroyWindow(hwnd);
                return 0;
            case WM_DESTROY:
                RemoveIcon();
                _hwnd = 0;
                PostQuitMessage(0);
                return 0;
            default:
                return DefWindowProcW(hwnd, message, wParam, lParam);
        }
    }

    private void OnIconEvent(nint wParam, nint lParam)
    {
        // With NOTIFYICON_VERSION_4 the event is the low word of lParam and wParam carries the
        // anchor point, which is right for keyboard activation where the cursor is elsewhere.
        var iconEvent = (uint)(lParam & 0xFFFF);
        var x = (short)(wParam & 0xFFFF);
        var y = (short)((wParam >> 16) & 0xFFFF);

        switch (iconEvent)
        {
            case NIN_SELECT:
            case NIN_KEYSELECT:
                var now = Environment.TickCount64;
                if (now - _lastOpenTicks < RepeatOpenGuardMilliseconds)
                {
                    return;
                }

                _lastOpenTicks = now;
                StartWingman("open", "updates");
                break;
            case WM_CONTEXTMENU:
                ShowMenu(x, y);
                break;
        }
    }

    private void ShowMenu(int x, int y)
    {
        var menu = CreatePopupMenu();
        if (menu == 0)
        {
            Log($"CreatePopupMenu failed (error {Marshal.GetLastPInvokeError()})");
            return;
        }

        MenuCommand command;
        try
        {
            var pauseFlags = MF_STRING | (_notificationsPaused ? MF_CHECKED : 0);
            AppendMenuW(menu, MF_STRING, (nuint)MenuCommand.UpdateAll, "Update all");
            AppendMenuW(menu, MF_STRING, (nuint)MenuCommand.Open, "Open Wingman");
            AppendMenuW(menu, MF_STRING, (nuint)MenuCommand.CheckNow, "Check now");
            AppendMenuW(menu, MF_SEPARATOR, 0, null);
            AppendMenuW(menu, pauseFlags, (nuint)MenuCommand.PauseNotifications, "Pause notifications");
            AppendMenuW(menu, MF_STRING, (nuint)MenuCommand.Settings, "Settings");
            AppendMenuW(menu, MF_SEPARATOR, 0, null);
            AppendMenuW(menu, MF_STRING, (nuint)MenuCommand.Quit, "Quit");

            // The documented tray-menu recipe: without the foreground call the menu does not close
            // when the user clicks elsewhere, and the WM_NULL lets it dismiss on the next click.
            SetForegroundWindow(_hwnd);
            command = (MenuCommand)TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, x, y, _hwnd, 0);
            PostMessageW(_hwnd, WM_NULL, 0, 0);
        }
        finally
        {
            DestroyMenu(menu);
        }

        Execute(command);
    }

    private void Execute(MenuCommand command)
    {
        switch (command)
        {
            case MenuCommand.UpdateAll:
                StartWingman("open", "update-all");
                break;
            case MenuCommand.Open:
                StartWingman("open", "updates");
                break;
            case MenuCommand.CheckNow:
                CheckNow();
                break;
            case MenuCommand.PauseNotifications:
                ToggleNotificationsPaused();
                break;
            case MenuCommand.Settings:
                StartWingman("open", "settings");
                break;
            case MenuCommand.Quit:
                Log("quit chosen from the menu");
                DestroyWindow(_hwnd);
                break;
        }
    }

    /// <summary>Starts Wingman through the shell, so a console app gets a terminal window of its own.</summary>
    private void StartWingman(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(_exePath) { UseShellExecute = true };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo);
            Log($"started wingman {string.Join(' ', arguments)}");
        }
        catch (Win32Exception ex)
        {
            Log($"could not start wingman {string.Join(' ', arguments)}: {ex.Message}");
        }
    }

    private void CheckNow()
    {
        // Marks the run before the check starts so the badge flips at once instead of after the
        // check process gets around to writing state.json itself.
        if (!TryUpdateState(state => state.Running = true))
        {
            return;
        }

        var startInfo = new ProcessStartInfo(_exePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("check");
        startInfo.ArgumentList.Add("--notify");

        try
        {
            using var process = Process.Start(startInfo);
            Log("started wingman check --notify");
        }
        catch (Win32Exception ex)
        {
            Log($"could not start wingman check: {ex.Message}");
            TryUpdateState(state => state.Running = false);
        }

        Refresh("check now");
    }

    private bool TryUpdateState(Action<WingmanState> change)
    {
        try
        {
            _stateStore.Update(change);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log($"could not write {_stateStore.FilePath}: {ex.Message}");
            return false;
        }
    }

    private void ToggleNotificationsPaused()
    {
        try
        {
            var settings = _settingsStore.Load();
            settings.NotificationsPaused = !settings.NotificationsPaused;
            _settingsStore.Save(settings);
            Log($"notifications paused: {settings.NotificationsPaused}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log($"could not write {_settingsStore.FilePath}: {ex.Message}");
        }

        Refresh("pause toggled");
    }

    private void StartWatching()
    {
        try
        {
            Directory.CreateDirectory(_dataDirectory);
            _debounceTimer = new Timer(_ => PostMessageW(_hwnd, RefreshMessage, 0, 0));
            _watcher = new FileSystemWatcher(_dataDirectory, "*.json")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            _watcher.Changed += OnDataFileChanged;
            _watcher.Created += OnDataFileChanged;
            _watcher.Deleted += OnDataFileChanged;
            _watcher.Renamed += OnDataFileChanged;
            _watcher.Error += (_, _) => ScheduleRefresh();
            _watcher.EnableRaisingEvents = true;
            Log($"watching {_dataDirectory}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // The 60 second timer still picks up changes, just later.
            Log($"could not watch {_dataDirectory}: {ex.Message}");
        }
    }

    private void OnDataFileChanged(object sender, FileSystemEventArgs e) => ScheduleRefresh();

    /// <summary>
    /// Restarts the debounce window. Runs on a watcher thread, so it only posts a message and
    /// leaves the file reads and shell calls to the window's own thread.
    /// </summary>
    private void ScheduleRefresh() => _debounceTimer?.Change(DebounceMilliseconds, Timeout.Infinite);

    private void Refresh(string reason)
    {
        WingmanState state;
        WingmanSettings settings;
        try
        {
            state = _stateStore.Load();
            settings = _settingsStore.Load();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Usually a writer holding the file; its next write event schedules another refresh.
            Log($"refresh ({reason}) skipped: {ex.Message}");
            return;
        }

        _notificationsPaused = settings.NotificationsPaused;
        var badge = TrayIconState.From(state, settings);
        var iconFileName = TrayIconState.IconFileName(badge, IsLightTaskbar());
        var tooltip = TrayIconState.Tooltip(state, settings);
        var icon = LoadIcon(iconFileName);

        if (_iconAdded && ModifyIcon(icon, tooltip))
        {
            Log($"NIM_MODIFY ok ({reason}): {badge}, {iconFileName}, \"{tooltip}\"");
            return;
        }

        // A failed modify means Explorer restarted and dropped the icon; adding it again restores it.
        _iconAdded = AddIcon(icon, tooltip);
        Log($"NIM_ADD {(_iconAdded ? "ok" : "failed")} ({reason}): {badge}, {iconFileName}, \"{tooltip}\"");
    }

    private bool AddIcon(nint icon, string tooltip)
    {
        var data = NewIconData(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        data.hIcon = icon;
        data.szTip = tooltip;
        if (!Shell_NotifyIconW(NIM_ADD, ref data))
        {
            return false;
        }

        data.uVersion = NOTIFYICON_VERSION_4;
        var versionSet = Shell_NotifyIconW(NIM_SETVERSION, ref data);
        Log($"NIM_SETVERSION {(versionSet ? "ok" : "failed")}");
        return true;
    }

    private bool ModifyIcon(nint icon, string tooltip)
    {
        var data = NewIconData(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        data.hIcon = icon;
        data.szTip = tooltip;
        return Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    private void RemoveIcon()
    {
        if (!_iconAdded)
        {
            return;
        }

        var data = NewIconData(0);
        var removed = Shell_NotifyIconW(NIM_DELETE, ref data);
        _iconAdded = false;
        Log($"NIM_DELETE {(removed ? "ok" : "failed")}");
    }

    private NOTIFYICONDATAW NewIconData(uint flags) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = _hwnd,
        uID = IconId,
        uFlags = flags,
        uCallbackMessage = IconCallbackMessage,
        szTip = "",
        szInfo = "",
        szInfoTitle = "",
    };

    /// <summary>
    /// Loads <c>assets/tray/<paramref name="fileName"/></c> next to the exe once and caches it,
    /// falling back to the exe's own icon when the file is missing or unreadable.
    /// </summary>
    private nint LoadIcon(string fileName)
    {
        if (_icons.TryGetValue(fileName, out var cached))
        {
            return cached;
        }

        var path = Path.Combine(_trayAssetDirectory, fileName);
        if (File.Exists(path))
        {
            var width = GetSystemMetrics(SM_CXSMICON);
            var height = GetSystemMetrics(SM_CYSMICON);
            var icon = LoadImageW(0, path, IMAGE_ICON, width, height, LR_LOADFROMFILE);
            if (icon != 0)
            {
                _icons[fileName] = icon;
                Log($"loaded {path} at {width}x{height}");
                return icon;
            }

            Log($"LoadImageW failed for {path} (error {Marshal.GetLastPInvokeError()})");
        }
        else
        {
            Log($"{path} is missing; using the exe icon");
        }

        return FallbackIcon();
    }

    private nint FallbackIcon()
    {
        if (_fallbackIcon == 0)
        {
            // ExtractIconW returns 1 for a file that is not an executable or icon file.
            var icon = ExtractIconW(_instance, _exePath, 0);
            _fallbackIcon = icon > 1 ? icon : 0;
            Log(_fallbackIcon != 0 ? $"extracted the icon of {_exePath}" : $"{_exePath} has no icon");
        }

        return _fallbackIcon;
    }

    /// <summary>
    /// The taskbar follows the system theme, not the app theme. A missing value means a Windows
    /// version whose taskbar is always dark.
    /// </summary>
    private static bool IsLightTaskbar()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue(SystemUsesLightTheme) is int value && value != 0;
        }
        catch (Exception ex) when (ex is SecurityException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        // Close through the message thread so the icon is removed there before the process exits.
        e.Cancel = true;
        Log("Ctrl+C received; closing");
        PostMessageW(_hwnd, WM_CLOSE, 0, 0);
    }

    internal static void Log(string message) => Trace.WriteLine("Wingman.Tray: " + message);
}
