# Watershed

**Works out a site's ground, as a pipeline step: heights over its border, slope, aspect, drainage and hollows.**

A pipeline hands Watershed a SiteStudy on its `subject` input. Watershed follows the study to the site it studies and
the site to its Parcel's `boundary`, the share Things the site has and the terms they assess. Each predicate and kind is
found through the role that binds it, except `has`, which no role binds yet and is read by its name.

**The height grid** covers the border's box plus 30 % on each side, at least 400 m a side, with cells of at least 30 m
and at most 28 points along a side. Its points run row by row from the south-west. The heights come from the source
the `borderElevation` role binds, asked through the platform's fetcher at most 100 points a call and one second apart,
because the free provider allows one call a second for the whole platform. A point the provider holds no height for
is open water at 0 m. A grid whose layout is the one the site already holds reuses the stored heights and asks for
none, so an unchanged border makes no call.

**From the grid** it works out slope by the Horn kernel, aspect where the slope is 1 % or more, drainage area by
priority flood and steepest descent, and hollows filled by half a metre or more. It reads the figures over the cells
inside the border, or over the whole grid for a site smaller than one cell. Each slope class share is the cells whose
slope the class's bounds hold, and each aspect share the cells facing within the sector around its centre; the aspect
term with no centre takes flat ground.

**The two profiles** run west to east and south to north through the middle of the box, 50 points each over the site
plus its margin (on each side of the box's centre, the larger of 1.3 × the site's half-length along that line and
250 m), asked in one more call. A
profile's `cellSizeMetres` is its real spacing, so distance along the line is the index times that spacing. The
site's span on each line is written in metres from the line's start.

**Writes.** The site's grids, profiles and figures go as one batch of observations, and each share's values as one
batch on that share. Because a batch is accepted before it is stored, Watershed reads one value of each batch back,
every 250 ms for at most 15 s, before it writes `groundWorkedOutAt` on the study as a fact. The stamp is the last
write of every run, including a run with no border or no heights source, where nothing else is written. It is what
takes the study out of the range that started the run.

**Failures.** A model it cannot read or write, a provider at its limit or away, or a batch that never reads back is a
failure the orchestrator may try again, and the stamp is not written. Any other refusal by the provider, or an
answer holding fewer heights than points asked, fails the run.

Mycelium launches it on the port its service Thing names, the first time a pipeline calls the connection that
reaches it. The deploy's build of the services checkout builds it with the rest of the solution.
