using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Wingman.Core.Models;
using Wingman.Core.Updates;
using Wingman.Core.Winget;

namespace Wingman.Tui;

/// <summary>
/// Chooses how Wingman treats one package's updates, in place of a list tab's content, laid out as
/// the mockup's update policy dialog: four levels with what each one does, a note, and a
/// suggestion when the package's last operation failed on a hash mismatch. The current policy is
/// chosen when it opens; Enter saves through <see cref="Shell.ApplyPolicyAsync"/> and Esc cancels.
/// </summary>
internal sealed class UpdatePolicyDialog : FormView, IThemedView
{
    private const int ChoicesRow = 2;
    private const int NoteRow = 15;
    private const int SuggestionRow = 17;
    private const string NoteLabel = " Note    ";
    private const string HashMismatchName = "INSTALLER_HASH_MISMATCH";

    private readonly Shell _shell;
    private Theme _theme;
    private readonly PackageRow _row;
    private readonly ChoiceList _choices;
    private readonly FormTextField _note;
    private readonly View[] _fields;
    private readonly KeyHint[] _hints;
    private readonly UpdatePolicyKind _openedWith;
    private readonly string _openedNote;
    private string? _suggestion;

    public UpdatePolicyDialog(Shell shell, PackageRow row)
    {
        _shell = shell;
        _theme = shell.Theme;
        _row = row;

        var canSkip = !string.IsNullOrEmpty(row.AvailableVersion);
        _choices = new ChoiceList(_theme, Choices(row, canSkip), shell.ResolvePolicy(row))
        {
            X = 0,
            Y = ChoicesRow,
            Width = Dim.Fill(),
        };

        _note = CreateTextField(_theme);
        _note.X = DisplayWidth.Of(NoteLabel) + 2;
        _note.Y = NoteRow;
        _note.Width = Dim.Fill(3);
        _note.Text = shell.PolicyNotes.GetValueOrDefault(row.Id) ?? "";
        _openedWith = _choices.Selected;
        _openedNote = _note.Text;

        _fields = [_choices, _note];
        _hints =
        [
            new(Key.Enter, "Save", Save, "⏎"),
            new(Key.Esc, "Cancel", Close),
            new(Key.CursorUp, "Choose", () => _choices.SetFocus(), "↑↓"),
            new(Key.Tab, "Note", () => FocusField(1)),
        ];

        Add(_choices, _note);
        shell.LoadLastFailure(row.Id, ShowSuggestion);
    }

    public override IReadOnlyList<KeyHint> Hints => _hints;

    public override HelpGroup Help { get; } = new("Update policy",
    [
        new("↑↓", "choose"),
        new("⏎", "save"),
        new("Esc", "cancel"),
        new("Tab", "note"),
    ]);

    public override bool HasUnsavedChanges => _choices.Selected != _openedWith || _note.Text != _openedNote;

    protected override IReadOnlyList<View> Fields => _fields;

    public void ApplyTheme(Theme theme) => _theme = theme;

    protected override bool HandleFormKey(Key key)
    {
        if (key == Key.Enter)
        {
            Save();
            return true;
        }

        if (key == Key.Esc)
        {
            Close();
            return true;
        }

        return base.HandleFormKey(key);
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var width = Viewport.Width;
        var dim = _theme.On(_theme.Dim);

        Move(0, 0);
        SetAttribute(_theme.On(_theme.Foreground, TextStyle.Bold));
        AddStr(" Update policy");
        SetAttribute(dim);
        AddStr(CellText.Fit("  " + Subtitle(), Math.Max(0, width - DisplayWidth.Of(" Update policy") - 1)));

        Move(0, NoteRow);
        SetAttribute(_theme.On(_theme.Foreground));
        AddStr(NoteLabel);
        SetAttribute(_note.HasFocus ? _theme.On(_theme.Accent, TextStyle.Bold) : _theme.On(_theme.Foreground));
        AddStr("[ ");
        Move(_note.Frame.X + _note.Frame.Width, NoteRow);
        AddStr(" ]");

        if (_suggestion is { } suggestion)
        {
            var y = SuggestionRow;
            SetAttribute(dim);
            foreach (var line in CellText.Wrap(suggestion, Math.Max(1, width - 2)))
            {
                if (y >= Viewport.Height)
                {
                    break;
                }

                Move(1, y);
                AddStr(line);
                y++;
            }
        }

        return true;
    }

    private static ChoiceList.Choice[] Choices(PackageRow row, bool canSkip)
    {
        var available = canSkip ? row.AvailableVersion : "the available version";
        return
        [
            new(UpdatePolicyKind.Update, "Update with Wingman", true,
                ["Default. Shows in Updates, included in \"update all\" and the overnight task."]),
            new(UpdatePolicyKind.Hold, $"Hold at {row.Version}", true,
                ["Stays in Updates, dimmed. Blocked until released.  → winget pin add --blocking"]),
            new(UpdatePolicyKind.SkipVersion, $"Skip {available} only", canSkip,
                ["Hidden until a newer version appears.  → Updates.IgnoredVersion"]),
            new(UpdatePolicyKind.Exclude, "Exclude: this app updates itself", true,
            [
                "Leaves Updates, auto-update, and toast counts. Still listed in Installed with a ⟳ marker.",
                "Uninstall and install options keep working.  → Updates.UpdatesIgnored = true",
            ]),
        ];
    }

