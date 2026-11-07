from .refusal import Refusal


def bbox_zone(west, south, east, north):
    if not (-180 <= west < east <= 180 and -90 <= south < north <= 90):
        raise Refusal(
            "invalid_bbox",
            f"a box is west < east within -180..180 and south < north within -90..90, "
            f"not ({west}, {south}, {east}, {north}); a box crossing the antimeridian is two boxes",
        )
    return {
        "type": "Polygon",
        "coordinates": [[[west, south], [east, south], [east, north], [west, north], [west, south]]],
    }
