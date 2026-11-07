# Quadrat — raster reader

Reads a raster at a point or over a zone. It is a Python package (`quadrat/`) with a command line, and a managed
microservice (`app.py`) that exposes the same operations to a pipeline as a node, built on
[`vos.Service.Python.Shared`](../vos.Service.Python.Shared/README.md).

Coordinates and zones are always WGS84 (lon/lat); Quadrat reprojects to the raster's own CRS.

## Run

```bash
python3 -m venv .venv
.venv/bin/python -m pip install -r requirements.txt     # on Windows: .venv/Scripts/python
.venv/bin/python -m pytest

.venv/bin/python -m quadrat metadata samples/tatra-z10-568-350.tif
.venv/bin/python -m quadrat point samples/tatra-z10-568-350.tif --lon 19.9814 --lat 49.2319 --resampling bilinear
.venv/bin/python -m quadrat zonal samples/tatra-z10-568-350.tif --bbox 19.92 49.24 19.95 49.26
.venv/bin/python -m quadrat zonal samples/tatra-z10-568-350.tif --geometry zone.geojson
.venv/bin/python -m quadrat histogram land-cover.tif --bbox 19.92 49.24 19.95 49.26
.venv/bin/python -m quadrat classify land-cover.tif --geometry zone.geojson --classes classes.json
```

A request Quadrat will not answer prints `{"refused": "<code>", "reason": "..."}` and exits 2. Codes:
`outside_raster`, `unreadable_raster`, `no_crs`, `unknown_resampling`, `unsupported_geometry`, `invalid_geometry`,
`invalid_bbox`, `rotated_raster`, `invalid_classes`, `too_many_values`.

## As a service

```bash
.venv/bin/python app.py --port=5110 --myceliumUrl=https://localhost:7243 --rasterDirectory=/data/rasters
```

