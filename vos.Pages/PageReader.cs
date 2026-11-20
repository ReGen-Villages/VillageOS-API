using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
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

    public async Task<IReadOnlyList<PageListing>> ListPagesAsync(string language, IPageReads reads, CancellationToken cancellation)
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
    public async Task<IReadOnlyList<PageScope>> ScopesOfAsync(string pageId, string language, IPageReads reads, CancellationToken cancellation)
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

    // One page read for each of the given scopes, in the order given, each as JSON. A null scope reads the
    // page across every Thing it compares. The page's Things are fetched and indexed once for all of them.
    // Names the page lists are ordered as the language orders them.
    public async Task<IReadOnlyList<string>> ReadPageAsync(
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
