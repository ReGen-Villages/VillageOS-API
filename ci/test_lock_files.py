"""A project that declares dependencies pins them, or a build resolves whatever is newest that day.

`npm ci` refuses to run without a lock file, so a project whose dependencies CI installs is already
guarded. `tools/docs-pdf` is not one: it is run by hand when a document needs rendering, so its
missing lock file went unnoticed until somebody looked (#6638). This is the check that looks.

A project declaring no dependencies has nothing to pin and needs no lock file.
"""

import json
import os
import subprocess
import unittest

REPOSITORY_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def unpinned(manifests, tracked):
    """The lock files missing from `tracked`, given {path: parsed package.json}."""
    missing = []
    for path, manifest in sorted(manifests.items()):
        if not manifest.get("dependencies") and not manifest.get("devDependencies"):
            continue
        directory = os.path.dirname(path)
        missing.append(f"{directory}/package-lock.json" if directory else "package-lock.json")
    return [lock for lock in missing if lock not in tracked]


def tracked_files():
    listing = subprocess.run(
        ["git", "ls-files"], cwd=REPOSITORY_ROOT, capture_output=True, text=True, check=True
    )
    return set(listing.stdout.split("\n"))


def tracked_manifests(tracked):
    manifests = {}
    for path in tracked:
        if os.path.basename(path) != "package.json":
            continue
        with open(os.path.join(REPOSITORY_ROOT, path), encoding="utf-8") as declaration:
            manifests[path] = json.load(declaration)
    return manifests


class UnpinnedTests(unittest.TestCase):
    def test_a_project_declaring_dependencies_needs_a_lock_file(self):
        manifests = {"tools/renderer/package.json": {"dependencies": {"marked": "^18.0.0"}}}
        self.assertEqual(["tools/renderer/package-lock.json"], unpinned(manifests, set()))

    def test_development_dependencies_count_too(self):
        manifests = {"tools/renderer/package.json": {"devDependencies": {"vitest": "^4.0.0"}}}
        self.assertEqual(["tools/renderer/package-lock.json"], unpinned(manifests, set()))

    def test_a_project_declaring_none_needs_nothing(self):
        manifests = {"tools/converter/package.json": {"scripts": {"test": "node --test"}}}
        self.assertEqual([], unpinned(manifests, set()))

    def test_a_pinned_project_is_not_reported(self):
        manifests = {"tools/renderer/package.json": {"dependencies": {"marked": "^18.0.0"}}}
        self.assertEqual([], unpinned(manifests, {"tools/renderer/package-lock.json"}))


class ThisRepositoryTests(unittest.TestCase):
    def test_every_project_that_declares_dependencies_pins_them(self):
        tracked = tracked_files()
        self.assertEqual([], unpinned(tracked_manifests(tracked), tracked))


if __name__ == "__main__":
    unittest.main()
