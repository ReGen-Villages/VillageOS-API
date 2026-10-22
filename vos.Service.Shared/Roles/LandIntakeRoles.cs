namespace vos.Service.Shared.Roles;

// The roles the land-intake, open-data and site-analysis templates declare for the vocabularies a submission
// is resolved against and the Things a reviewer's page and commands read. Each binds one archetype, predicate
// or Thing; a reader asks for the role and follows its binding, and never names what the model calls the
// Thing it binds. The identifiers are the standard version 5 identifiers of the words.
public static class LandIntakeRoles
{
    public static readonly Role SiteStudy = new("siteStudy", new("e9036a23-21c1-5956-b0d8-a212e8ad8272"));
    public static readonly Role Site = new("site", new("84e29a09-b999-5b22-b82c-d101723d96d1"));
    public static readonly Role AllocationCategory = new("allocationCategory", new("2cdde53d-72b8-54d3-aaa5-e5a9cc56b9e6"));
    public static readonly Role CategorizedAs = new("categorizedAs", new("e0fd16d3-1feb-509b-8753-f4197b1288be"));
    public static readonly Role WaterDemandComponent = new("waterDemandComponent", new("69c31cd9-313d-56dc-b554-3aa0cc24d427"));
    public static readonly Role PropertyName = new("propertyName", new("a44ad23e-44ed-5992-8360-1fdabc1a6240"));
    public static readonly Role ProposedSite = new("proposedSite", new("ac8f6596-7dd4-53bb-bcc9-01d3f7b78b5b"));
    public static readonly Role PersonalDetail = new("personalDetail", new("2e154d0e-6477-5e2b-9d61-faf40f7d39d1"));
    public static readonly Role SharedDocument = new("sharedDocument", new("a3605555-fa7a-50c0-9422-3085734c5c22"));
    public static readonly Role Shares = new("shares", new("bdd517c7-e2d1-5b2f-a8ef-dad58e1fc8c7"));
    public static readonly Role BoundarySource = new("boundarySource", new("ec334dde-46d5-5696-9f80-5963dcea3dc7"));
    public static readonly Role ObtainedBy = new("obtainedBy", new("fffc87a4-7fef-5a2c-b56e-dee55bab3ea3"));
    public static readonly Role HazardType = new("hazardType", new("84c01d40-c4d9-56ec-b31a-b168be53e2f9"));
    public static readonly Role Assesses = new("assesses", new("d6515c5a-ce3e-5484-a1b4-ab19d5fb9876"));
    public static readonly Role HazardLevel = new("hazardLevel", new("0b02fd28-a6c5-5f1c-ac48-3b82fe33a462"));
    public static readonly Role ReportedLevel = new("reportedLevel", new("bb61c39c-019f-502b-9232-6f9acd848817"));
    public static readonly Role SubmissionDisposition = new("submissionDisposition", new("f4c54fc1-2c17-54bc-b8e8-c467e8fa321a"));
    public static readonly Role ResolvedAs = new("resolvedAs", new("92e4581a-4936-58b1-a783-36a051aa0a43"));
    public static readonly Role Theme = new("theme", new("d81072a6-1f26-5d4c-a8ce-59957f25ca29"));
    public static readonly Role SubmitterFindingsDashboard = new("submitterFindingsDashboard", new("3a2e1317-ea1d-5f38-be9d-6c2faf0a30a7"));
    public static readonly Role RootPlace = new("rootPlace", new("18d228a8-2bed-5b05-8026-2526cf939dfd"));
    public static readonly Role PlaceNesting = new("placeNesting", new("c3c9edde-b271-5a52-8f23-cc6bae9ea1d1"));
    public static readonly Role PlaceArchetype = new("placeArchetype", new("8360ea47-ae00-5727-bc79-27a518276327"));
    public static readonly Role ProvidedVariable = new("providedVariable", new("711cabb4-98b0-51ec-8448-5da579a7712d"));
    public static readonly Role AreaNameLookup = new("areaNameLookup", new("24d985a8-0f60-5645-88eb-ad622746b204"));
    public static readonly Role LocalAreaNameLookup = new("localAreaNameLookup", new("82ada0ec-1af6-57d6-b023-fc276162b432"));
    public static readonly Role HazardDivisionLookup = new("hazardDivisionLookup", new("9a6f7cd7-921a-5f83-9e41-3c5e9f11b4f1"));
    public static readonly Role ParcelLookup = new("parcelLookup", new("b4094d7a-944f-52ca-8ec5-856526b76195"));
    public static readonly Role PlaceSearch = new("placeSearch", new("cdb5e35c-ba1d-5e9f-be4e-330b3b234223"));
    public static readonly Role SourceCoverage = new("sourceCoverage", new("2b8de5b8-d8ff-503d-86d8-38b53c00d438"));
    public static readonly Role SiteAnalysisConnection = new("siteAnalysisConnection", new("b79434df-2c42-58a2-bdaa-db241a458804"));

    public static readonly IReadOnlyList<Role> All =
    [
        SiteStudy, Site, AllocationCategory, CategorizedAs, WaterDemandComponent, PropertyName, ProposedSite,
        PersonalDetail, SharedDocument, Shares, BoundarySource, ObtainedBy, HazardType, Assesses, HazardLevel,
        ReportedLevel, SubmissionDisposition, ResolvedAs, Theme, SubmitterFindingsDashboard, RootPlace, PlaceNesting,
        PlaceArchetype, ProvidedVariable, AreaNameLookup, LocalAreaNameLookup, HazardDivisionLookup, ParcelLookup,
        PlaceSearch, SourceCoverage, SiteAnalysisConnection,
    ];
}
