import os
import unittest

from check_build_file import problems

REPOSITORY_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

COMPLIANT = """\
trigger:
  branches:
    include:
      - develop
      - main

variables:
  - name: buildConfiguration
    value: $[ replace(replace(or(eq(variables['Build.SourceBranch'], 'refs/heads/main'), \
eq(variables['System.PullRequest.TargetBranch'], 'refs/heads/main')), 'True', 'Release'), \
'False', 'Debug') ]

steps:
- bash: echo publish
  displayName: 'Publish Docs to Wiki'
  condition: and(succeeded(), eq(variables['Build.SourceBranch'], 'refs/heads/main'))

- bash: echo mirror
  displayName: 'Mirror to GitHub'
  condition: and(succeeded(), eq(variables['Build.SourceBranch'], 'refs/heads/main'))

- task: SomeTask@1
  displayName: 'Mirror Wiki to GitHub'
  inputs:
    script: echo wiki
  condition: and(succeeded(), eq(variables['Build.SourceBranch'], 'refs/heads/main'))
"""


def build_file_text():
    with open(os.path.join(REPOSITORY_ROOT, 'azure-pipelines.yml'), encoding='utf-8') as handle:
        return handle.read()


class CheckBuildFileTests(unittest.TestCase):
    def test_the_build_file_this_repository_ships_matches_the_policy(self):
        self.assertEqual([], problems(build_file_text()))

    def test_a_compliant_build_file_has_nothing_to_report(self):
        self.assertEqual([], problems(COMPLIANT))

    def test_a_widened_trigger_is_reported(self):
        widened = COMPLIANT.replace('      - main\n', '      - main\n      - release\n')

        self.assertIn('the trigger names', problems(widened)[0])

    def test_one_configuration_for_every_branch_is_reported(self):
        literal = COMPLIANT.replace(
            COMPLIANT[COMPLIANT.index('  - name: buildConfiguration'):COMPLIANT.index('\nsteps:')],
            "  - name: buildConfiguration\n    value: 'Release'")

        self.assertIn('must compile Release', problems(literal)[0])

    def test_a_configuration_blind_to_pull_requests_is_reported(self):
        branch_only = COMPLIANT.replace(
            "eq(variables['System.PullRequest.TargetBranch'], 'refs/heads/main')), ", '), ')

        self.assertIn('System.PullRequest.TargetBranch', problems(branch_only)[0])

    def test_a_publishing_step_left_on_develop_is_reported(self):
        on_develop = COMPLIANT.replace(
            "  condition: and(succeeded(), eq(variables['Build.SourceBranch'], 'refs/heads/main'))\n\n"
            "- bash: echo mirror",
            "  condition: and(succeeded(), or(eq(variables['Build.SourceBranch'], 'refs/heads/develop'), "
            "eq(variables['Build.SourceBranch'], 'refs/heads/main')))\n\n- bash: echo mirror")

        self.assertIn('"Publish Docs to Wiki" must run on main only', problems(on_develop)[0])

    def test_a_second_push_trigger_is_reported(self):
        twice = COMPLIANT + '\ntrigger:\n  branches:\n    include:\n      - main\n'

        self.assertIn('found 2', problems(twice)[0])

    def test_a_configuration_nothing_declares_is_reported(self):
        undeclared = COMPLIANT.replace('  - name: buildConfiguration\n', '  - name: somethingElse\n')

        self.assertIn('gives buildConfiguration a value', problems(undeclared)[0])

    def test_a_configuration_declared_with_no_value_is_reported(self):
        valueless = COMPLIANT[:COMPLIANT.index('    value: $[')]

        self.assertIn('gives buildConfiguration a value', problems(valueless)[0])

    def test_a_build_file_with_no_variables_at_all_is_reported(self):
        nothing_declared = COMPLIANT.replace('variables:\n', 'notvariables:\n')

        self.assertIn('gives buildConfiguration a value', problems(nothing_declared)[0])

    def test_a_publishing_step_with_no_condition_at_all_is_reported(self):
        unconditional = COMPLIANT.replace(
            "- bash: echo publish\n  displayName: 'Publish Docs to Wiki'\n"
            "  condition: and(succeeded(), eq(variables['Build.SourceBranch'], 'refs/heads/main'))\n",
            "- bash: echo publish\n  displayName: 'Publish Docs to Wiki'\n")

        self.assertIn('"Publish Docs to Wiki" has no condition', problems(unconditional)[0])

    def test_a_comment_between_the_name_and_its_value_is_read_through(self):
        commented = COMPLIANT.replace(
            '  - name: buildConfiguration\n', '  - name: buildConfiguration\n    # why it is chosen\n')

        self.assertEqual([], problems(commented))

    def test_the_last_step_in_the_file_having_no_condition_is_reported(self):
        unconditional = COMPLIANT[:COMPLIANT.index(
            "  condition: and(succeeded(), eq(variables['Build.SourceBranch'], 'refs/heads/main'))\n"
            "\n- task: SomeTask@1")] + "\n- task: SomeTask@1\n  displayName: 'Mirror Wiki to GitHub'\n"

        self.assertIn('"Mirror Wiki to GitHub" has no condition', problems(unconditional)[-1])

    def test_a_publishing_step_that_disappeared_is_reported(self):
        renamed = COMPLIANT.replace("displayName: 'Mirror Wiki to GitHub'", "displayName: 'Sync wiki'")

        self.assertIn('no step named "Mirror Wiki to GitHub"', problems(renamed)[0])

    def test_a_step_that_packs_a_library_is_reported(self):
        packing = COMPLIANT + (
            "\n- script: dotnet pack --configuration $(buildConfiguration) -o ./nupkgs\n"
            "  displayName: 'Pack NuGet Package'\n"
            "  condition: and(succeeded(), eq(variables['Build.SourceBranch'], 'refs/heads/main'))\n")

        self.assertIn('"dotnet pack"', problems(packing)[0])

    def test_a_step_that_pushes_to_a_feed_is_reported(self):
        pushing = COMPLIANT + (
            "\n- task: DotNetCoreCLI@2\n"
            "  displayName: 'Push NuGet Package to Azure Artifacts'\n"
            "  inputs:\n"
            "    command: 'push'\n"
            "    publishVstsFeed: 'NuGetPackages'\n"
            "  condition: and(succeeded(), eq(variables['Build.SourceBranch'], 'refs/heads/main'))\n")

        self.assertIn('"publishVstsFeed"', problems(pushing)[0])


if __name__ == '__main__':
    unittest.main()
