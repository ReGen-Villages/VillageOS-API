namespace vos.Service.Intake.Models;

// What a form draws itself with, for a page that holds no credential and so cannot read the
// model for itself: the programme categories a submission may name, the imagery a map may draw on,
// the split a page offers before anybody has stated a programme, whether the position lookups —
// the parcel boundary at a point, and place names for a typed search — are registered, so a page draws
// only what the model can honour, and what each offered term is called in each language the model
// states.
public sealed record FormOptions(
    IReadOnlyList<string> AllocationCategories, IReadOnlyList<DeclaredBasemapSource> BasemapSources,
    IReadOnlyList<string> HazardTypes, IReadOnlyList<string> HazardLevels,
    IReadOnlyList<DeclaredShare> DefaultProgramme, bool ParcelLookup, bool PlaceSearch,
    IReadOnlyList<DeclaredTheme> Themes, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Wording);

// A theme a section of the report may name, as the model declares it: the colour that faces
// its tile, the icon the page draws on it, and its place in the grid. Each is absent where the model
// states none, and passed through unjudged like a basemap's fields — the page that draws the tile is
// the one place that says what a face with no colour looks like.
public sealed record DeclaredTheme(string Name, string? Colour, string? Icon, long? Order);

// One category's share of the starting programme, as the category Thing itself states it. A
// category stating none is offered unchosen, exactly as it is today.
public sealed record DeclaredShare(string Category, double SharePct);

// A basemap source as the model states it. Which of the two addresses is filled in is what
// decides how a map loads it, and the credit is what its licence obliges the page to display — all three
// are the model's answer, and none is judged here. The client that draws the map is the one place that
// says what makes a source usable, so a form and the planner's page cannot disagree about it.
// The last four are what a map needs to draw land rather than a diagram: the elevation tiles that
// shape the ground, how they encode a height, how far to exaggerate it, and the layer inside the
// source's own tiles that holds building footprints. Absent where the model declares none, which draws
// flat — and passed through unjudged, like the rest.
public sealed record DeclaredBasemapSource(
    Guid Id, string Name, string? Attribution, string? StyleUrl, string? TileUrl, double? MaximumZoom,
    string? TerrainTileUrl, string? TerrainEncoding, double? TerrainExaggeration,
    string? BuildingSourceLayer);
