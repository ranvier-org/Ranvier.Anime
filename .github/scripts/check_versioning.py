"""Fail unless ShipIt owns the version of every packaged project.

Every run checks each project against its changelog. Given a pull request's base, it also rejects version changes and
release titles from anything but ShipIt's release pull request.
"""

import argparse
from pathlib import Path
import posixpath
import re
import subprocess
import sys

RELEASE_BRANCH = 'release/main'
RELEASE_AUTHOR = 'github-actions[bot]'
RELEASE_TITLES = ('chore: release ', 'chore(release): ')
# The expression ShipIt reads versions with. A heading ending in CRLF does not match.
HEADING = re.compile(r'^##\s\[?v?(?P<version>[\w.-]+\.[\w.-]+[a-zA-Z0-9])\]?(\s-\s\d{4}-\d{2}-\d{2})?$', re.MULTILINE)
UPDATER = re.compile(r'^\s*-\s*(?P<kind>[\w.]+):\s*\n\s*file:\s*(?P<file>\S+)\s*$', re.MULTILINE)
REFERENCE = re.compile(r'<PackageReference\s+Include="(?P<package>[^"]+)"\s+Version="(?P<version>[^"]+)"')


def git(root, *args):
    return subprocess.run(['git', '-C', str(root), *args], capture_output=True, text=True, encoding='utf-8')


def read(path):
    # Bytes, so that line endings reach the heading expression unchanged.
    return path.read_bytes().decode('utf-8')


def tracked_projects(root):
    tracked = git(root, 'ls-files', '*.fsproj', '*.csproj').stdout.splitlines()
    return [path for path in tracked if (root / path).exists()]


def packaged_projects(root):
    return [path for path in tracked_projects(root) if '<PackageId>' in read(root / path)]


def declared_version(text):
    versions = re.findall(r'<Version>([^<$]+)</Version>', text)
    return versions[0] if len(versions) == 1 else None


def changelogs(root):
    tracked = git(root, 'ls-files', 'CHANGELOG.md', '*/CHANGELOG.md').stdout.splitlines()
    return {path: read(root / path) for path in tracked if (root / path).exists()}


def changelog_problems(path, text):
    problems = []
    if not re.search(r'^last_commit_released: [0-9a-f]{40}\s*$', text, re.MULTILINE):
        problems.append(f'{path} has no last_commit_released baseline.')
    if HEADING.search(text) is None:
        problems.append(f'{path} has no version heading ShipIt can read.')
    if any(match['kind'] == 'xml' for match in UPDATER.finditer(text)):
        problems.append(f'{path} uses an xml updater, which reformats the whole file. Use a regex updater.')
    return problems


def project_owners(texts):
    return {posixpath.normpath(posixpath.join(posixpath.dirname(path), match['file'])): path
            for path, text in texts.items() for match in UPDATER.finditer(text)}


def project_problems(root, path, owners, texts):
    version = declared_version(read(root / path))
    if version is None:
        return [f'{path} must hold exactly one literal <Version>.']
    owner = owners.get(path)
    if owner is None:
        return [f'{path} has no CHANGELOG.md updater, so ShipIt cannot version it.']
    heading = HEADING.search(texts[owner])
    if heading is not None and heading['version'] != version:
        return [f"{path} is at {version}, but the latest heading in {owner} is {heading['version']}."]
    return []


def reference_problems(root, projects):
    owned = {package for path in projects for package in re.findall(r'<PackageId>([^<$]+)</PackageId>', read(root / path))}
    return [f'{path} pins {match["package"]} at {match["version"]}, which ShipIt will move. Read the version from a property.'
            for path in tracked_projects(root)
            for match in REFERENCE.finditer(read(root / path))
            if match['package'] in owned and '$(' not in match['version']]


def pull_request_problems(root, projects, base, head_ref, author, title):
    if head_ref == RELEASE_BRANCH and author == RELEASE_AUTHOR:
        return []
    problems = []
    if title.startswith(RELEASE_TITLES):
        problems.append(f'The title "{title}" would publish packages. Only ShipIt\'s release pull request may use it.')
    for path in projects:
        before = git(root, 'show', f'{base}:{path}')
        if before.returncode == 0 and declared_version(before.stdout) != declared_version(read(root / path)):
            problems.append(f'{path} changes its <Version>. ShipIt owns version changes: see CONTRIBUTOR.md.')
    return problems


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=Path.cwd())
    parser.add_argument('--base')
    parser.add_argument('--head-ref', default='')
    parser.add_argument('--author', default='')
    parser.add_argument('--title', default='')
    args = parser.parse_args()
    projects = packaged_projects(args.root)
    texts = changelogs(args.root)
    owners = project_owners(texts)
    problems = [problem for path, text in texts.items() for problem in changelog_problems(path, text)]
    problems += [problem for path in projects for problem in project_problems(args.root, path, owners, texts)]
    problems += reference_problems(args.root, projects)
    if args.base:
        problems += pull_request_problems(args.root, projects, args.base, args.head_ref, args.author, args.title)
    for problem in problems:
        print(problem, file=sys.stderr)
    return 1 if problems else 0


if __name__ == '__main__':
    sys.exit(main())
