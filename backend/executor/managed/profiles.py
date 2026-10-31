#!/usr/bin/python3
"""Owner-defined offline profiles. Only structured runner results certify test counts."""
import json
import pathlib
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET

root = pathlib.Path('/workspace')
profile = sys.argv[1]

def execute(arguments):
    # Stream directly so the outer executor enforces time/output/resource bounds.
    return subprocess.run(arguments, cwd=root, check=False, timeout=110).returncode

def require(arguments):
    code = execute(arguments)
    if code:
        raise subprocess.CalledProcessError(code, arguments)

def dotnet_targets(directory):
    targets = sorted(str(p.relative_to(root)) for p in (root / directory).rglob('*.csproj') if 'obj' not in p.parts and 'bin' not in p.parts)
    if len(targets) > 20:
        raise ValueError('Too many project targets for one bounded profile')
    return targets

def npm_prepare():
    if not (root / 'frontend/package-lock.json').is_file():
        raise ValueError('MISSING_TEST_INFRASTRUCTURE: persist an approved frontend lockfile before execution')
    require(['npm', 'ci', '--offline', '--ignore-scripts', '--prefix', 'frontend', '--no-audit', '--no-fund'])

def bounded_report(path):
    if not path.is_file() or path.stat().st_size > 10 * 1024 * 1024:
        raise ValueError('Missing or oversized structured test report')
    return path

def backend_tests():
    targets = dotnet_targets('backend/tests')
    if not targets:
        raise ValueError('MISSING_TEST_INFRASTRUCTURE: create backend test projects with executable tests')
    total = 0
    for target in targets:
        with tempfile.TemporaryDirectory(prefix='aep-trx-') as temporary:
            code = execute(['dotnet', 'test', target, '-p:NuGetAudit=false', '-v:minimal', '--logger', 'trx', '--results-directory', temporary])
            reports = list(pathlib.Path(temporary).glob('*.trx'))
            if not reports or len(reports) > 20:
                raise ValueError('The test runner did not produce bounded TRX evidence')
            executed = 0
            for report in reports:
                counters = ET.parse(bounded_report(report)).getroot().find('.//{*}Counters')
                if counters is None:
                    raise ValueError('TRX counters are missing')
                count = int(counters.attrib['executed'])
                if count < 0 or count > int(counters.attrib['total']):
                    raise ValueError('Invalid executed-test counters')
                executed += count
            total += executed
            if code or executed == 0:
                return total, code or 1
    return total, 0

def frontend_tests():
    npm_prepare()
    with tempfile.TemporaryDirectory(prefix='aep-vitest-') as temporary:
        report = pathlib.Path(temporary) / 'results.json'
        code = execute(['npm', 'test', '--prefix', 'frontend', '--', '--maxWorkers=1', '--no-file-parallelism', '--reporter=json', '--outputFile=' + str(report)])
        result = json.loads(bounded_report(report).read_text())
        passed, failed = int(result['numPassedTests']), int(result['numFailedTests'])
        executed = passed + failed
        if passed < 0 or failed < 0 or executed > int(result['numTotalTests']):
            raise ValueError('Invalid Vitest executed-test counters')
        return executed, code if code else (0 if executed > 0 and result['success'] and failed == 0 else 1)

count = 0
completed = []
code = 0
try:
    if profile in ('backend', 'frontend', 'all'):
        for stage in (['backend', 'frontend'] if profile == 'all' else [profile]):
            executed, code = backend_tests() if stage == 'backend' else frontend_tests()
            count += executed
            completed.append(stage)
            if code:
                break
    elif profile == 'typecheck':
        npm_prepare()
        require(['npm', 'run', 'typecheck', '--prefix', 'frontend'])
    elif profile == 'build':
        targets = dotnet_targets('backend/src')
        if not targets and not (root / 'frontend/package-lock.json').is_file():
            raise ValueError('MISSING_BUILD_INFRASTRUCTURE: create a backend project or frontend lockfile')
        for target in targets:
            require(['dotnet', 'build', target, '-p:NuGetAudit=false', '-v:minimal'])
        if (root / 'frontend/package-lock.json').is_file():
            npm_prepare()
            require(['npm', 'run', 'build', '--prefix', 'frontend'])
    else:
        raise ValueError('Unapproved execution profile')
except (ValueError, KeyError, OSError, ET.ParseError, subprocess.SubprocessError) as error:
    print(str(error), file=sys.stderr)
    code = 1
finally:
    if profile in ('backend', 'frontend', 'all'):
        # The final trusted helper summary follows all untrusted command output.
        print('AEP managed test evidence: executed=' + str(count) + '; profiles=' + ','.join(completed) + '; exit=' + str(code) + ';', flush=True)
sys.exit(code)
