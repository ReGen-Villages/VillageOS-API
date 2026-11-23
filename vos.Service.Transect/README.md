# Transect

**Measures a parcel's border again, as a pipeline step, with the area method the model configures.**

A pipeline hands Transect a Parcel on its `subject` input. Transect reads the Parcel's `boundary` and the method the
Parcel archetype relates to along `measuresAreaWith`, found through the roles that bind each method. It measures
with `BorderArea` from `vos.Service.Shared`, the formula the intake measures a submission with, and writes one
fragment: the Parcel's `measuredAreaHectares`, its `areaMeasuredWith` relationship to the chosen method, and the
retraction of its relationship to every other method. It answers the same Parcel on its `subject` output.

A call it cannot measure fails naming why and writes nothing: no subject, a Parcel the model does not hold, no
border or one of fewer than three corners, a border that is not a polygon, or a model choosing no method, two, or
one no implemented role binds. A model it cannot reach is a failure the orchestrator may try again.

Mycelium launches it on the port its service Thing names, the first time a pipeline calls the connection that
reaches it. The deploy's build of the services checkout builds it with the rest of the solution.
