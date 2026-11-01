#!/usr/bin/python3
"""Trusted npm resolution helper. Network is absent; HTTPS is brokered by the executor.
No repository scripts, .npmrc, credentials, arbitrary arguments or source code are loaded.
"""
import base64, hashlib, http.server, io, json, os, pathlib, shutil, subprocess, sys, tarfile, threading
MAX = 48 * 1024 * 1024
wire_lock = threading.Lock()
def send(value):
    sys.stdout.write(json.dumps(value, separators=(',', ':')) + '\n'); sys.stdout.flush()
def broker(path):
    with wire_lock:
        send({'kind': 'registry', 'path': path})
        line = sys.stdin.readline(32 * 1024 * 1024)
        value = json.loads(line)
        if value.get('status') != 200:
            raise ValueError('Registry request rejected')
        return base64.b64decode(value['body'], validate=True)
class Registry(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        try:
            data = broker(self.path)
            if '/-/' not in self.path:
                value = json.loads(data)
                # npm must see only the loopback broker, never an unrestricted URL.
                for version in value.get('versions', {}).values():
                    dist = version.get('dist', {})
                    url = dist.get('tarball', '')
                    if not url.startswith('https://registry.npmjs.org/'):
                        raise ValueError('Unsupported package source')
                    dist['tarball'] = 'http://127.0.0.1:%d/%s' % (self.server.server_port, url.split('https://registry.npmjs.org/', 1)[1])
                data = json.dumps(value, separators=(',', ':')).encode()
            self.send_response(200); self.send_header('Content-Length', str(len(data))); self.end_headers(); self.wfile.write(data)
        except Exception:
            self.send_error(403, 'Registry request blocked')
    def log_message(self, *args): pass
try:
    initial = json.loads(sys.stdin.readline(3000000))
    root = pathlib.Path('/tmp/resolve'); root.mkdir(mode=0o700)
    manifest = initial['manifest']
    (root / 'package.json').write_text(json.dumps(manifest))
    if initial.get('lock'):
        (root / 'package-lock.json').write_text(initial['lock'])
    server = http.server.ThreadingHTTPServer(('127.0.0.1', 0), Registry)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    registry = 'http://127.0.0.1:%d/' % server.server_port
    env = {'PATH': '/usr/local/bin:/usr/bin:/bin', 'HOME': '/tmp', 'npm_config_cache': '/tmp/npm', 'npm_config_registry': registry, 'npm_config_userconfig': '/tmp/no-user.npmrc', 'npm_config_globalconfig': '/tmp/no-global.npmrc', 'npm_config_ignore_scripts': 'true', 'npm_config_audit': 'false', 'npm_config_fund': 'false', 'npm_config_fetch_retries': '0', 'npm_config_update_notifier': 'false'}
    if pathlib.Path('/opt/npm-cache').is_dir(): shutil.copytree('/opt/npm-cache', '/tmp/npm/_cacache', dirs_exist_ok=True)
    log = pathlib.Path('/tmp/npm-resolution.log')
    with log.open('wb') as out:
        code = subprocess.run(['npm', 'install', '--package-lock-only', '--ignore-scripts', '--no-audit', '--no-fund'], cwd=root, env=env, stdin=subprocess.DEVNULL, stdout=out, stderr=out, timeout=100).returncode
    if code != 0: raise ValueError('Bounded npm resolution failed (scripts disabled): ' + log.read_text(errors='replace')[:1500])
    lock = json.loads((root / 'package-lock.json').read_text())
    if len(lock.get('packages', {})) > 500: raise ValueError('Dependency graph exceeds 500 entries')
    for path, package in lock['packages'].items():
        if not path: continue
        url = package.get('resolved', '')
        if url.startswith('https://registry.npmjs.org/'):
            fetch = registry + url.split('https://registry.npmjs.org/', 1)[1]
        elif url.startswith(registry):
            fetch = url
        else: raise ValueError('Only registry tarballs are supported')
        if not package.get('integrity', '').startswith('sha512-'): raise ValueError('SHA-512 integrity is required')
        with log.open('ab') as out:
            if subprocess.run(['npm', 'cache', 'add', fetch, '--ignore-scripts'], cwd=root, env=env, stdin=subprocess.DEVNULL, stdout=out, stderr=out, timeout=30).returncode != 0: raise ValueError('Tarball acquisition failed')
        package['resolved'] = 'https://registry.npmjs.org/' + fetch.split(registry, 1)[1]
    # Export content-addressed blobs only. Never export auth headers, npm config, logs or metadata indexes.
    stream = io.BytesIO(); count = total = 0
    cache = pathlib.Path('/tmp/npm/_cacache/content-v2')
    wanted = {base64.b64decode(p['integrity'][7:]).hex() for k, p in lock['packages'].items() if k}
    with tarfile.open(fileobj=stream, mode='w') as archive:
        for path in sorted(cache.rglob('*')):
            if not path.is_file() or path.parts[-4] != 'sha512' or ''.join(path.parts[-3:]) not in wanted: continue
            if path.is_symlink(): raise ValueError('Linked cache content rejected')
            data = path.read_bytes(); total += len(data); count += 1
            if total > MAX or count > 4000: raise ValueError('Acquisition cache exceeds bounds')
            item = tarfile.TarInfo('content-v2/' + path.relative_to(cache).as_posix()); item.size = len(data); item.mode = 0o600; item.mtime = 0
            archive.addfile(item, io.BytesIO(data))
    encoded = stream.getvalue()
    send({'kind': 'result', 'lock': json.dumps(lock, separators=(',', ':')) + '\n', 'cache': base64.b64encode(encoded).decode(), 'cacheSha256': hashlib.sha256(encoded).hexdigest()})
except Exception as error:
    send({'kind': 'error', 'message': str(error)[:500]}); sys.exit(1)
