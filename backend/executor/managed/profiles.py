#!/usr/bin/python3
"""Owner-defined, offline argument arrays. Repository text never selects a shell command."""
import pathlib
import subprocess
import sys

root = pathlib.Path('/workspace')
profile = sys.argv[1]
def execute(arguments):
    subprocess.run(arguments, cwd=root, check=True, timeout=110)

def dotnet_targets(directory):
    return sorted(str(p.relative_to(root)) for p in (root / directory).rglob('*.csproj') if 'obj' not in p.parts and 'bin' not in p.parts)[:21]

def npm_prepare():
    if not (root / 'frontend/package-lock.json').is_file():
        raise ValueError('MISSING_TEST_INFRASTRUCTURE: create and persist an approved frontend lockfile before execution')
    execute(['npm', 'ci', '--offline', '--ignore-scripts', '--prefix', 'frontend', '--no-audit', '--no-fund'])

try:
    if profile == 'backend':
        targets = dotnet_targets('backend/tests')
        if not targets or len(targets) > 20:
            raise ValueError('MISSING_TEST_INFRASTRUCTURE: create backend test projects with executable tests')
        for target in targets:
            execute(['dotnet', 'test', target, '-p:NuGetAudit=false', '-v:minimal'])
    elif profile in ('frontend', 'typecheck'):
        npm_prepare()
        execute(['npm', 'test' if profile == 'frontend' else 'run', *([] if profile == 'frontend' else ['typecheck']), '--prefix', 'frontend'])
    elif profile == 'build':
        targets = dotnet_targets('backend/src')
        if len(targets) > 20:
            raise ValueError('Too many build targets for one bounded profile')
        if not targets and not (root / 'frontend/package-lock.json').is_file():
            raise ValueError('MISSING_BUILD_INFRASTRUCTURE: create a backend project or frontend lockfile')
        for target in targets:
            execute(['dotnet', 'build', target, '-p:NuGetAudit=false', '-v:minimal'])
        if (root / 'frontend/package-lock.json').is_file():
            npm_prepare()
            execute(['npm', 'run', 'build', '--prefix', 'frontend'])
    else:
        raise ValueError('Unapproved execution profile')
except (ValueError, subprocess.SubprocessError) as error:
    print(str(error), file=sys.stderr)
    sys.exit(1)
