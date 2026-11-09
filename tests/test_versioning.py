from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
SCRIPT = ROOT / '.github/scripts/check_versioning.py'
SHA = 'c' * 40


def project_xml(version):
    return f'<Project><PropertyGroup><PackageId>A</PackageId><Version>{version}</Version></PropertyGroup></Project>\n'


def updater(file):
    return f'  - regex:\n      file: {file}\n      pattern: (?<=<Version>)[^<]+(?=</Version>)\n'


def changelog(version, updaters=None):
    return (
        f'---\nlast_commit_released: {SHA}\nupdaters:\n{updaters or updater("A.fsproj")}---\n\n'
        f'# Changelog\n\n## {version} - 2026-11-09\n\n## 0.1.0\n'
    )


class VersioningTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.git('init', '-q')
        self.write('src/A/A.fsproj', project_xml('0.1.1-rc.1'))
        self.write('src/A/CHANGELOG.md', changelog('0.1.1-rc.1'))
        self.commit()

    def git(self, *args):
        subprocess.run(
            ['git', '-c', 'user.name=t', '-c', 'user.email=t@example.com', '-c', 'core.autocrlf=false', *args],
            cwd=self.root, check=True, capture_output=True)

    def write(self, path, content):
        target = self.root / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(content.encode())

    def commit(self):
        self.git('add', '-A')
        self.git('commit', '-qm', 'fixture', '--allow-empty')

    def check(self, *args):
        return subprocess.run([sys.executable, '-I', str(SCRIPT), '--root', str(self.root), *args],
                              capture_output=True, text=True)

    def pull_request(self, head='fix/thing', author='someone', title='fix: thing'):
        return self.check('--base', 'HEAD~1', '--head-ref', head, '--author', author, '--title', title)

    def assertRejected(self, result, text):
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn(text, result.stderr)

    def test_a_project_matching_its_changelog_passes(self):
        result = self.check()
        self.assertEqual(result.returncode, 0, result.stderr)

    def test_a_version_ahead_of_the_changelog_fails(self):
        self.write('src/A/A.fsproj', project_xml('0.1.1-rc.2'))
        self.assertRejected(self.check(), '0.1.1-rc.1')

    def test_a_packaged_project_without_a_changelog_fails(self):
        (self.root / 'src/A/CHANGELOG.md').unlink()
        self.commit()
        self.assertRejected(self.check(), 'CHANGELOG.md')

    def test_a_changelog_shipit_cannot_read_fails(self):
        self.write('src/A/CHANGELOG.md', changelog('0.1.1-rc.1').replace('\n', '\r\n'))
        self.commit()
        self.assertRejected(self.check(), 'heading')

    def test_a_changelog_without_a_baseline_fails(self):
        self.write('src/A/CHANGELOG.md', changelog('0.1.1-rc.1').replace(f'last_commit_released: {SHA}\n', ''))
        self.commit()
        self.assertRejected(self.check(), 'last_commit_released')

    def test_a_changelog_without_an_updater_for_the_project_fails(self):
        self.write('src/A/CHANGELOG.md', changelog('0.1.1-rc.1').replace('file: A.fsproj', 'file: B.fsproj'))
        self.commit()
        self.assertRejected(self.check(), 'updater')

    def test_an_xml_updater_fails_because_it_reformats_the_project(self):
        self.write('src/A/CHANGELOG.md', changelog(
            '0.1.1-rc.1', '  - xml:\n      file: A.fsproj\n      selector: /Project/PropertyGroup/Version\n'))
        self.commit()
        self.assertRejected(self.check(), 'xml')

    def test_projects_may_share_one_changelog(self):
        self.write('src/B/B.fsproj', project_xml('0.1.1-rc.1'))
        self.write('src/A/CHANGELOG.md', changelog('0.1.1-rc.1', updater('A.fsproj') + updater('../B/B.fsproj')))
        self.commit()
        result = self.check()
        self.assertEqual(result.returncode, 0, result.stderr)

    def test_a_project_behind_its_shared_changelog_fails(self):
        self.write('src/B/B.fsproj', project_xml('0.1.0'))
        self.write('src/A/CHANGELOG.md', changelog('0.1.1-rc.1', updater('A.fsproj') + updater('../B/B.fsproj')))
        self.commit()
        self.assertRejected(self.check(), 'src/B/B.fsproj')

    def reference_a_from_b(self, version):
        self.write('src/B/B.csproj',
                   f'<Project><ItemGroup><PackageReference Include="A" Version="{version}" /></ItemGroup></Project>\n')
        self.commit()

    def test_a_literal_reference_to_a_shipit_versioned_package_fails(self):
        self.reference_a_from_b('[0.1.1-rc.1]')
        self.assertRejected(self.check(), 'src/B/B.csproj')

    def test_a_property_reference_to_a_shipit_versioned_package_passes(self):
        self.reference_a_from_b('[$(AVersion)]')
        result = self.check()
        self.assertEqual(result.returncode, 0, result.stderr)

    def release_a_new_version(self):
        self.write('src/A/A.fsproj', project_xml('0.1.1-rc.2'))
        self.write('src/A/CHANGELOG.md', changelog('0.1.1-rc.2'))
        self.commit()

    def test_a_hand_bump_in_a_pull_request_fails(self):
        self.release_a_new_version()
        self.assertRejected(self.pull_request(), 'src/A/A.fsproj')

    def test_a_hand_bump_on_a_branch_named_like_the_release_branch_fails(self):
        self.release_a_new_version()
        self.assertRejected(self.pull_request(head='release/main'), 'src/A/A.fsproj')

    def test_the_shipit_release_pull_request_may_bump(self):
        self.release_a_new_version()
        result = self.pull_request(
            head='release/main', author='github-actions[bot]', title='chore: release multiple projects')
        self.assertEqual(result.returncode, 0, result.stderr)

    def test_a_pull_request_leaving_versions_alone_passes(self):
        self.write('src/A/Code.fs', 'module A\n')
        self.commit()
        result = self.pull_request()
        self.assertEqual(result.returncode, 0, result.stderr)

    def test_a_new_packaged_project_may_choose_its_first_version(self):
        self.write('src/B/A.fsproj', project_xml('0.1.0-rc.1'))
        self.write('src/B/CHANGELOG.md', changelog('0.1.0-rc.1'))
        self.commit()
        result = self.pull_request()
        self.assertEqual(result.returncode, 0, result.stderr)

    def test_a_release_title_outside_the_release_pull_request_fails(self):
        self.write('src/A/Code.fs', 'module A\n')
        self.commit()
        for title in ('chore: release A 1.0.0', 'chore(release): A 1.0.0'):
            with self.subTest(title):
                self.assertRejected(self.pull_request(title=title), 'publish')


if __name__ == '__main__':
    unittest.main()
