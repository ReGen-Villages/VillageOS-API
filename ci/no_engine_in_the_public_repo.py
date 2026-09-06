"""Whether proprietary engine code has reappeared in the public repository.

The engine lives only in the private platform repository. This repository holds the services, the
clients and the tools that speak to it, and must be publishable as it stands.

One piece of the engine is meant to be compiled against: `vos.Mycelium.Testing`, the host that runs
the engine inside a consumer's own test process. It ships in a release for exactly that, and the
platform build proves on every run that a project outside its repository can boot it. A check that
refused it would refuse the supported way of testing against the platform.

The import rule therefore judges each import, not each file. A file holding
`using vos.Mycelium.Testing;` beside `using vos.Mycelium;` is a file with one violation in it, and a
check reading whole files sees either none or one and cannot say which.

Stock Python: the build agents carry python3 and nothing else, and a guard that needed installing
would be one more thing able to fail.
"""

import os
import re
import sys

ENGINE_NAMESPACES = ('vos.Core', 'vos.Application', 'vos.Infrastructure', 'vos.Mycelium',
                     'vos.Persistence')

# The one namespace under an engine namespace a consumer may compile against.
IMPORTABLE = 'vos.Mycelium.Testing'

UNSEARCHED = ('bin', 'obj', 'node_modules')

_ALTERNATIVES = '|'.join(namespace.replace('.', r'\.') for namespace in ENGINE_NAMESPACES)

# `using X;`, `global using X;`, `using static X.Y;` and `using Alias = X.Y;`. The trailing class
# refuses a longer namespace that merely starts with an engine one — `vos.CoreThing` is not
# `vos.Core` — while still matching every namespace genuinely under it.
IMPORT = re.compile(
    r'^\s*(?:global\s+)?using\s+'
    r'(?:static\s+|[A-Za-z_]\w*\s*=\s*)?'
    r'(?P<namespace>(?:%s)(?:\.\w+)*)\s*;' % _ALTERNATIVES)


def imports_an_engine_namespace(line):
    """The namespace this line imports when the import is one this repository may not make."""
    match = IMPORT.match(line)
    if match is None:
        return None

    namespace = match.group('namespace')
    if namespace == IMPORTABLE or namespace.startswith(IMPORTABLE + '.'):
        return None
    return namespace


def source_files(root, suffix):
    for directory, children, files in os.walk(root):
        # A git worktree lives under a hidden directory and holds a second checkout of everything
        # below; the staged engine lives under one too, and is not source this repository keeps.
        children[:] = [child for child in children
                       if not child.startswith('.') and child not in UNSEARCHED]
        for name in files:
            if name.endswith(suffix):
                yield os.path.join(directory, name)


def _lines(path):
    with open(path, encoding='utf-8', errors='replace') as handle:
        return handle.read().splitlines()


def problems(root):
    """Every violation, each naming where it is. Empty is a repository that may be published."""
    found = []

    for namespace in ENGINE_NAMESPACES:
        if os.path.isdir(os.path.join(root, namespace)):
            found.append('Engine project directory present: %s' % namespace)

    for path in source_files(root, '.csproj'):
        for number, line in enumerate(_lines(path), start=1):
            for namespace in ENGINE_NAMESPACES:
                if namespace + '.csproj' in line:
                    found.append('%s:%d references the engine project %s.csproj'
                                 % (os.path.relpath(path, root), number, namespace))

    for path in source_files(root, '.cs'):
        for number, line in enumerate(_lines(path), start=1):
            namespace = imports_an_engine_namespace(line)
            if namespace is not None:
                found.append('%s:%d imports the engine namespace %s'
                             % (os.path.relpath(path, root), number, namespace))

    return found


def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    found = problems(root)
    for problem in found:
        print('##[error]%s' % problem)
    if found:
        print('##[error]Proprietary engine code must never exist in the public repository. '
              'See the engine-exposure remediation.')
        return 1
    print('Guard passed: no proprietary engine footprint.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
