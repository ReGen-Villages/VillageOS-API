namespace vos.Service.Forage.Helpers;

// What a service call named, as the model tells it. A site is fetched and recorded; a source is offered to
// the sites it covers and fetched about nothing; the other two answers are why a run wrote nothing.
public enum SubjectKind
{
    Site,

    Source,

    // Neither `is` the archetype the site role binds, nor covers a Place, nor is resolved by a registration: a
    // Place, a study, an archetype. Taken for a source it would be stamped as worked out — and a stamp on the
    // site archetype itself is inherited by every site, which would take all of them out of the state that
    // starts a run.
    NeitherSiteNorSource,

    // The model binds no site archetype, so the two cannot be told apart. Such a model cannot have
    // declared the connection that service calls a source either, so every subject is taken as a site — the
    // answer every service call got before there were two kinds.
    ModelBindsNoSite,
}
