import argparse
import json

from . import Refusal, bbox_zone, describe, sample_point, zonal_stats


def build_parser():
    parser = argparse.ArgumentParser(prog="quadrat", description="Read a raster at a point or over a zone; prints JSON.")
    commands = parser.add_subparsers(dest="command", required=True)

    commands.add_parser("metadata", help="shape, CRS, extent, bands").add_argument("raster")

    point = commands.add_parser("point", help="the value at a WGS84 coordinate")
    point.add_argument("raster")
    point.add_argument("--lon", type=float, required=True)
    point.add_argument("--lat", type=float, required=True)
    point.add_argument("--resampling", default="nearest", choices=("nearest", "bilinear"))

    zonal = commands.add_parser("zonal", help="statistics over a WGS84 polygon")
    zonal.add_argument("raster")
    zone = zonal.add_mutually_exclusive_group(required=True)
    zone.add_argument("--geometry", help="a GeoJSON file holding a Polygon, a Feature or a FeatureCollection")
    zone.add_argument("--bbox", nargs=4, type=float, metavar=("WEST", "SOUTH", "EAST", "NORTH"))
    return parser


def read_geometry(path):
    with open(path, encoding="utf-8") as file:
        document = json.load(file)
    if document.get("type") == "FeatureCollection":
        document = document["features"][0]
    return document["geometry"] if document.get("type") == "Feature" else document


def run(arguments):
    if arguments.command == "metadata":
        return describe(arguments.raster)
    if arguments.command == "point":
        return sample_point(arguments.raster, arguments.lon, arguments.lat, arguments.resampling)
    if arguments.bbox:
        zone = bbox_zone(*arguments.bbox)
    else:
        zone = read_geometry(arguments.geometry)
    return zonal_stats(arguments.raster, zone)


def main(argv=None):
    arguments = build_parser().parse_args(argv)
    try:
        print(json.dumps(run(arguments), indent=2))
    except Refusal as refusal:
        print(json.dumps({"refused": refusal.code, "reason": refusal.reason}, indent=2))
        return 2
    return 0
