using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.ClearScript;
using Microsoft.ClearScript.V8;

[assembly: InternalsVisibleTo("vos.Pages.Tests")]

namespace vos.Pages;

// Lists and reads a model's pages with the binding reader the console uses, run in a JavaScript engine
// inside this process. Safe to call from several threads: the engine lets one caller in at a time, and
// a read that is waiting on IPageReads lets the others run. Nothing is kept between calls.
public sealed class PageReader : IDisposable
{
    private const string BundleResource = "vos.Pages.reader.bundle.js";
    private const double BytesInAMebibyte = 1024 * 1024;
    private const int EngineMemoryCeilingMebibytes = 128;

    private static readonly JsonSerializerOptions s_listingOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly V8ScriptEngine _engine;
    private long _calls;

    public PageReader(PageReaderOptions? options = null)
    {
        options ??= new PageReaderOptions();
        _engine = new V8ScriptEngine(
            new V8RuntimeConstraints { MaxOldSpaceSize = EngineMemoryCeilingMebibytes },
            V8ScriptEngineFlags.EnableTaskPromiseConversion);
        _engine.Execute(ReadBundle());
        if (options.Warned is { } warned) _engine.Script.pageReader.tellWarningsTo(warned);
    }

    public Task<IReadOnlyList<PageListing>> ListPagesAsync(string language, IPageReads reads, CancellationToken cancellation) =>
        InTheReadersWordsAsync(() => ListedAsync(language, reads, cancellation));

    private async Task<IReadOnlyList<PageListing>> ListedAsync(string language, IPageReads reads, CancellationToken cancellation)
    {
        var navigation = await HoldAsync(NavigationSelector(), reads, cancellation);
        try
        {
            var listed = (string)_engine.Script.pageReader.listPages(navigation, language);
            return JsonSerializer.Deserialize<List<PageListing>>(listed, s_listingOptions)!;
        }
        finally
        {
            Release(navigation);
        }
    }

    // The Things a page compares, each of which it can be read for, in the language's name order.
    public Task<IReadOnlyList<PageScope>> ScopesOfAsync(string pageId, string language, IPageReads reads, CancellationToken cancellation) =>
        InTheReadersWordsAsync(() => ComparedAsync(pageId, language, reads, cancellation));

    private async Task<IReadOnlyList<PageScope>> ComparedAsync(string pageId, string language, IPageReads reads, CancellationToken cancellation)
    {
        var page = await HoldAsync(await PageSelectorAsync(pageId, [], reads, cancellation), reads, cancellation);
        try
        {
            var scopes = (string)_engine.Script.pageReader.scopesOf(page, pageId, language);
            return JsonSerializer.Deserialize<List<PageScope>>(scopes, s_listingOptions)!;
        }
        finally
        {
            Release(page);
        }
    }

    // A null scope reads the page across every Thing it compares, and the page's Things are fetched once for all the scopes.
    public Task<IReadOnlyList<string>> ReadPageAsync(
        string pageId, IReadOnlyList<string?> scopeIds, string language, IPageReads reads, CancellationToken cancellation) =>
        InTheReadersWordsAsync(() => ReadAsync(pageId, scopeIds, language, reads, cancellation));

    private async Task<IReadOnlyList<string>> ReadAsync(
        string pageId, IReadOnlyList<string?> scopeIds, string language, IPageReads reads, CancellationToken cancellation)
    {
        var page = await HoldAsync(await PageSelectorAsync(pageId, scopeIds, reads, cancellation), reads, cancellation);
        try
        {
            var host = new ReadsForTheEngine(reads, cancellation);
            var answers = new List<string>(scopeIds.Count);
            foreach (var scopeId in scopeIds)
            {
                cancellation.ThrowIfCancellationRequested();
                var reading = (Task<object>)_engine.Script.pageReader.readPage(page, pageId, scopeId, language, host);
                answers.Add((string)await reading);
            }
            return answers;
        }
        finally
        {
            Release(page);
        }
    }

    /// <summary>
    /// The inputs at the bottom of a Thing's derived figures, of the one figure named or of all of them,
    /// as JSON: each with where its value is held, which way the figure moves with it, and the way out of
    /// each state the Thing holds that judges the figure; and the inputs left out, with the reason.
    /// </summary>
    public Task<string> InputsOfAsync(string thingId, string? figure, string language, IPageReads reads, CancellationToken cancellation) =>
        WithTheThingHeldAsync(thingId, reads, cancellation, async held =>
        {
            var host = new ReadsForTheEngine(reads, cancellation);
            return (string)await (Task<object>)_engine.Script.pageReader.inputsOfThing(held, thingId, figure, language, host);
        });

