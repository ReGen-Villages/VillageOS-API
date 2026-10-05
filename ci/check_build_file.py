"""What the build runs on, and what it publishes, read as a passing build when they are wrong.

A widened trigger, a configuration that no longer follows the branch, and a publishing step gated
to main all produce more green builds rather than a failure, so nothing in a build result would
look wrong. A pull request trigger that names no branch produces no build at all, which the GitHub
branch rules then read as a pull request that cannot merge. A job with no time limit shows nothing in
a green build either, and holds a shared agent for an hour on a red one. This reads the build file
and says what has drifted.

Stock Python: the build agents carry python3 and nothing else, and a guard that needed installing
would be one more thing able to fail.
"""

MAIN = 'refs/heads/main'
DEVELOP = 'refs/heads/develop'

# The wiki describes develop, the branch that moves. The wiki mirror publishes whatever the docs
# publish left on the project wiki, so the pair only agree while both run on the same branch — which
# is why a condition naming main is refused whether it moved the step there or widened it to both.
PUBLISHING_STEPS = ('Publish Docs to Wiki', 'Mirror Wiki to GitHub')

# A build against main compiles Release, which is what a release is built in; everything else
# compiles Debug, which is what a developer runs. A pull request into main has to compile Release
# too — a merge is too late to learn that it does not.
CONFIGURATION_MUST_READ = (MAIN, 'Release', 'Debug', 'Build.SourceBranch')

# A repository on GitHub gives a pull request's target as the bare branch name, where Azure Repos gave
# the full ref; compared against the full ref, a pull request into main compiles Debug and passes.
PULL_REQUEST_INTO_MAIN = "eq(variables['System.PullRequest.TargetBranch'], 'main')"

# A build that pushes to the repository it builds, as the old copy to GitHub did with a generated
# commit on top, changes the history every clone has to follow.
OWN_REPOSITORY = 'github.com/ReGen-Villages/VillageOS-API.git'

# A release is handed over as an archive of the running platform, not as libraries on a package feed:
# a feed needs a credential scoped for packaging that this organization does not issue, and a
# consumer wants something to run rather than something to compile against. The steps that packed and
# pushed outlived that decision, gated to a branch that had never built, so no build result ever
# showed the contradiction. Read as the absence of packaging anywhere, so a step added later is
# decided on rather than inherited.
PACKAGING_MARKERS = ('dotnet pack', 'publishVstsFeed')


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


def trigger_branches(build_file_text, opening):
    return [
        line.strip()[2:].strip().strip("'\"")
        for line in indented_block(build_file_text, opening)
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


def indentation(line):
    return len(line) - len(line.lstrip())


def step_condition(build_file_text, display_name):
    """The condition line of a named step, '' when it has none, None when there is no such step.

    A step ends at the first line sitting left of its display name — the next step's own marker, or
    whatever follows the job. A list nested deeper inside the step is part of it.
    """
    lines = build_file_text.splitlines()
    for index, line in enumerate(lines):
        if "displayName: '%s'" % display_name in line:
            for following in lines[index + 1:]:
                if not following.strip():
                    continue
                if indentation(following) < indentation(line):
                    break
                if following.strip().startswith('condition:'):
                    return following
            return ''
    return None


def jobs(build_file_text):
    """Each job's name with the lines between its name and its steps, where its own settings live."""
    lines = build_file_text.splitlines()
    found = []
    for index, line in enumerate(lines):
        if not line.strip().startswith('- job:'):
            continue
        settings = []
        for following in lines[index + 1:]:
            if not following.strip():
                continue
            if following.strip() == 'steps:' or indentation(following) <= indentation(line):
                break
            settings.append(following.strip())
        found.append((line.strip()[len('- job:'):].strip(), settings))
    return found


def problems(build_file_text):
    found = []

    declarations = [line for line in build_file_text.splitlines() if line.startswith('trigger:')]
    if len(declarations) != 1:
        found.append('Expected one push trigger in the build file, found %d' % len(declarations))
    else:
        branches = trigger_branches(build_file_text, 'trigger:')
        if branches != ['develop', 'main']:
            found.append('A push must build develop and main only; the trigger names %s' % branches)

    pull_request_branches = trigger_branches(build_file_text, 'pr:')
    if pull_request_branches != ['develop', 'main']:
        found.append(
            'A pull request into develop or main must be built, and no other; the pr block names %s'
            % pull_request_branches)

    configuration = build_configuration_value(build_file_text)
    if not configuration:
        found.append(
            'Nothing in the variables block gives buildConfiguration a value, so no branch chooses it')
    else:
        missing = [name for name in CONFIGURATION_MUST_READ if name not in configuration]
        if missing:
            found.append(
                'A build against main must compile Release and every other build Debug, and '
                'buildConfiguration never mentions %s' % ', '.join(missing))
        if PULL_REQUEST_INTO_MAIN not in configuration:
            found.append(
                "A pull request into main must compile Release, and only %s names a pull request's "
                "target as main on GitHub" % PULL_REQUEST_INTO_MAIN)

    if OWN_REPOSITORY in build_file_text:
        found.append(
            'The build file names %s, so it pushes to the repository it builds' % OWN_REPOSITORY)

    for marker in PACKAGING_MARKERS:
        if marker in build_file_text:
            found.append(
                'The build file names "%s", and a release is handed over as an archive of the '
                'running platform rather than as libraries on a feed' % marker)

    for step in PUBLISHING_STEPS:
        condition = step_condition(build_file_text, step)
        if condition is None:
            found.append('The build has no step named "%s", so nothing publishes it' % step)
        elif not condition:
            found.append('"%s" has no condition, so it publishes from every branch' % step)
        elif DEVELOP not in condition or MAIN in condition:
            found.append(
                '"%s" must run on develop only; its condition reads %s' % (step, condition.strip()))

    declared_jobs = jobs(build_file_text)
    if not declared_jobs:
        found.append(
            'The build file declares no job, so its steps run as one with the pool\'s default time '
            'limit, and a step that stops making progress holds an agent for an hour')
    for name, settings in declared_jobs:
        if not any(setting.startswith('timeoutInMinutes:') for setting in settings):
            found.append(
                'The job "%s" declares no timeoutInMinutes, so when it stops making progress it holds '
                'an agent for the pool\'s default hour' % name)

    return found
