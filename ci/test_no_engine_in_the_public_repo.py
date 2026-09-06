import os
import tempfile
import unittest

from no_engine_in_the_public_repo import imports_an_engine_namespace, problems

REPOSITORY_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


class TheRepositoryItGuards(unittest.TestCase):
    def test_holds_no_engine_code(self):
        self.assertEqual([], problems(REPOSITORY_ROOT))


class AnImportItRefuses(unittest.TestCase):
    def test_the_engine_itself(self):
        self.assertEqual('vos.Mycelium', imports_an_engine_namespace('using vos.Mycelium;'))

    def test_a_namespace_under_the_engine(self):
        self.assertEqual('vos.Mycelium.Services',
                         imports_an_engine_namespace('using vos.Mycelium.Services;'))

    def test_each_of_the_others(self):
        for namespace in ('vos.Core', 'vos.Application', 'vos.Infrastructure', 'vos.Persistence'):
            self.assertEqual(namespace, imports_an_engine_namespace('using %s;' % namespace))

    def test_one_written_as_a_global_using(self):
        self.assertEqual('vos.Core', imports_an_engine_namespace('global using vos.Core;'))

    def test_one_written_as_a_static_using(self):
        self.assertEqual('vos.Core.Names', imports_an_engine_namespace('using static vos.Core.Names;'))

    # The form the check this replaced could not see at all: an alias puts a name between `using` and
    # the namespace, so a pattern anchored on `using` followed by the namespace never matched it.
    def test_one_written_as_an_alias(self):
        self.assertEqual('vos.Core.VosThing',
                         imports_an_engine_namespace('using Thing = vos.Core.VosThing;'))

    def test_one_indented(self):
        self.assertEqual('vos.Core', imports_an_engine_namespace('    using vos.Core;'))


class AnImportItAllows(unittest.TestCase):
    # The host a consumer is meant to compile against. Refusing it would refuse the supported way of
    # testing against the platform, which is what this repository now does.
    def test_the_testing_host(self):
        self.assertIsNone(imports_an_engine_namespace('using vos.Mycelium.Testing;'))

    def test_something_under_the_testing_host(self):
        self.assertIsNone(imports_an_engine_namespace('using vos.Mycelium.Testing.Hosting;'))

    def test_the_testing_host_as_a_global_using(self):
        self.assertIsNone(imports_an_engine_namespace('global using vos.Mycelium.Testing;'))

    # `vos.Core` is a namespace; `vos.CoreThing` is a different word that begins with it.
    def test_a_namespace_that_merely_begins_with_an_engine_one(self):
        self.assertIsNone(imports_an_engine_namespace('using vos.CoreThings;'))

    def test_a_service_namespace(self):
        self.assertIsNone(imports_an_engine_namespace('using vos.Service.Phloem.Services;'))

    def test_a_line_that_is_not_an_import(self):
        self.assertIsNone(imports_an_engine_namespace('// using vos.Core; would be refused'))


class AFileHoldingBoth(unittest.TestCase):
    """A check reading whole files is satisfied by the allowed import and never sees the refused one
    beside it. This is the case that decided the shape of the guard."""

    def test_is_reported_for_the_refused_import_only(self):
        with tempfile.TemporaryDirectory() as root:
            with open(os.path.join(root, 'Both.cs'), 'w', encoding='utf-8') as handle:
                handle.write('using vos.Mycelium.Testing;\nusing vos.Mycelium;\n')

            found = problems(root)

        self.assertEqual(1, len(found), found)
        self.assertIn('Both.cs:2', found[0])
        self.assertIn('vos.Mycelium', found[0])


class WhatElseItRefuses(unittest.TestCase):
    def test_an_engine_project_directory(self):
        with tempfile.TemporaryDirectory() as root:
            os.mkdir(os.path.join(root, 'vos.Core'))
            found = problems(root)

        self.assertEqual(['Engine project directory present: vos.Core'], found)

    def test_a_project_file_referencing_an_engine_project(self):
        with tempfile.TemporaryDirectory() as root:
            with open(os.path.join(root, 'A.csproj'), 'w', encoding='utf-8') as handle:
                handle.write('<ProjectReference Include="..\\vos.Application\\vos.Application.csproj" />\n')

            found = problems(root)

        self.assertEqual(1, len(found), found)
        self.assertIn('vos.Application.csproj', found[0])


class WhereItLooks(unittest.TestCase):
    def test_it_skips_a_hidden_directory(self):
        """A git worktree lives under one and holds a second checkout of every file below."""
        with tempfile.TemporaryDirectory() as root:
            hidden = os.path.join(root, '.worktrees', 'branch')
            os.makedirs(hidden)
            with open(os.path.join(hidden, 'Elsewhere.cs'), 'w', encoding='utf-8') as handle:
                handle.write('using vos.Core;\n')

            self.assertEqual([], problems(root))

    def test_it_skips_build_output(self):
        with tempfile.TemporaryDirectory() as root:
            os.makedirs(os.path.join(root, 'obj'))
            with open(os.path.join(root, 'obj', 'Generated.cs'), 'w', encoding='utf-8') as handle:
                handle.write('using vos.Core;\n')

            self.assertEqual([], problems(root))


if __name__ == '__main__':
    unittest.main()
