"""No type in the repository is named like a part of the shared client library's namespace.

C# looks a name up in each enclosing namespace, innermost first, before it reads the `using` lines at the
top of a file. Code in `namespace vos.Hypha` that names a class `MyceliumClient` meets a namespace
`vos.MyceliumClient` at the `vos` level and never reaches the class, whichever project declares it, so every
such file fails to compile in a project that references the library.
"""

import os
import re
import unittest

REPOSITORY_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LIBRARY = os.path.join(REPOSITORY_ROOT, 'vos.MyceliumClient')
UNSEARCHED = ('bin', 'obj', 'node_modules')
SHARED_ROOT = 'vos'

NAMESPACE = re.compile(r'^\s*namespace\s+([\w.]+)', re.MULTILINE)
TYPE = re.compile(
    r'^\s*(?:(?:public|internal|protected|private|sealed|static|abstract|partial|readonly|file)\s+)*'
    r'(?:class|interface|enum|struct|record(?:\s+(?:class|struct))?)\s+(\w+)',
    re.MULTILINE)


def clashes(library, repository):
    """Each part of the library's namespaces below `vos` that a type in the repository is named, given the
    text of the library's source files and of every source file in the repository."""
    parts = {part
             for text in library
             for namespace in NAMESPACE.findall(text)
             for part in namespace.split('.')
             if part != SHARED_ROOT}
    types = {name for text in repository for name in TYPE.findall(text)}
    return sorted(parts & types)


def sources(root):
    for directory, children, files in os.walk(root):
        # The staged engine and every git worktree live under hidden directories.
        children[:] = [child for child in children
                       if not child.startswith('.') and child not in UNSEARCHED]
        for name in files:
            if name.endswith('.cs'):
                with open(os.path.join(directory, name), encoding='utf-8', errors='replace') as source:
                    yield source.read()


class TheSharedClientLibrary(unittest.TestCase):
    def test_no_type_in_the_repository_is_named_like_a_part_of_its_namespace(self):
        self.assertEqual([], clashes(list(sources(LIBRARY)), list(sources(REPOSITORY_ROOT))))


class Clashes(unittest.TestCase):
    def test_the_librarys_own_class_named_like_its_namespace(self):
        library = ['namespace vos.MyceliumClient;\n\npublic partial class MyceliumClient\n{\n}\n']
        self.assertEqual(['MyceliumClient'], clashes(library, library))

    def test_another_projects_class_named_like_it(self):
        library = ['namespace vos.MyceliumClient;\n\npublic static class Timestamps\n{\n}\n']
        service = ['namespace vos.Service.Delta.Services;\n\npublic class MyceliumClient : MyceliumClientBase\n{\n}\n']
        self.assertEqual(['MyceliumClient'], clashes(library, library + service))

    def test_a_record_named_like_a_deeper_part(self):
        library = ['namespace vos.PlatformClient.Reads;\n']
        self.assertEqual(['Reads'], clashes(library, ['public sealed record Reads(string Name);\n']))

    def test_a_record_struct(self):
        library = ['namespace vos.PlatformClient;\n']
        self.assertEqual(['PlatformClient'],
                         clashes(library, ['internal readonly record struct PlatformClient;\n']))

    def test_a_namespace_in_braces(self):
        library = ['namespace vos.PlatformClient\n{\n}\n']
        self.assertEqual(['PlatformClient'], clashes(library, ['    public interface PlatformClient\n']))

    def test_a_type_named_like_the_shared_root(self):
        library = ['namespace vos.PlatformClient;\n']
        self.assertEqual([], clashes(library, ['public class vos\n{\n}\n']))

    def test_types_named_unlike_any_part(self):
        library = ['namespace vos.PlatformClient;\n\npublic partial class MyceliumClient\n{\n}\n']
        self.assertEqual([], clashes(library, library))

    def test_a_name_in_a_comment(self):
        library = ['namespace vos.PlatformClient;\n']
        self.assertEqual([], clashes(library, ['// class PlatformClient was renamed\n']))


if __name__ == '__main__':
    unittest.main()
