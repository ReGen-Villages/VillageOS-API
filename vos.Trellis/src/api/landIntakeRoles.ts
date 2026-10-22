/**
 * The roles the land-intake, open-data and site-analysis templates declare for the vocabularies a
 * submission is resolved against and the Things a reviewer's page reads. Each role is a Thing the model
 * declares under a fixed identifier, and it binds the one archetype, predicate or Thing playing it along
 * `binds`, so a page asks for the role and follows its binding and never names what the model calls the
 * Thing it binds. Each identifier is the standard version 5 identifier of the role's word; the intake service
 * and the command line read the same ones.
 */
export const LAND_INTAKE_ROLE = {
  SiteStudy: 'e9036a23-21c1-5956-b0d8-a212e8ad8272',
  Site: '84e29a09-b999-5b22-b82c-d101723d96d1',
  AllocationCategory: '2cdde53d-72b8-54d3-aaa5-e5a9cc56b9e6',
  CategorizedAs: 'e0fd16d3-1feb-509b-8753-f4197b1288be',
  WaterDemandComponent: '69c31cd9-313d-56dc-b554-3aa0cc24d427',
  PropertyName: 'a44ad23e-44ed-5992-8360-1fdabc1a6240',
  ProposedSite: 'ac8f6596-7dd4-53bb-bcc9-01d3f7b78b5b',
  PersonalDetail: '2e154d0e-6477-5e2b-9d61-faf40f7d39d1',
  SharedDocument: 'a3605555-fa7a-50c0-9422-3085734c5c22',
  Shares: 'bdd517c7-e2d1-5b2f-a8ef-dad58e1fc8c7',
  BoundarySource: 'ec334dde-46d5-5696-9f80-5963dcea3dc7',
  ObtainedBy: 'fffc87a4-7fef-5a2c-b56e-dee55bab3ea3',
  HazardType: '84c01d40-c4d9-56ec-b31a-b168be53e2f9',
  Assesses: 'd6515c5a-ce3e-5484-a1b4-ab19d5fb9876',
  HazardLevel: '0b02fd28-a6c5-5f1c-ac48-3b82fe33a462',
  ReportedLevel: 'bb61c39c-019f-502b-9232-6f9acd848817',
  SubmissionDisposition: 'f4c54fc1-2c17-54bc-b8e8-c467e8fa321a',
  ResolvedAs: '92e4581a-4936-58b1-a783-36a051aa0a43',
  Theme: 'd81072a6-1f26-5d4c-a8ce-59957f25ca29',
  SubmitterFindingsDashboard: '3a2e1317-ea1d-5f38-be9d-6c2faf0a30a7',
  RootPlace: '18d228a8-2bed-5b05-8026-2526cf939dfd',
  PlaceNesting: 'c3c9edde-b271-5a52-8f23-cc6bae9ea1d1',
  Decision: 'b29b4a43-211b-5d07-9ca1-761675bafc6b',
  Consideration: '9727515b-7d2d-5654-993a-0a672ca55626',
  Constraint: '389ba00e-3e4e-5559-8f9c-de9da5d4356d',
  DecisionSubject: 'aee6f88a-f53c-5ddf-a469-95d62544a857',
  Chosen: '969edafa-9d2b-56d7-b017-49ae65ce141b',
  DecidedUnder: '505d8032-d3b7-5c9b-8cfb-a8bdda126624',
  Decider: '83ee7bd0-bc28-5888-bce3-636a4bd27fea',
  Refusal: '530a0d7f-5cac-5bb9-8ca7-c2d6fb63b50b',
  Support: '9b0bc62e-90fa-5c77-9238-4b20b606053b',
  ConsiderationCause: 'a6a352aa-731f-5a89-a04c-48eef142d4fd',
  ConsiderationEvidence: 'fe9f09f2-1d77-5867-adcc-4db302552ae5',
  RefusedCandidate: '3634a526-0bab-5e7b-ae8f-6d822ce7c1b1',
} as const;

/** The platform's own predicate a role Thing reaches the Thing playing it along, and so found by name. */
export const BINDS_PREDICATE_NAME = 'binds';