    /// <summary>Where a Thing's value for one property came from, as JSON.</summary>
    public Task<string> OriginOfAsync(string thingId, string property, IPageReads reads, CancellationToken cancellation) =>
        WithTheThingHeldAsync(thingId, reads, cancellation, held =>
            Task.FromResult((string)_engine.Script.pageReader.originOf(held, thingId, property)));

    /// <summary>
    /// One figure of a Thing from the model alone, as JSON: its value, the ranges that judge it, what it
    /// was worked out from, and its inputs. The model's own sentence for a figure is in a page.
    /// </summary>
    public Task<string> ExplainAsync(string thingId, string figure, string language, IPageReads reads, CancellationToken cancellation) =>
        WithTheThingHeldAsync(thingId, reads, cancellation, async held =>
        {
            var host = new ReadsForTheEngine(reads, cancellation);
            return (string)await (Task<object>)_engine.Script.pageReader.explain(held, thingId, figure, language, host);
        });

    public void Dispose() => _engine.Dispose();

    internal double EngineMemoryInUseMebibytes()
    {
        _engine.CollectGarbage(exhaustive: true);
        return _engine.GetRuntimeHeapInfo().UsedHeapSize / BytesInAMebibyte;
    }

    private async Task<string> PageSelectorAsync(
        string pageId, IReadOnlyList<string?> scopeIds, IPageReads reads, CancellationToken cancellation)
    {
        var navigation = await HoldAsync(NavigationSelector(), reads, cancellation);
        try
        {
            var scoped = JsonSerializer.Serialize(scopeIds.Where(scopeId => scopeId is not null).Distinct());
            return (string)_engine.Script.pageReader.selectorFor(navigation, pageId, scoped);
        }
        finally
        {
            Release(navigation);
        }
    }

    private Task<string> WithTheThingHeldAsync(
        string thingId, IPageReads reads, CancellationToken cancellation, Func<string, Task<string>> read) =>
        InTheReadersWordsAsync(() => HeldAndReadAsync(thingId, reads, cancellation, read));

    private async Task<string> HeldAndReadAsync(
        string thingId, IPageReads reads, CancellationToken cancellation, Func<string, Task<string>> read)
    {
        var held = await HoldAsync((string)_engine.Script.pageReader.selectorForThing(thingId, "[]"), reads, cancellation);
        try
        {
            // An input a total reads is named from the kind of its members, so where a figure of the Thing has one that kind is loaded as well.
            var memberKinds = (string)_engine.Script.pageReader.memberKindsOf(held, thingId);
            if (memberKinds != "[]")
            {
                Release(held);
                held = await HoldAsync((string)_engine.Script.pageReader.selectorForThing(thingId, memberKinds), reads, cancellation);
            }
            return await read(held);
        }
        finally
        {
            Release(held);
        }
    }

    /// <summary>The reader refuses by throwing inside the engine; its words are what a caller is given.</summary>
    private static async Task<T> InTheReadersWordsAsync<T>(Func<Task<T>> read)
    {
        try
        {
            return await read();
        }
        catch (ScriptEngineException refused)
        {
            const string errorPrefix = "Error: ";
            var words = refused.Message.StartsWith(errorPrefix, StringComparison.Ordinal) ? refused.Message[errorPrefix.Length..] : refused.Message;
            throw new PageNotReadException(words);
        }
    }

    private string NavigationSelector() => (string)_engine.Script.pageReader.navigationSelector();

    private async Task<string> HoldAsync(string selectorJson, IPageReads reads, CancellationToken cancellation)
    {
        var selected = await reads.SelectAsync(selectorJson, cancellation);
        var heldAs = Interlocked.Increment(ref _calls).ToString();
        _engine.Script.pageReader.hold(heldAs, selected.ThingsJson, selected.RelationshipsJson);
        return heldAs;
    }

    private void Release(string heldAs) => _engine.Script.pageReader.release(heldAs);

    private static string ReadBundle()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(BundleResource)
            ?? throw new InvalidOperationException($"The reader bundle is not embedded as {BundleResource}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