    /// <summary><c>Git.Git · installed 2.51.0 · available 2.52.0</c>, without the last part when nothing newer is known.</summary>
    private string Subtitle()
    {
        var subtitle = $"{_row.Id} · installed {_row.Version}";
        return string.IsNullOrEmpty(_row.AvailableVersion) ? subtitle : $"{subtitle} · available {_row.AvailableVersion}";
    }

    /// <summary>Arrives from a background read of the history, possibly after the dialog has closed.</summary>
    private void ShowSuggestion(ErrorExplanation? lastFailure)
    {
        var isOpen = SuperView is not null;
        if (isOpen && lastFailure is { Name: HashMismatchName } explanation)
        {
            _suggestion = $"Suggested: Exclude. {explanation.Suggestion}";
            SetNeedsDraw();
        }
    }

    private void Save()
    {
        _shell.PolicyNotes[_row.Id] = _note.Text;
        Close();
        _ = _shell.ApplyPolicyAsync(_row, _choices.Selected, _note.Text);
    }

    /// <summary>
    /// The four levels, each a radio line with a bold label and its explanation lines dimmed under
    /// it. Up and down choose among the enabled ones and a click on a level's lines chooses it; the
    /// chosen line is drawn background-on-accent while the list has focus.
    /// </summary>
    private sealed class ChoiceList : View, IThemedView
    {
        private const string ExplanationIndent = "     ";

        private Theme _theme;
        private readonly Choice[] _choices;
        private readonly int[] _tops;
        private int _selected;

        public ChoiceList(Theme theme, Choice[] choices, UpdatePolicyKind current)
        {
            _theme = theme;
            _choices = choices;
            _tops = new int[choices.Length];

            var y = 0;
            for (var i = 0; i < choices.Length; i++)
            {
                _tops[i] = y;
                y += 1 + choices[i].Explanation.Count + 1;
                if (choices[i].Kind == current)
                {
                    _selected = i;
                }
            }

            Height = y - 1;
            CanFocus = true;
        }

        public UpdatePolicyKind Selected => _choices[_selected].Kind;

        public void ApplyTheme(Theme theme) => _theme = theme;

        protected override bool OnDrawingContent(DrawContext? context)
        {
            var width = Viewport.Width;
            for (var i = 0; i < _choices.Length; i++)
            {
                var choice = _choices[i];
                var isChosen = i == _selected;
                var labelColor = choice.IsEnabled ? _theme.On(_theme.Foreground, TextStyle.Bold) : _theme.On(_theme.Dim);
                var dot = isChosen ? "•" : " ";

                Move(0, _tops[i]);
                SetAttribute(_theme.On(_theme.Foreground));
                AddStr(" ");
                if (isChosen && HasFocus)
                {
                    SetAttribute(_theme.SelectedBold);
                    AddStr(CellText.Fit($"({dot}) {choice.Label}", width - 2));
                }
                else
                {
                    SetAttribute(choice.IsEnabled ? _theme.On(_theme.Foreground) : _theme.On(_theme.Dim));
                    AddStr("(");
                    SetAttribute(_theme.On(_theme.Accent));
                    AddStr(dot);
                    SetAttribute(choice.IsEnabled ? _theme.On(_theme.Foreground) : _theme.On(_theme.Dim));
                    AddStr(") ");
                    SetAttribute(labelColor);
                    AddStr(CellText.Fit(choice.Label, width - 6));
                }

                SetAttribute(_theme.On(_theme.Dim));
                for (var line = 0; line < choice.Explanation.Count; line++)
                {
                    Move(0, _tops[i] + 1 + line);
                    AddStr(CellText.Fit(ExplanationIndent + choice.Explanation[line], width));
                }
            }

            return true;
        }

        protected override bool OnKeyDown(Key key)
        {
            if (key == Key.CursorUp || key == Key.CursorDown)
            {
                Choose(key == Key.CursorUp ? -1 : 1);
                return true;
            }

            return base.OnKeyDown(key);
        }

        protected override bool OnMouseEvent(Mouse mouse)
        {
            if (!mouse.IsLeftClick() || mouse.Position is not { } position)
            {
                return base.OnMouseEvent(mouse);
            }

            SetFocus();
            for (var i = 0; i < _choices.Length; i++)
            {
                var isOnChoice = position.Y >= _tops[i] && position.Y <= _tops[i] + _choices[i].Explanation.Count;
                if (isOnChoice && _choices[i].IsEnabled)
                {
                    _selected = i;
                    SetNeedsDraw();
                }
            }

            return true;
        }

        protected override void OnHasFocusChanged(bool newHasFocus, View? previousFocusedView, View? focusedView) => SetNeedsDraw();

        /// <summary>Moves to the next enabled level in the direction of <paramref name="step"/>, staying put at the ends.</summary>
        private void Choose(int step)
        {
            for (var i = _selected + step; i >= 0 && i < _choices.Length; i += step)
            {
                if (_choices[i].IsEnabled)
                {
                    _selected = i;
                    SetNeedsDraw();
                    return;
                }
            }
        }

        public sealed record Choice(UpdatePolicyKind Kind, string Label, bool IsEnabled, IReadOnlyList<string> Explanation);
    }
}
