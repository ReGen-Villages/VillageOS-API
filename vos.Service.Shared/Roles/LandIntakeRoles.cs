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
    public static readonly Role SubmitterProDataPage = new("submitterProDataPage", new("f7976253-aff8-5f8e-b5cf-dd822312c2ee"));
    public static readonly Role SubmitterLiteDataPage = new("submitterLiteDataPage", new("5bf9dc23-f819-5480-9cc4-b57cec64540c"));
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
    public static readonly Role DiscoveredVocabulary = new("discoveredVocabulary", new("7f80ce0e-0cc2-58c4-a1ad-92df27b2cbbd"));
    public static readonly Role StudiedSite = new("studiedSite", new("84e004bc-835c-5888-81ab-2a8c70c1a086"));
    public static readonly Role ServiceOrder = new("serviceOrder", new("f5c493f4-5c42-5d83-9948-9850d3e41869"));
    public static readonly Role Parcel = new("parcel", new("af18300a-eebf-59ea-abab-79de771dc012"));
    public static readonly Role Project = new("project", new("6c039cfc-182e-5cc1-99b3-8c29fbf95d99"));
    public static readonly Role ProgrammeAllocation = new("programmeAllocation", new("bdaf6982-e8c4-526d-9b6f-3232ed1e60bc"));
    public static readonly Role HazardAssessment = new("hazardAssessment", new("7577e1af-e4ce-5f7c-a511-f92ba40a24a7"));
    public static readonly Role SubmittedSource = new("submittedSource", new("0c7e3a25-92e3-5ec8-b031-53a00a03d43c"));
    public static readonly Role Submission = new("submission", new("4eba92e8-7fb9-5137-b6dd-7de3c0248a09"));
    public static readonly Role Observed = new("observed", new("a44eff0a-1230-5b5f-93e6-a7cc0443b58b"));
    public static readonly Role BasemapSource = new("basemapSource", new("61066752-41d5-5bea-a1cd-5450e6603486"));
    public static readonly Role Predicate = new("predicate", new("86743302-6588-5a93-b6a5-0dde78b092c1"));
    public static readonly Role FallsBackTo = new("fallsBackTo", new("a5439b98-8724-5431-99ef-9c193026e40e"));
    public static readonly Role PlotsInViewLookup = new("plotsInViewLookup", new("bdb1e270-c1ef-55eb-8cfd-7cb053b23ae9"));
    public static readonly Role BorderElevation = new("borderElevation", new("bf5f80ab-6a4a-5d16-8f70-8f9c51005b72"));
    public static readonly Role SiteLayerKind = new("siteLayerKind", new("2ed90db2-4734-514b-aa12-c4f83c3be9bc"));
    public static readonly Role SiteLayerFeature = new("siteLayerFeature", new("da99b07b-fa78-581c-bb5f-7560773cbee4"));
    public static readonly Role OfLayerKind = new("ofLayerKind", new("f60956a5-cde5-5f08-8176-6e98f7d69d2b"));
    public static readonly Role SourcedFrom = new("sourcedFrom", new("7b89b20c-9f9c-5d60-9481-56c524dd1370"));
    public static readonly Role ShownUnder = new("shownUnder", new("70b382c6-edfa-5295-9b43-de0471b147a4"));
    public static readonly Role HistorySource = new("historySource", new("db993771-bdc0-513f-a5c2-dab3e999471d"));
    public static readonly Role Licence = new("licence", new("bfb62f83-4059-5c4d-93bb-2f2cee7a4e6f"));
    public static readonly Role Holding = new("holding", new("d04c93e4-ff1c-50f8-92bb-facbf2c208ff"));
    public static readonly Role SoilDepth = new("soilDepth", new("7adcafde-a041-5e55-bb29-c3a03bd35493"));
    public static readonly Role SoilHorizon = new("soilHorizon", new("67af9641-6c6e-56ac-b58b-20039cf040e1"));
    public static readonly Role LandCoverClass = new("landCoverClass", new("e849536c-e5de-52e1-a7c9-b0a59ee7f591"));
    public static readonly Role LandCoverShare = new("landCoverShare", new("865a1a58-ea9e-5699-8a76-73b48e705042"));
    public static readonly Role LandCoverRaster = new("landCoverRaster", new("e04416cd-ece6-5a3b-895f-5a550f4a1065"));
    public static readonly Role SlopeClass = new("slopeClass", new("5d4a58bc-6177-5b10-8619-f78851d54173"));
    public static readonly Role SlopeClassShare = new("slopeClassShare", new("35b8a44c-d5ff-5dc3-bc31-d53e596854a8"));
    public static readonly Role AspectDirection = new("aspectDirection", new("ee170eb3-5323-5601-88be-9c764477c447"));
    public static readonly Role AspectShare = new("aspectShare", new("eaff61c7-9ed9-5c26-9040-42c88f5d8f99"));
    public static readonly Role MeasuresAreaWith = new("measuresAreaWith", new("0ba45c9f-fdb9-53f1-9576-76fb041629e8"));
    public static readonly Role AreaMeasuredWith = new("areaMeasuredWith", new("b1cbb4b6-a7b6-50e7-b974-bbde68dd2d14"));
    public static readonly Role MeanSphereAreaMethod = new("meanSphereAreaMethod", new("5ec67ad4-faf3-54c5-bdd5-ccf6502b139b"));
    public static readonly Role Wgs84EllipsoidAreaMethod = new("wgs84EllipsoidAreaMethod", new("54961752-debb-572e-94b9-748a56921ba5"));
    public static readonly Role VerifiedRequestBudget = new("verifiedRequestBudget", new("f333535d-e10e-5ed6-b21f-9b9e1002f18f"));
    public static readonly Role UnverifiedRequestBudget = new("unverifiedRequestBudget", new("7d88b06c-605d-5d3f-8f4c-46c2801ecc1a"));
    public static readonly Role RunoffClass = new("runoffClass", new("152196ea-969e-589f-bbce-8e2a0d358c4f"));
    public static readonly Role RunoffClassAlternative = new("runoffClassAlternative", new("7e1b9c86-eeff-5955-b619-e0939d6f0fcd"));
    public static readonly Role AlternativeOf = new("alternativeOf", new("991a1daf-7239-5d6f-9e1a-b1254a6b4c65"));
    public static readonly Role DesignStorm = new("designStorm", new("b02b4a00-ff83-5434-8297-bce625291799"));
    public static readonly Role RainfallTable = new("rainfallTable", new("427b2470-d7f5-5f5c-a920-4b2041dfd452"));
    public static readonly Role RainfallIntensity = new("rainfallIntensity", new("90d71d24-ee08-52f3-b6c3-9c8f8db09400"));
    public static readonly Role ForStorm = new("forStorm", new("1d927020-c97b-52bf-ae13-762800989a4e"));
    public static readonly Role AppliesIn = new("appliesIn", new("ced2c68d-48f9-521d-a70b-bed43aa64e66"));

    public static readonly IReadOnlyList<Role> All =
    [
        SiteStudy, Site, AllocationCategory, CategorizedAs, WaterDemandComponent, PropertyName, ProposedSite,
        PersonalDetail, SharedDocument, Shares, BoundarySource, ObtainedBy, HazardType, Assesses, HazardLevel,
        ReportedLevel, SubmissionDisposition, ResolvedAs, Theme, SubmitterProDataPage, SubmitterLiteDataPage, RootPlace,
        PlaceNesting,
        PlaceArchetype, ProvidedVariable, AreaNameLookup, LocalAreaNameLookup, HazardDivisionLookup, ParcelLookup,
        PlaceSearch, SourceCoverage, DiscoveredVocabulary, StudiedSite, ServiceOrder, Parcel,
        Project, ProgrammeAllocation, HazardAssessment, SubmittedSource, Submission, Observed, BasemapSource,
        Predicate, FallsBackTo, PlotsInViewLookup, BorderElevation, SiteLayerKind, SiteLayerFeature, OfLayerKind,
        SourcedFrom, ShownUnder, HistorySource, Licence, Holding, SoilDepth, SoilHorizon, LandCoverClass, LandCoverShare, LandCoverRaster,
        SlopeClass, SlopeClassShare, AspectDirection, AspectShare,
        MeasuresAreaWith, AreaMeasuredWith, MeanSphereAreaMethod, Wgs84EllipsoidAreaMethod,
        VerifiedRequestBudget, UnverifiedRequestBudget,
        RunoffClass, RunoffClassAlternative, AlternativeOf, DesignStorm, RainfallTable, RainfallIntensity, ForStorm, AppliesIn,
    ];

    // A request naming any other role is refused before the model is read, so a stranger's browser reaches only
    // these pages and never whatever else a project binds.
    public static readonly IReadOnlyList<Role> SubmitterPages = [SubmitterProDataPage, SubmitterLiteDataPage];
}
