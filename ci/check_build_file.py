"""What the build runs on, and what it publishes, read as a passing build when they are wrong.

A widened trigger, a configuration that no longer follows the branch, and a publishing step gated
back to develop all produce more green builds rather than a failure, so nothing in a build result
would look wrong. This reads the build file and says what has drifted.

Stock Python: the build agents carry python3 and nothing else, and a guard that needed installing
would be one more thing able to fail.
"""

MAIN = 'refs/heads/main'
DEVELOP = 'refs/heads/develop'

# main is the release branch, so main is what leaves the repository. The wiki mirror publishes
# whatever the docs publish left on the project wiki, so the pair only agree while both run on the
# same branch.
PUBLISHING_STEPS = ('Publish Docs to Wiki', 'Mirror to GitHub', 'Mirror Wiki to GitHub')

# A build against main compiles Release, which is what main packs and pushes; everything else
# compiles Debug, which is what a developer runs. Both branch variables, because a pull request into
# main has to compile Release too — a merge is too late to learn that it does not.
CONFIGURATION_MUST_READ = (
    MAIN, 'Release', 'Debug', 'Build.SourceBranch', 'System.PullRequest.TargetBranch',
)


def indented_block(build_file_text, opening):
    """The lines indented under the first line beginning with `opening`."""
    lines = build_file_text.splitlines()
    for index, line in enumerate(lines):
        if line.startswith(opening):
            block = []
            for following in lines[index + 1:]:
                if following and not following[0].isspace():
                    break
                block.append(following)
            return block
    return []


def push_trigger_branches(build_file_text):
    return [
        line.strip()[2:].strip().strip("'\"")
        for line in indented_block(build_file_text, 'trigger:')
        if line.strip().startswith('- ')
    ]


def build_configuration_value(build_file_text):
    block = indented_block(build_file_text, 'variables:')
    for index, line in enumerate(block):
        if 'name: buildConfiguration' in line:
            for following in block[index + 1:]:
                if 'value:' in following:
                    return following
    return ''


def step_condition(build_file_text, display_name):
    """The condition line of a named step, '' when it has none, None when there is no such step."""
    lines = build_file_text.splitlines()
    for index, line in enumerate(lines):
        if "displayName: '%s'" % display_name in line:
            for following in lines[index + 1:]:
                if following.startswith('- '):
                    break
                if following.strip().startswith('condition:'):
                    return following
            return ''
    return None


def problems(build_file_text):
    found = []

    declarations = [line for line in build_file_text.splitlines() if line.startswith('trigger:')]
    if len(declarations) != 1:
        found.append('Expected one push trigger in the build file, found %d' % len(declarations))
    else:
        branches = push_trigger_branches(build_file_text)
        if branches != ['develop', 'main']:
            found.append('A push must build develop and main only; the trigger names %s' % branches)

    configuration = build_configuration_value(build_file_text)
    if not configuration:
        found.append('buildConfiguration is not declared by name, so no branch chooses it')
    else:
        missing = [name for name in CONFIGURATION_MUST_READ if name not in configuration]
        if missing:
            found.append(
                'A build against main must compile Release and every other build Debug, and '
                'buildConfiguration never mentions %s' % ', '.join(missing))

    for step in PUBLISHING_STEPS:
        condition = step_condition(build_file_text, step)
        if condition is None:
            found.append('The build has no step named "%s", so nothing publishes it' % step)
        elif not condition:
            found.append('"%s" has no condition, so it publishes from every branch' % step)
        elif MAIN not in condition or DEVELOP in condition:
            found.append(
                '"%s" must run on main only; its condition reads %s' % (step, condition.strip()))

    return found
