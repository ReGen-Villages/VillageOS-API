# Watershed

**Works out a site's ground, as a pipeline step: heights over its border, slope, aspect, drainage, hollows, land cover and where the rain of a design storm runs.**

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

**Land cover** is read after the terrain's values are recorded, so a run tried again reuses the stored heights. It
needs only the border, not the heights. Watershed reads it itself from the ESA WorldCover 2021 tiles at the
`tileAddress` of the source the `landCoverRaster` role binds, by byte range and with no TIFF library:
- It reads every 3° tile the border's box touches, named by its south-west corner (N24E054). For each tile it reads
  the header, the block tables if they lie past the first 16 KiB, and then only the 1,024-pixel blocks the box touches.
  A run over a site of a few dozen hectares reads about 73 KB.
- A pixel counts when its centre is inside the border, at its own area at its own latitude. Each share is the share of
  that area, so a pixel whose code no land cover class holds, such as ESA's 0 for no data, is left out and the shares
  add up to 100. Each share gets its `pixelCount`, `areaHectares` and `sharePercent`, and a class with no pixel is written as 0.
- No tile for the place (ESA answers 404, as for open sea) or no pixel inside the border writes no share. A source
  that cannot be reached, or answers 429 or 5xx, can be tried again. A file in any layout but ESA's (big-endian,
  BigTIFF, a compression other than deflate, more than 8 bits or one sample a pixel, strips, blocks other than 1,024,
  a predictor, planar configuration 2, no pixel scale or tie point) fails the run and names what differs, as does an
  answer that is not the byte range asked for.

**Site layer features.** Each feature the site has, of the kind the `siteLayerFeature` role binds, is measured against the
border in metres east and north of the border's middle. Measuring needs only the border, not the heights. The site
reaches its features along `has`.
- A shape (Polygon or MultiPolygon, holes included) gets its `hectaresInside`, its `shareOfSitePercent` of the border's
  own area, and its `distanceFromSiteMetres`, which is 0 when the shape overlaps or touches the border.
- A line (LineString or MultiLineString) gets its `metresInside`, `crossesBorder` and `distanceFromSiteMetres`. A line
  lying along the border is not inside it, and a line crosses the border only where one of its segments passes through
  a border edge. Its distance is 0 when any part is inside.
- The area is exact: each outline is cut where it meets the other, and the pieces lying inside the other are summed.
  The parts of a MultiPolygon are taken not to overlap.
- Every figure that applies is written on every run, so a changed border leaves none behind. A feature whose geometry
  cannot be read is left as it is and named in the log, and a border enclosing no area measures none.

**Runoff.** Three grids of runoff classes are worked out on the same cells: the 1-in-5 and 1-in-100 year storms
on bare ground, and the 1-in-100 year storm with every cell inside the border built over. The method is the rational
method of the Dubai Municipality Stormwater Design Guidelines (version 02, July 2024, section 4.4).
The runoff coefficients, the roughness of the ground, the shortest time of concentration, the
length of sheet flow and the smallest slopes are the study's own correctable inputs. The rain comes from a table
reached through the site's Places, nearest first: of the tables that apply in that Place, the one used east of the
greatest longitude west of the site, else the one with no such longitude. Each RunoffClass holds the conditions a cell
meets to take it, and a class that can be met more than one way has alternatives, each holding its own. A cell takes
the class of highest order whose conditions hold, with all of one alternative's where it has any, and the cell holds
that class's `gridCode`. A site in no Place with a table, or a model with no runoff class, gets no runoff grid. A
table's intensities are reached along `has` by its name until the holding role replaces it (VillageOS 8417 and 8771).

**Standing water** gathers at the bottom of each hollow, the touching cells the levelling raised. The hollow's water is
the runoff leaving it, less what the hollows above it keep. Each pit inside it, split from the next by a ridge, fills
from the area draining into it, spills across its lowest ridge once full, and shares one level with the pit beside it
only once both are full to that ridge. Water past the hollow's spill level runs on from its lowest edge cell. A cell's
standing depth is how far that level stands above it. This is on purpose unlike a method that counts only the runoff
along each cell's own route, which puts water on a shelf the hollow's whole flow crosses.

**Writes.** The site's grids, profiles and figures go as one batch of observations, each share's values as one batch
on that share, and each site layer feature's figures as one batch on that feature. Because a batch is accepted before
it is stored, Watershed reads one value of each batch back, every 250 ms for at most 15 s, before it writes
`groundWorkedOutAt` on the study as a fact. The stamp is the last write of every run, including a run with no border,
or with no heights source, no land cover and no site layer feature, where nothing else is written. It is what takes the study out of
the range that started the run.

**Failures.** A model it cannot read or write, a provider at its limit or away, or a batch that never reads back is a
failure the orchestrator may try again, and the stamp is not written. Any other refusal by the provider, or an
answer holding fewer heights than points asked, fails the run.

**Times.** Each run logs, at information level, how many milliseconds and calls each part took: the model read, the
height calls, the land cover read, the observation writes, the read back, the pauses between read backs and the stamp.
It also logs the run's total, which includes the computing between calls. A run that stops part way, cancelled or
failed, still logs them and names the part it was in.

Mycelium launches it on the port its service Thing names, the first time a pipeline calls the connection that
reaches it. The deploy's build of the services checkout builds it with the rest of the solution.
