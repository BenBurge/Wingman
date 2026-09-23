using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Wingman.Tui;

/// <summary>
/// A form that takes a list tab's whole content area, as the batch runner does, until it raises
/// <see cref="Closed"/>. Tab and Shift+Tab move between <see cref="Fields"/> in order; a
/// <see cref="FormTextField"/> hands every key to <see cref="HandleFormKey"/> before typing it, so
/// the form's keys work from inside a text box too.
/// </summary>
internal abstract class FormView : View
{
    protected FormView()
    {
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();
        CanFocus = true;
    }

    /// <summary>Raised when the form is saved or canceled; the tab then takes it down.</summary>
    public event Action? Closed;

    /// <summary>The key bar entries while the form shows, before the shell's <c>? Help</c> and <c>q Quit</c>.</summary>
    public abstract IReadOnlyList<KeyHint> Hints { get; }

    /// <summary>The form's keys in the help overlay.</summary>
    public abstract HelpGroup Help { get; }

    /// <summary>Whether closing the form now would lose edits, which makes <c>q</c> ask before quitting.</summary>
    public virtual bool HasUnsavedChanges => false;

    /// <summary>The focusable fields in Tab order.</summary>
    protected abstract IReadOnlyList<View> Fields { get; }

    /// <summary>Focuses the first field, for when the form opens.</summary>
    public virtual void FocusFirstField()
    {
        if (Fields.Count > 0)
        {
            Fields[0].SetFocus();
        }
    }

    /// <summary>Moves focus <paramref name="step"/> fields forward, or backward when negative, wrapping at the ends.</summary>
    protected void FocusField(int step)
    {
        var fields = Fields;
        if (fields.Count == 0)
        {
            return;
        }

        var current = -1;
        for (var i = 0; i < fields.Count; i++)
        {
            if (fields[i].HasFocus)
            {
                current = i;
                break;
            }
        }

        var next = current < 0 ? 0 : (current + step + fields.Count) % fields.Count;
        fields[next].SetFocus();
    }

    protected void Close() => Closed?.Invoke();

    /// <summary>A text box whose keys reach <see cref="HandleFormKey"/> first.</summary>
    protected FormTextField CreateTextField(Theme theme)
    {
        var field = new FormTextField(theme) { FormKeys = HandleFormKey };
        field.HasFocusChanged += (_, _) => SetNeedsDraw();
        return field;
    }

    /// <summary>The keys the whole form answers to, whichever field has focus; true when handled.</summary>
    protected virtual bool HandleFormKey(Key key)
    {
        if (key == Key.Tab)
        {
            FocusField(1);
            return true;
        }

        if (key == Key.Tab.WithShift)
        {
            FocusField(-1);
            return true;
        }

        return false;
    }

    /// <summary>
    /// The arrows are taken even when unused, because left unhandled they would move focus off
    /// the form; a text box has already used them by the time they get here.
    /// </summary>
    protected override bool OnKeyDown(Key key)
    {
        if (HandleFormKey(key))
        {
            return true;
        }

        var isArrowKey = key == Key.CursorLeft || key == Key.CursorRight || key == Key.CursorUp || key == Key.CursorDown;
        return isArrowKey || base.OnKeyDown(key);
    }
}
