"""A README that documents how to install and test a service must document commands that work.

The Python echo service shipped a bare `pip install` and a bare `pytest`. Neither runs on a
developer machine: macOS has no `python` command, and the `python3` on PATH refuses to install
packages into itself. The install was the worse half — where it did succeed it put the dependencies
on the system interpreter, and `python3 -m unittest discover`, the command the stdlib suites in this
repository use, then reports `OK` on that directory having collected nothing. A reader following the
README turned a loud failure into a silent pass (#6692). This is the check that reads those READMEs.

A directory with no requirements.txt has no environment to get wrong, so nothing to check.
"""

import os
import subprocess
import unittest

REPOSITORY_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

ENVIRONMENT_PREFIX = '.venv/bin/'
BUILDS_THE_ENVIRONMENT = 'python3 -m venv'
PYTHON_PROGRAMS = ('python', 'python3', 'pip', 'pip3', 'pytest')


def command_lines(readme_text):
    """The commands inside the bash fences of `readme_text`, line continuations joined."""
    commands, inside_bash, continued = [], False, ''
    for line in readme_text.splitlines():
        if line.startswith('```'):
            inside_bash = line.startswith('```bash')
            continue
        if not inside_bash:
            continue
        statement = line.strip()
        if not statement or statement.startswith('#'):
            continue
        if statement.endswith('\\'):
            continued += statement[:-1]
            continue
        commands.append((continued + statement).strip())
        continued = ''
    return commands


def program_run_by(command):
    """The program a command runs, reading past any environment assignments in front of it."""
    for word in command.split():
        if '=' in word and not word.startswith(('.', '/')):
            continue
        return word
    return ''


def unguarded_commands(readme_text):
    """The documented commands that reach for an interpreter outside the virtual environment."""
    offending = []
    for command in command_lines(readme_text):
        if command.startswith(BUILDS_THE_ENVIRONMENT):
            continue
        program = program_run_by(command)
        if program.startswith(ENVIRONMENT_PREFIX):
            continue
        if program in PYTHON_PROGRAMS:
            offending.append(command)
    return offending


def readmes_beside_requirements():
    """The README of every tracked directory that declares Python dependencies, by path."""
    listing = subprocess.run(
        ['git', 'ls-files'], cwd=REPOSITORY_ROOT, capture_output=True, text=True, check=True
    )
    tracked = set(listing.stdout.split('\n'))
    readmes = {}
    for path in sorted(tracked):
        if os.path.basename(path) != 'requirements.txt':
            continue
        readme = os.path.join(os.path.dirname(path), 'README.md')
        if readme not in tracked:
            continue
        with open(os.path.join(REPOSITORY_ROOT, readme), encoding='utf-8') as documentation:
            readmes[readme] = documentation.read()
    return readmes


class UnguardedCommandTests(unittest.TestCase):
    def test_every_service_readme_this_repository_ships_uses_its_virtual_environment(self):
        for path, readme_text in readmes_beside_requirements().items():
            with self.subTest(readme=path):
                self.assertEqual([], unguarded_commands(readme_text))

    def test_a_directory_declaring_dependencies_has_a_readme_to_check(self):
        self.assertNotEqual({}, readmes_beside_requirements())

    def test_a_bare_install_is_reported(self):
        readme = '```bash\npip install -r requirements.txt\n```\n'
        self.assertEqual(['pip install -r requirements.txt'], unguarded_commands(readme))

    def test_an_install_through_the_system_interpreter_is_reported(self):
        readme = '```bash\npython3 -m pip install -r requirements.txt\n```\n'
        self.assertEqual(['python3 -m pip install -r requirements.txt'], unguarded_commands(readme))

    def test_a_bare_test_runner_is_reported(self):
        self.assertEqual(['pytest -v'], unguarded_commands('```bash\npytest -v\n```\n'))

    def test_a_command_macos_cannot_run_at_all_is_reported(self):
        readme = '```bash\npython app.py --port=5103\n```\n'
        self.assertEqual(['python app.py --port=5103'], unguarded_commands(readme))

    def test_building_the_environment_is_not_reported(self):
        self.assertEqual([], unguarded_commands('```bash\npython3 -m venv .venv\n```\n'))

    def test_commands_that_go_through_the_environment_are_not_reported(self):
        readme = ('```bash\npython3 -m venv .venv\n.venv/bin/python -m pip install -r '
                  'requirements.txt\n.venv/bin/python -m pytest\n```\n')
        self.assertEqual([], unguarded_commands(readme))

    def test_environment_assignments_in_front_of_a_command_are_read_through(self):
        readme = '```bash\nToken=<jwt> SigningKey=<key> .venv/bin/python app.py --port=5103\n```\n'
        self.assertEqual([], unguarded_commands(readme))

    def test_an_assignment_in_front_of_a_bare_command_does_not_hide_it(self):
        readme = '```bash\nToken=<jwt> python app.py --port=5103\n```\n'
        self.assertEqual(['Token=<jwt> python app.py --port=5103'], unguarded_commands(readme))

    def test_a_continued_line_is_judged_whole(self):
        readme = '```bash\nToken=<jwt> \\\n  python app.py --port=5103\n```\n'
        self.assertEqual(['Token=<jwt> python app.py --port=5103'], unguarded_commands(readme))

    def test_a_commented_line_is_not_a_command(self):
        self.assertEqual([], unguarded_commands('```bash\n# pip install -r requirements.txt\n```\n'))

    def test_prose_outside_a_fence_is_not_a_command(self):
        self.assertEqual([], unguarded_commands('Do not run `pip install -r requirements.txt`.\n'))

    def test_a_fence_in_another_language_is_not_read(self):
        readme = '```python\npip install -r requirements.txt\n```\n'
        self.assertEqual([], unguarded_commands(readme))

    def test_a_program_this_check_has_no_opinion_on_is_not_reported(self):
        self.assertEqual([], unguarded_commands('```bash\ndocker build -t echo .\n```\n'))


if __name__ == '__main__':
    unittest.main()
