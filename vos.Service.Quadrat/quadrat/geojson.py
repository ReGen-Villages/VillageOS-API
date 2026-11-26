import json

from .refusal import Refusal

FORMS = "the GeoJSON object, its text, or a GeoJson value {\"Json\": text}"


def geometry_of(value):
    """A geometry as the object, from any form the platform holds one in; None when none was given."""
    if value is None:
        return None
    given = value
    if isinstance(value, dict) and set(value) == {"Json"}:
        value = value["Json"]
    if isinstance(value, str):
        try:
            value = json.loads(value)
        except ValueError:
            raise Refusal("invalid_geometry", f"a geometry is {FORMS}; this text is not JSON") from None
    if not (isinstance(value, dict) and isinstance(value.get("type"), str)):
        raise Refusal("invalid_geometry", f"a geometry is {FORMS}; got {_described(given)}")
    return value


def _described(value):
    text = json.dumps(value) if isinstance(value, (dict, list)) else repr(value)
    return text if len(text) <= 80 else text[:77] + "..."
