"""Which statistics of an answer are recorded on a Thing, and as which properties."""
import uuid

from .refusal import Refusal


def requested(inputs):
    """(thing id, {property: statistic path}) when the request asks for its answer to be recorded, else None."""
    thing, observations = inputs.get("thingId"), inputs.get("observations")
    if thing is None and observations is None:
        return None
    if thing is None or observations is None:
        raise Refusal("missing_input", "'thingId' and 'observations' are given together: the Thing to record on, and the property each statistic fills")
    if not _is_guid(thing):
        raise Refusal("invalid_thing", f"'thingId' is the Thing's GUID; got {thing!r}")
    if not isinstance(observations, dict) or not observations or not all(_names(p) and _names(s) for p, s in observations.items()):
        raise Refusal("invalid_observations", "'observations' maps each property to record to the statistic of the answer that fills it, e.g. {\"elevation_mean_m\": \"mean\"}")
    return thing, observations


def recorded(answer, observations):
    """(the samples to record, the properties left out because the answer holds none) for a Thing's observations."""
    samples, skipped = [], []
    for prop, path in observations.items():
        found = _find(answer, path)
        if found is _ABSENT:
            raise Refusal("unknown_statistic", f"the answer has no {path!r} to record as {prop!r}")
        if found is None:
            skipped.append(prop)
        elif isinstance(found, (dict, list)):
            raise Refusal("not_a_reading", f"{path!r} is not one value, so it cannot be recorded as {prop!r}")
        else:
            samples.append({"property": prop, "value": found})
    return samples, skipped


_ABSENT = object()


def _find(document, path):
    for part in path.split("."):
        if isinstance(document, dict) and part in document:
            document = document[part]
        elif isinstance(document, list) and part.isdigit() and int(part) < len(document):
            document = document[int(part)]
        else:
            return _ABSENT
    return document


def _names(value):
    return isinstance(value, str) and bool(value.strip())


def _is_guid(value):
    try:
        uuid.UUID(value)
    except (ValueError, AttributeError, TypeError):
        return False
    return isinstance(value, str)
