namespace vos.Service.Intake.Models;

/// <summary>What a form draws itself with, for a page that holds no credential and so cannot read the
/// model for itself: the programme categories a submission may name, and the imagery a map may draw on.
/// </summary>
public sealed record FormOptions(
    IReadOnlyList<string> AllocationCategories, IReadOnlyList<DeclaredBasemapSource> BasemapSources,
    IReadOnlyList<string> HazardTypes, IReadOnlyList<string> HazardLevels);

/// <summary>A basemap source as the model states it. Which of the two addresses is filled in is what
/// decides how a map loads it, and the credit is what its licence obliges the page to display — all three
/// are the model's answer, and none is judged here. The client that draws the map is the one place that
/// says what makes a source usable, so a form and the planner's page cannot disagree about it.</summary>
public sealed record DeclaredBasemapSource(
    Guid Id, string Name, string? Attribution, string? StyleUrl, string? TileUrl, double? MaximumZoom);
