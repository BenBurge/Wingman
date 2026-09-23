using Terminal.Gui.ViewBase;
using Wingman.Core.Models;

namespace Wingman.Tui.Tabs;

/// <summary>
/// A tab whose content area a <see cref="BatchRunnerScreen"/> or a <see cref="FormView"/> can take
/// over: a batch screen until the batch is over and dismissed, a form until it closes. The tab
/// hides its own views meanwhile, and its key bar and help follow whichever has the content area.
/// </summary>
internal abstract class ScreenHostTab : ShellTab
{
    private BatchRunnerScreen? _batchScreen;
    private FormView? _form;

    protected ScreenHostTab(Shell shell, string title)
        : base(title)
    {
        Shell = shell;
        CanFocus = true;
    }

    public sealed override IReadOnlyList<KeyHint> Hints => _batchScreen?.Hints ?? _form?.Hints ?? TableHints;

    public override bool ShowsHelpHint => !IsBatchShown;

    public sealed override IReadOnlyList<HelpGroup> HelpGroups
    {
        get
        {
            if (IsBatchShown)
            {
                return [BatchRunnerScreen.Help];
            }

            return _form is { } form ? [form.Help] : [TabHelp];
        }
    }

    /// <summary>Puts <paramref name="screen"/> in place of the tab's own views, and gives it focus.</summary>
    public void ShowBatchScreen(BatchRunnerScreen screen)
    {
        _batchScreen = screen;
        Add(screen);

        // Focused first, so hiding the focused table does not leave focus to Terminal.Gui's choice.
        screen.SetFocus();
        HideContent();
    }

    /// <summary>Whether a form has the tab's content area with edits that closing it would lose.</summary>
    public bool HasUnsavedForm => _form?.HasUnsavedChanges ?? false;

    /// <summary>Opens the bundle export screen in place of the tab's own views.</summary>
    public void OpenBundleExport() => ShowForm(new BundleExportScreen(Shell));

    /// <summary>Opens the bundle import screen in place of the tab's own views; its plan runs as a batch on this tab.</summary>
    public void OpenBundleImport() => ShowForm(new BundleImportScreen(Shell, this));

    /// <summary>Takes the batch screen down and disposes it, putting the tab's own views back.</summary>
    public void HideBatchScreen()
    {
        if (_batchScreen is not { } screen)
        {
            return;
        }

        _batchScreen = null;
        RestoreContent(screen);
    }

    protected Shell Shell { get; }

    /// <summary>The tab's own keys, shown while its own views are.</summary>
    protected abstract IReadOnlyList<KeyHint> TableHints { get; }

    /// <summary>The tab's own keys in the help overlay.</summary>
    protected abstract HelpGroup TabHelp { get; }

    /// <summary>Whether a batch screen has the tab's content area.</summary>
    protected bool IsBatchShown => _batchScreen is not null;

    /// <summary>Whether a form, such as the install options editor or a bundle screen, has the tab's content area.</summary>
    protected bool IsFormShown => _form is not null;

    /// <summary>Asks on the message line whether to export or import a bundle, for <c>b</c>.</summary>
    protected void ChooseBundle()
    {
        if (IsBatchShown || IsFormShown)
        {
            return;
        }

        Shell.AskChoice("Bundle: e export, i import, Esc cancel",
        [
            new Shell.PromptChoice('e', "Export", OpenBundleExport),
            new Shell.PromptChoice('i', "Import", OpenBundleImport),
        ]);
    }

    /// <summary>Opens the install options editor for <paramref name="row"/> in place of the tab's own views.</summary>
    protected void OpenOptions(PackageRow row) => ShowForm(new InstallOptionsEditor(Shell, row));

    /// <summary>Puts <paramref name="form"/> in place of the tab's own views until it closes, focused on its first field.</summary>
    protected void ShowForm(FormView form)
    {
        if (IsBatchShown || IsFormShown)
        {
            return;
        }

        _form = form;
        form.Closed += () => HideForm(form);
        Add(form);

        // Focused first, so hiding the focused table does not leave focus to Terminal.Gui's choice.
        form.FocusFirstField();
        HideContent();
        Shell.RefreshHints(this);
    }

    /// <summary>
    /// Focuses the batch screen or the form when one is showing, so coming back to the tab keeps the
    /// keys on it, and the table otherwise.
    /// </summary>
    protected void FocusContent()
    {
        if (_batchScreen is { } screen)
        {
            screen.SetFocus();
        }
        else if (_form is { } form)
        {
            form.SetFocus();
        }
        else
        {
            FocusTable();
        }
    }

    /// <summary>Hides the tab's own views; the batch screen or form that replaces them already has focus.</summary>
    protected abstract void HideContent();

    /// <summary>Shows the tab's own views again and focuses the table, before the screen that had the area is removed.</summary>
    protected abstract void ShowContent();

    protected abstract void FocusTable();

    private void HideForm(FormView form)
    {
        if (_form != form)
        {
            return;
        }

        _form = null;
        RestoreContent(form);
        Shell.RefreshHints(this);
    }

    private void RestoreContent(View screen)
    {
        ShowContent();
        Remove(screen);
        screen.Dispose();
    }
}
