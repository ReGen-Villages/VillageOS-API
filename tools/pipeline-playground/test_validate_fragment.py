import unittest

import generate as G
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


class ThePaletteListsServicesAStepCanWire(unittest.TestCase):
    def test_a_connection_whose_service_declares_no_port_is_left_out(self):
        kit = G.build()
        [connection_kind] = [tid for tid, role in kit.role_of.items() if role == G.role_id("connection")]
        [service_kind] = [tid for tid, role in kit.role_of.items() if role == G.role_id("service")]
        [is_] = [tid for tid, t in kit.things.items() if t["Name"] == "is"]
        [has] = [tid for tid, t in kit.things.items() if t["Name"] == "has"]
        service = kit.thing("portless-service", "registersEndpoint service")
        connection = kit.thing("portless-connection", "registersEndpoint", {"Subdomain": G.typed("delta", "vos.String")})
        kit.rel(service, is_, service_kind)
        kit.rel(connection, is_, connection_kind)
        kit.rel(connection, has, service)

        listed = [c["subdomain"] for c in V.Model(G.as_fragment(kit)).connections()]

        self.assertNotIn("delta", listed)
        self.assertIn("echo", listed)


if __name__ == "__main__":
    unittest.main()
