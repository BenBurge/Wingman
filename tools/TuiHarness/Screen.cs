using System.Drawing;
using System.Runtime.CompilerServices;
using System.Text;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Testing;

namespace TuiHarness;

/// <summary>Drives the running app with injected input and appends every screen cell to <c>out.txt</c>.</summary>
internal sealed class Screen(IApplication app, int width, int height)
{
    public string OutputPath { get; } = Path.Combine(HarnessDirectory(), "out.txt");

    public void Reset() => File.WriteAllText(OutputPath, "");

    /// <summary>
    /// Holds the driver at the harness size. Between steps the ANSI driver's size monitor sometimes
    /// reports 120x30 instead, which would lay the app out at another size under the mouse
    /// coordinates and the checks that read the frame drawn between steps.
    /// </summary>
    public void HoldSize()
    {
        var driver = app.Driver!;
        driver.SizeChanged += (_, _) =>
        {
            if (driver.Cols != width || driver.Rows != height)
            {
                driver.SetScreenSize(width, height);
            }
        };
        driver.SetScreenSize(width, height);
    }

    public void Log(string text) => File.AppendAllText(OutputPath, text + "\n");

    /// <summary>
    /// Lays out and draws at the fixed size, then writes the text rows and, when
    /// <paramref name="withColors"/> is set, a map of one letter per cell with a legend of the attributes.
    /// </summary>
    public void Dump(string label, bool withColors)
    {
        app.Driver!.SetScreenSize(width, height);
        app.LayoutAndDraw(true);

        var cells = app.Driver.Contents!;
        var rows = cells.GetLength(0);
        var columns = cells.GetLength(1);
        var text = new StringBuilder();
        text.AppendLine("--- " + label);
        foreach (var row in Rows())
        {
            text.Append(row).AppendLine("|");
        }

        if (withColors)
        {
            var legend = new Dictionary<string, char>();
            for (var y = 0; y < rows; y++)
            {
                for (var x = 0; x < columns; x++)
                {
                    var attribute = cells[y, x].Attribute?.ToString() ?? "null";
                    if (!legend.TryGetValue(attribute, out var letter))
                    {
                        letter = (char)('a' + legend.Count);
                        legend[attribute] = letter;
                    }

                    text.Append(letter);
                }

                text.AppendLine();
            }

            foreach (var (attribute, letter) in legend)
            {
                text.AppendLine($"  {letter} = {attribute}");
            }
        }

        File.AppendAllText(OutputPath, text.ToString());
    }

    /// <summary>The screen's rows as last drawn, for checking what a frame shows.</summary>
    public IReadOnlyList<string> Rows()
    {
        var cells = app.Driver!.Contents!;
        var rows = new List<string>();
        for (var y = 0; y < cells.GetLength(0); y++)
        {
            var row = new StringBuilder();
            for (var x = 0; x < cells.GetLength(1); x++)
            {
                row.Append(cells[y, x].Grapheme ?? "?");
            }

            rows.Add(row.ToString());
        }

        return rows;
    }

    /// <summary>The attribute of the cell at column <paramref name="x"/>, row <paramref name="y"/> as last drawn, in the dump legend's format.</summary>
    public string AttributeAt(int x, int y) => app.Driver!.Contents![y, x].Attribute?.ToString() ?? "null";

    public void Click(int x, int y) => app.InjectSequence(InputInjectionExtensions.LeftButtonClick(new Point(x, y)));

    public void DoubleClick(int x, int y) => app.InjectSequence(InputInjectionExtensions.LeftButtonDoubleClick(new Point(x, y)));

    public void RightClick(int x, int y) => app.InjectSequence(InputInjectionExtensions.RightButtonClick(new Point(x, y)));

    public void Wheel(int x, int y, bool down) =>
        app.InjectMouse(new Mouse { ScreenPosition = new Point(x, y), Flags = down ? MouseFlags.WheeledDown : MouseFlags.WheeledUp });

    public void Press(Key key, int times = 1)
    {
        for (var i = 0; i < times; i++)
        {
            app.InjectKey(key);
        }
    }

    public void Type(string text)
    {
        foreach (var character in text)
        {
            app.InjectKey(new Key(character));
        }
    }

    private static string HarnessDirectory([CallerFilePath] string sourcePath = "") =>
        Path.GetDirectoryName(sourcePath) ?? Directory.GetCurrentDirectory();
}