The contract (arguments, `ApiKey` or `Token` and `VerificationKey` from the environment, registration, `/health`,
`/stats`, `/shutdown`) and the pipeline node's envelope, answer and manifest are the shared library's
([`NodeService`](../vos.Service.Python.Shared/README.md#answer-pipeline-nodes)). Quadrat answers pipeline nodes: a
`/handle` body carrying `runId` and `nodeId` ([docs/SERVICES.md §16.2](../docs/SERVICES.md)). Any other body is refused
with `400`. `GET /manifest` lists the ports. An input given as a graph reference (`{"ref": {"thingId", "property"}}`)
is read from the graph before the operation runs, and the source rules below then apply to what it held.

### What the manifest carries

`GET /manifest` is what a model needs to declare Quadrat, and `tools/pipeline-playground/declare_service.py` reads it
for that:

| | |
|---|---|
| `service` | `Quadrat` |
| `rehearses` | `true`: Quadrat writes nothing but its answer, so a rehearsal may call it |
| `ports` | `raster` (required), `tile`, `lon`, `lat`, `bbox`, `geometry` in; `result` out |
| `settings` | `operation` (required: `metadata`, `point`, `zonal`, `histogram`, `classify`), `resampling` (`nearest` or `bilinear`, default `nearest`; `point` only) and `encoding` (`terrarium` or `mapbox`; with a `tile` only) |

The settings arrive in `params`, not through ports. A node declared from the manifest carries every setting, empty
until someone sets it, so an empty `resampling` or `encoding` reads as not set. An empty `operation` is refused. The
allowed values are read from the code, so the manifest cannot offer one the service would refuse.

### Where it reads from

A request names a raster, but never decides where the service may look. Quadrat reads only from the sources it is
started with, and **with none configured it reads nothing**:

| Flag | Allows |
|---|---|
| `--rasterDirectory=<folder>` | Any file under that folder. Repeat the flag for more folders. `..` and links are resolved first, so a path cannot leave the folder by either |
| `--rasterUrl=<address>` | Any `http` or `https` address under that one: the same scheme and host, and a path inside it. Repeat for more |

A path or address outside them is refused as `source_not_allowed` before the raster library sees it. So are the raster
library's own virtual file systems (`/vsicurl/…`, `/vsizip/…`) and driver or scheme prefixes (`s3://`, `zip://`,
`NETCDF:"…"`), which are never allowed. The command line is not restricted: it reads what the person running it can.

#### Rasters at a web address

An allowed address is read with byte-range requests, so a Cloud Optimized GeoTIFF is fetched a block at a time and a
point or a small zone never downloads the file. In the tests, describing a 15 MB COG, reading a point from it and
reading a zone from it each fetched under a quarter of it. A GeoTIFF that is not tiled still works but is fetched in
much larger pieces.

- **Redirects are refused** (`redirected`). The raster library follows them and cannot be told not to, so before it
  reads, Quadrat asks the host for one byte itself and stops at a redirect. A host that begins redirecting between
  that check and the read is the one thing left to the host's own trustworthiness.
- **Only `.tif` and `.tiff`** are opened over the web. A `.vrt` is a file that points at other files, so it is not one.
- **A host that does not answer is given up on**, as `unreadable_raster`: 5 seconds to connect and 20 seconds per
  request, with no retries. Set `GDAL_HTTP_TIMEOUT`, `GDAL_HTTP_CONNECTTIMEOUT` or `GDAL_HTTP_MAX_RETRY` in the
  service's environment to change them. The environment's own value wins.
- Directory listings are not requested (`GDAL_DISABLE_READDIR_ON_OPEN`), so a read makes no requests beyond the file.

| | |
|---|---|
| `params.operation` | `metadata`, `point`, `zonal`, `histogram` or `classify` |
| `params.resampling` | `point` only: `nearest` (default) or `bilinear` |
| `inputs.raster` | Path to the raster, or to a terrain tile PNG when `inputs.tile` is given. Required |
| `inputs.tile`, `params.encoding` | A terrain tile: `{"z": n, "x": n, "y": n}` and `terrarium` or `mapbox`. See [Terrain tiles](#terrain-tiles) |
| `inputs.lon`, `inputs.lat` | `point`: the WGS84 coordinate |
| `inputs.bbox` or `inputs.geometry` | `zonal`, `histogram`, `classify`: `[west, south, east, north]` or a GeoJSON Polygon or MultiPolygon, not both |
| `inputs.classes` | `classify`: the class table, `[{"label": "trees", "values": [10, 11]}, {"label": "low", "max": 200}, ...]`. Required |
| `outputs.result` | What the command line prints |

The answer is `{"result": "success", "outputs": {"result": ...}, "error": null}`, the shape Phloem reads. A request
Quadrat will not answer comes back as `{"result": "fail", "outputs": {}, "error": "<code>: <reason>"}` with the codes
below, plus `missing_input`, `unknown_operation`, `ambiguous_zone`, `unstated_encoding`, `unknown_encoding`,
`encoding_without_tile`, `invalid_tile`, `not_a_terrain_tile`, `source_not_allowed`, `invalid_classes`, `too_many_values`, `redirected`, `invalid_ticket`, `ticket_not_found`, `ticket_unreadable`, `ticket_too_large`, `too_many_cells` and `read_failed`.

#### Rasters held in the asset store

`inputs.raster` may also be a store ticket, `sha256:` and 64 lowercase hexadecimal digits: the name the platform's
asset store gives bytes Tributary kept (`GET /api/assets/{ticket}`). Quadrat fetches the bytes with its own service
token, so authentication, caching and provider terms stay with Tributary, and a ticket needs no `--rasterDirectory` or
`--rasterUrl`. The bytes are written to a folder of their own, read, and removed whether the read answers or refuses.
That folder is allowed for that one request only, so another request cannot name a path inside it.

A ticket is refused, before any read, as `invalid_ticket` (not shaped like one, so the store is never asked),
`ticket_not_found` (the store answered 404), `ticket_unreadable` (any other refusal by the store) or
`ticket_too_large` (more than `--maxTicketBytes`, below). Terrain tiles work the same way: the ticket names the PNG.

### Limits

What one request may ask of the service is set when it starts, never by a request. A request over a limit is refused
before anything is read:

| Flag | Default | Limits |
|---|---|---|
| `--maxCells=<n>` | `4000000` | The cells of the raster a zonal read must load: the whole cells the zone's bounding box touches, clipped to the raster. A terrain tile image is counted by its size. Point and description reads load no window and are not counted |
| `--maxConcurrentReads=<n>` | `2` | Reads going at once. The rest wait their turn |
| `--maxTicketBytes=<n>` | `268435456` | The most one store ticket may hold. It is written to disk while it is read; the store's declared size or the bytes as they arrive stop a larger one, and nothing is left behind |

Over the cell limit is refused as `too_many_cells`, saying how many cells the zone needs and how many are allowed. A flag
that is not a positive whole number stops the service from starting.

**Reads run in worker processes**, as many as `--maxConcurrentReads`. The raster library holds Python's global lock for
most of a large read, so in a thread it stalled everything else in the service: during four 4-million-cell reads
`/health` took up to 4.6 seconds to answer, against 29 milliseconds with worker processes. A worker the raster library
brings down takes only itself with it, and the request is refused as `read_failed` and can be sent again.

The defaults rest on what a read was measured to take (a float32 raster on a Windows development machine; yours will
differ, so measure before raising them):

| Window | Time | Peak memory of the read |
|---|---|---|
| 1 million cells | 0.6 s | 130 MB |
| 4 million cells | 2.5 s | 490 MB |
| 16 million cells | 8.8 s | 1,350 MB |

That is about 85 bytes per cell, so the defaults keep one read near half a gigabyte and the two that may run at once near
a gigabyte (measured: 988 MB for four requests at the default, 1,831 MB with `--maxConcurrentReads=4`).

## Operations

| Command | Answers |
|---|---|
| `metadata` | CRS, size, resolution, bounds (own CRS and WGS84), bands, dtype, nodata |
| `point` | The value at a coordinate; `nearest` or `bilinear`; `null` on a nodata cell |
| `zonal` | `count`, `mean`, `min`, `max`, `median` over a polygon, weighted by how much of each cell the polygon covers; nodata cells left out and reported as `nodata_cells` |
| `histogram` | The cells, share and ground area of each distinct value over a polygon, for a raster of class codes. See [Land cover](#land-cover-and-measurements-histogram-and-classify) |
| `classify` | The same, grouped by a class table the request states, of exact values (land cover) or ranges (elevation, slope, rainfall); cells no class names are reported as `unclassified` |

### Land cover and measurements: `histogram` and `classify`

Both answer *how much of the zone is each kind of cell*. `histogram` is for a raster of class codes (a land-cover map);
`classify` also takes ranges, so it serves a measurement such as elevation, slope or rainfall.

- **`histogram`** counts the cells of each distinct value: `classes: [{"value", "cells", "fraction", "area_m2"}]`,
  with the cells that hold no data under `nodata`, and the zone's `covered_cells` and `area_m2`.
- **`classify`** groups cells into the classes the request states, in the order given: `classes: [{"label",
  "cells", "fraction", "area_m2"}]`. A class with no cells is still listed. A cell that no class names is never put
  in one by guess: it is reported under `unclassified`.

A class table is a list of classes, each one of two kinds, and a table may mix them:

```json
[
  {"label": "trees", "values": [10, 11]},
  {"label": "low",  "max": 200},
  {"label": "mid",  "min": 200, "max": 400},
  {"label": "high", "min": 400}
]
```

- **Exact values** (`values`) suit a raster of codes, where 10 is a name, not a size. A code means what the request
  says it means, so Quadrat holds no table for any land-cover product.
- **A range** (`min` and/or `max`) suits a measurement, where the numbers have size. It takes every value from `min`
  up to but **not including** `max`, so a value on the edge of two ranges belongs to the upper one and no value is
  counted twice. Leave a bound out for an open end. Values are compared as stored in the raster.
- Each label is used once, no value belongs to two classes, ranges may touch but not overlap, and `min` is below
  `max`. Anything else is refused as `invalid_classes`.
- `unclassified` of a table with exact values only lists the `values` that were left over. A table with a range
  reports the `min` and `max` of the left-over values instead (`null` when nothing is left over), because a
  measurement has no short list of values.

- **Cells** are weighted by how much of each the zone covers, as in `zonal`, so an edge cell counts for its part.
- **`fraction`** is the share of the zone's covered cells, including the nodata ones, so the fractions of the classes
  and `nodata` add up to 1.
- **`area_m2`** is that share of the zone's ground area on the WGS84 ellipsoid. It takes the cells across the zone to
  be of one size, which is close for a zone the size of a site and loses accuracy for a very large one on a
  web-mercator raster (see the limits of raster data below).
- `histogram` counts each distinct value, so a raster whose zone holds more than 1000 of them is a measurement and
  is refused as `too_many_values`: classify it by ranges instead. A table of exact values is refused the same way.
- The cell limit applies as it does to `zonal`. Ranges need each cell's value, which takes about 50 bytes a cell:
  roughly 200 MiB at the default 4,000,000-cell limit for each read in flight (`--maxConcurrentReads`).

## Terrain tiles

Elevation is often served as PNG map tiles whose red, green and blue bytes hold the height. A PNG has no position of
its own, so Quadrat is told which tile it is and how the bytes are read, and guesses neither:

| `encoding` | Elevation in metres |
|---|---|
| `terrarium` | `red * 256 + green + blue / 256 - 32768` |
| `mapbox` | `-10000 + (red * 65536 + green * 256 + blue) * 0.1` |

`tile` is the web-mercator `{z}/{x}/{y}` the image was fetched as, with `y` counted down from the north. Quadrat
places the decoded image on that tile and answers `metadata`, `point` and `zonal` as for any raster. An encoding that
is not stated or not one of these is refused (`unstated_encoding`, `unknown_encoding`), as is an `encoding` with no
`tile`.

A tile is small (a 256 pixel Terrarium tile at zoom 10 has cells of 153 web-mercator metres, about 100 m on the ground at this latitude), so the limits below
apply with force: on the Tatra tile the 256 pixel PNG reads a maximum 18 m below the 512 pixel GeoTIFF of the same
tile, and its mean agrees to within 0.5 m.

## Sample data

`samples/` is not in git. The files there are public Terrain Tiles from the AWS Open Data bucket
`elevation-tiles-prod` (z10, x568, y350, the Tatra Mountains), about 140 KB each:

```bash
curl -o samples/tatra-z10-568-350.tif https://s3.amazonaws.com/elevation-tiles-prod/geotiff/10/568/350.tif
curl -o samples/tatra-z10-568-350.terrarium.png https://s3.amazonaws.com/elevation-tiles-prod/terrarium/10/568/350.png
```

## Zones: a box or a polygon

A zone is a WGS84 Polygon or MultiPolygon, given one of two ways:

- **`--bbox WEST SOUTH EAST NORTH`**: a box is two corners, so four numbers, and Quadrat turns it into a closed
  rectangle. It must have `west < east` and `south < north`, longitudes within -180..180 and latitudes within
  -90..90; otherwise it is refused as `invalid_bbox`. It cannot cross the antimeridian: send two boxes.
- **`--geometry FILE`**: a GeoJSON file holding a Polygon, a MultiPolygon, a Feature, or a FeatureCollection (the
  first feature is used). It can have any number of vertices and holes; a hole is left out of the statistics. The ring
  must close, must not cross itself, and must stay within the same ranges, otherwise it is refused as
  `invalid_geometry`. A hole may not touch the outer ring along an edge.

A zone that does not overlap the raster is refused as `outside_raster`; one that overlaps partly is clipped to the
raster, and `covered_cells` says how much of it was read.

## What a reading means: limits of raster data

- **Peaks are read low and pits high.** A cell holds one value for a whole patch of ground, an average or a
  resampled figure. A summit narrower than a cell is pulled toward its surroundings. On the sample tile Giewont
  (1895 m) reads 1822 m by `bilinear`, and the zone around it has a `max` of 1904 m. This is the data's resolution, not
  a fault in the reader. Use `zonal` `max` for "the highest point inside a site", not a point read at a coordinate, and
  prefer `nearest` over `bilinear` when a figure must be one the data actually holds. Finer sources help; the sample
  tile has 76 m nominal cells, and a steeper slope makes the same positional error cost more height (50 m off
  position on a 40 degree slope is about 40 m of height).
- **Position matters on slopes.** A point a few cells away from the one meant can read tens of metres different.
- **Web mercator cells are not equal in ground size.** The sample tiles are in EPSG:3857, where a cell covers much
  less ground than its nominal size away from the equator (about half the linear size at 49 N). `count` is a
  number of cells, not ground area, and must not be multiplied into hectares. Area per class has to be worked out in an
  equal-area projection.
- **Nodata is never a value.** A point on a nodata cell has `value: null`, and nodata cells inside a zone are left
  out of the statistics and reported as `nodata_cells`.
- **Partial cells are weighted.** A cell the zone half covers counts as 0.5 in `count` and in the mean.
- Band 1 only for `zonal`; grids rotated against their axes are refused.
