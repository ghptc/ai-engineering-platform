#!/usr/bin/env python3
"""Generate owner-only local runtime configuration. Never print credentials."""
import argparse
import base64
import getpass
import hashlib
import json
import os
from pathlib import Path
import re
import secrets
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument('--repository', required=True, help='Explicitly authorized OWNER/NAME; use a disposable repository for verification.')
parser.add_argument('--installation-id', required=True, type=int)
parser.add_argument('--default-branch', default='main')
parser.add_argument('--image', default='aep-executor-runtime:local')
args = parser.parse_args()
if not re.fullmatch(r'[A-Za-z0-9-]{1,39}/[A-Za-z0-9_.-]{1,100}', args.repository) or args.installation_id <= 0:
    raise SystemExit('Invalid explicit repository identity or installation ID.')
root = Path(__file__).resolve().parents[2]
runtime = root / 'backend/.local-runtime'
runtime.mkdir(mode=0o700, parents=True, exist_ok=True)
(runtime / 'socket').mkdir(mode=0o750, exist_ok=True)
config_path = runtime / 'executor.json'
if config_path.exists():
    raise SystemExit('executor.json already exists; edit the owner-managed allowlist/profiles instead of overwriting it.')
source = root / '.env'
if not source.exists():
    raise SystemExit('Create .env from .env.example and configure OpenAI/GitHub first. No credentials are inferred from gh login.')
values = {}
for line in source.read_text().splitlines():
    if line.strip() and not line.lstrip().startswith('#') and '=' in line:
        key, value = line.split('=', 1)
        values[key.strip()] = value.strip().strip('"\'')
if not values.get('APPLICATION_OWNER_CREDENTIAL_SECRET'):
    password = getpass.getpass('Local owner credential (at least 12 characters): ')
    if len(password) < 12 or password != getpass.getpass('Confirm credential: '):
        raise SystemExit('Credential length/confirmation failed.')
    salt = secrets.token_bytes(32)
    digest = hashlib.pbkdf2_hmac('sha256', password.encode(), salt, 600000, 32)
    values['APPLICATION_OWNER_CREDENTIAL_SECRET'] = '600000:' + base64.b64encode(salt).decode() + ':' + base64.b64encode(digest).decode()
token_path = runtime / 'control-token'
if not token_path.exists():
    token_path.write_text(secrets.token_hex(32))
token_path.chmod(0o600)
image = subprocess.check_output(['docker', 'image', 'inspect', args.image, '--format', '{{.Id}}'], text=True).strip()
key = values.get('GITHUB_APP_PRIVATE_KEY_PATH', '')
if key:
    key = str(Path(key).expanduser().resolve(strict=True))
    copied_key = runtime / 'github-app.pem'
    if Path(key) != copied_key:
        copied_key.write_bytes(Path(key).read_bytes())
    copied_key.chmod(0o640)
    key = str(copied_key)
    values['GITHUB_APP_PRIVATE_KEY_PATH'] = key
config = json.loads((root / 'backend/executor/executor.example.json').read_text())
executor = config['Executor']
executor.update(StateDirectory=str(runtime / 'state'), SocketPath=str(runtime / 'socket/executor.sock'), TokenFile=str(token_path), GitHubAppId=values.get('GITHUB_APP_ID', ''), GitHubPrivateKeyPath=values.get('GITHUB_APP_PRIVATE_KEY_PATH', ''))
repository = executor['Repositories'][0]
repository.update(Owner=args.repository.split('/')[0], Name=args.repository.split('/')[1], InstallationId=args.installation_id, DefaultBranch=args.default_branch)
for profile in repository['Profiles'].values():
    profile['Image'] = image
config_path.write_text(json.dumps(config, indent=2) + '\n')
config_path.chmod(0o600)
if key:
    key = str(Path(key).expanduser().resolve())
    if not Path(key).is_file():
        raise SystemExit('GitHub App private key file does not exist.')
    (runtime / 'github-key-path').write_text(key)
    values['GITHUB_APP_PRIVATE_KEY_PATH'] = '/run/secrets/github-app.pem'
values.update(ASPNETCORE_ENVIRONMENT='Development', ASPNETCORE_HTTP_PORTS='8080', APPLICATION_AUTH_MODE='Owner', MONGODB_CONNECTION_STRING='mongodb://aep-local-mongo:27017', EXECUTION_SANDBOX_PROVIDER='LocalUnix', EXECUTION_SANDBOX_SOCKET_PATH='/run/aep-executor/executor.sock', EXECUTION_SANDBOX_TOKEN=token_path.read_text().strip())
application = runtime / 'application.env'
application.write_text('\n'.join(key + '=' + value for key, value in values.items()) + '\n')
application.chmod(0o600)
print('Local configuration generated. Review executor.json profiles and prepare offline dependency caches before starting the daemon. Credentials were not printed.')
