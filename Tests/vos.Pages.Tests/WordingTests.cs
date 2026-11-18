using Microsoft.ClearScript.V8;
using Xunit;

namespace vos.Pages.Tests;

/// <summary>
/// The text a page is worded in is whatever the pinned engine's locale data produces. The expected file
/// holds that text for every number format, date label and name order in every page language, so an
/// engine upgrade shows each wording change it brings as a line of the file's diff.
/// </summary>
public class WordingTests
{
    [Fact]
    public void The_pinned_engine_words_numbers_dates_and_name_order_as_recorded()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Wording");
        using var engine = new V8ScriptEngine();

        var worded = (string)engine.Evaluate(File.ReadAllText(Path.Combine(folder, "cases.js")));

        var producedPath = Path.Combine(folder, "produced-by-this-engine.json");
        File.WriteAllText(producedPath, worded + "\n");
        Assert.True(
            worded + "\n" == File.ReadAllText(Path.Combine(folder, "worded-by-the-pinned-engine.json")),
            $"The engine words something differently from Wording/worded-by-the-pinned-engine.json. Compare it with {producedPath}; "
            + "if the engine version was changed on purpose, replace the recorded file with that one and read the diff.");
    }
}
