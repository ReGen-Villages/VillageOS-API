# Quadrat — raster reader (local prototype)

Reads a raster at a point or over a zone and prints JSON. This is the local prototype of the Quadrat service
(Feature #7603, spike #7610): a plain Python package and command line, with no HTTP, no Mycelium registration
and no auth. The `quadrat/` package is what the service will wrap.

Coordinates and zones are always WGS84 (lon/lat); Quadrat reprojects to the raster's own CRS.

## Run

```bash
python -m venv .venv
.venv/Scripts/python -m pip install -r requirements.txt     # Windows; .venv/bin/python elsewhere
.venv/Scripts/python -m pytest

.venv/Scripts/python -m quadrat metadata samples/tatra-z10-568-350.tif
.venv/Scripts/python -m quadrat point samples/tatra-z10-568-350.tif --lon 19.9814 --lat 49.2319 --resampling bilinear
.venv/Scripts/python -m quadrat zonal samples/tatra-z10-568-350.tif --bbox 19.92 49.24 19.95 49.26
.venv/Scripts/python -m quadrat zonal samples/tatra-z10-568-350.tif --geometry zone.geojson
```

A request Quadrat will not answer prints `{"refused": "<code>", "reason": "..."}` and exits 2. Codes:
`outside_raster`, `unreadable_raster`, `no_crs`, `unknown_resampling`, `unsupported_geometry`, `invalid_geometry`,
`invalid_bbox`, `rotated_raster`.

## Operations

| Command | Answers |
|---|---|
| `metadata` | CRS, size, resolution, bounds (own CRS and WGS84), bands, dtype, nodata |
| `point` | The value at a coordinate; `nearest` or `bilinear`; `null` on a nodata cell |
| `zonal` | `count`, `mean`, `min`, `max`, `median` over a polygon, weighted by how much of each cell the polygon covers; nodata cells left out and reported as `nodata_cells` |

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
