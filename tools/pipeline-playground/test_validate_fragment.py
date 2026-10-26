import unittest

import validate_fragment as V


def seed(predicate_names):
    things = [{"Id": "subject", "Name": "Harvest pipeline"}, {"Id": "target", "Name": "Rain gauge node"}]
    relationships = []
    for index, name in enumerate(predicate_names):
        things.append({"Id": f"predicate-{index}", "Name": name})
        relationships.append({"Subject": "subject", "Predicate": f"predicate-{index}", "Target": "target"})
    return {"Things": things, "Relationships": relationships}


class PredicateNamesAreMatchedExactly(unittest.TestCase):
    def test_a_predicate_named_exactly_is_followed(self):
        model = V.Model(seed(["has", "binds"]))

        self.assertEqual([thing["Id"] for thing in model.outgoing("subject", "has")], ["target"])
        self.assertEqual(model.bound.get("subject"), ["target"])

    def test_a_predicate_whose_name_differs_only_in_case_is_another_predicate(self):
        model = V.Model(seed(["Has", "Binds"]))

        self.assertEqual(model.outgoing("subject", "has"), [])
        self.assertIsNone(model.bound.get("subject"))


if __name__ == "__main__":
    unittest.main()
