#!/usr/bin/env python3
"""Real app-container/host-daemon Unix IPC smoke check; no live AI/GitHub calls."""
import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import secrets
import subprocess
import tempfile
import time
import urllib.error
import urllib.request

parser = argparse.ArgumentParser()
parser.add_argument('--image', default='ai-engineering-platform:milestone8')
args = parser.parse_args()
root = Path(__file__).resolve().parents[2]
prefix = 'aep-smoke-' + secrets.token_hex(5)
network, mongo, app = prefix + '-network', prefix + '-mongo', prefix + '-app'

def docker(*arguments):
    return subprocess.check_output(['docker', *arguments], text=True).strip()

with tempfile.TemporaryDirectory(prefix=prefix) as directory:
    state = Path(directory)
    socket_directory = state / 'socket'
    socket_directory.mkdir(mode=0o750)
    token = secrets.token_hex(32)
    token_path = state / 'token'
    token_path.write_text(token)
    token_path.chmod(0o600)
    config_path = state / 'executor.json'
    config_path.write_text(json.dumps({'Executor': {'StateDirectory': str(state / 'state'), 'SocketPath': str(socket_directory / 'executor.sock'), 'TokenFile': str(token_path), 'Repositories': []}}))
    password = 'temporary-local-container-smoke'
    salt = secrets.token_bytes(32)
    credential = '600000:' + base64.b64encode(salt).decode() + ':' + base64.b64encode(hashlib.pbkdf2_hmac('sha256', password.encode(), salt, 600000, 32)).decode()
    environment_file = state / 'app.env'
    environment_file.write_text('\n'.join(['ASPNETCORE_ENVIRONMENT=Development', 'MONGODB_CONNECTION_STRING=mongodb://' + mongo + ':27017', 'APPLICATION_OWNER_CREDENTIAL_SECRET=' + credential, 'EXECUTION_SANDBOX_PROVIDER=LocalUnix', 'EXECUTION_SANDBOX_SOCKET_PATH=/run/aep-executor/executor.sock', 'EXECUTION_SANDBOX_TOKEN=' + token]) + '\n')
    environment_file.chmod(0o600)
    daemon_environment = dict(os.environ, EXECUTOR_CONFIG=str(config_path), ASPNETCORE_ENVIRONMENT='Production')
    daemon_dll = root / 'backend/src/AIEngineeringPlatform.LocalExecutor/bin/Debug/net10.0/AIEngineeringPlatform.LocalExecutor.dll'
    with (state / 'daemon.log').open('w') as logs:
        daemon = subprocess.Popen(['dotnet', str(daemon_dll)], env=daemon_environment, stdout=logs, stderr=logs)
        try:
            docker('network', 'create', network)
            docker('run', '-d', '--name', mongo, '--network', network, 'mongo:8.0')
            for attempt in range(100):
                if daemon.poll() is not None:
                    raise RuntimeError('Host daemon failed to start; inspect runtime prerequisites.')
                if (socket_directory / 'executor.sock').exists():
                    break
                time.sleep(0.1)
            docker('run', '-d', '--name', app, '--network', network, '--env-file', str(environment_file), '--group-add', str(os.getgid()), '--mount', 'type=bind,src=' + str(socket_directory) + ',dst=/run/aep-executor,readonly', '-p', '127.0.0.1::8080', args.image)
            inspected = json.loads(docker('inspect', app))[0]
            assert inspected['Config']['User'] == 'app'
            assert len(inspected['Mounts']) == 1 and inspected['Mounts'][0]['Destination'] == '/run/aep-executor'
            port = inspected['NetworkSettings']['Ports']['8080/tcp'][0]['HostPort']
            base = 'http://127.0.0.1:' + port
            def request(path, body=None, cookie=None):
                headers = {'X-Requested-With': 'AIEngineeringPlatform', 'Content-Type': 'application/json'}
                if cookie:
                    headers['Cookie'] = cookie
                req = urllib.request.Request(base + path, data=json.dumps(body).encode() if body is not None else None, headers=headers)
                with urllib.request.urlopen(req, timeout=10) as response:
                    return response.status, response.read(), response.headers
            for attempt in range(60):
                try:
                    if request('/health/ready')[0] == 200:
                        break
                except (OSError, urllib.error.HTTPError):
                    pass
                time.sleep(0.5)
            else:
                raise RuntimeError('Application/MongoDB readiness timed out.')
            _, _, headers = request('/api/v1/auth/login', {'password': password})
            cookie = headers['Set-Cookie'].split(';')[0]
            _, content, _ = request('/api/v1/settings', cookie=cookie)
            settings = json.loads(content)
            assert settings['sandboxProvider'] == 'LocalUnix' and settings['sandboxConfigured'] and settings['sandboxReady'], settings
            print('PASS: real non-root application container -> authenticated Unix socket -> host daemon -> verified Docker readiness')
            print('PASS: separate MongoDB; only executor socket directory mounted; no Docker socket in application')
        finally:
            for container in (app, mongo):
                subprocess.run(['docker', 'rm', '-f', '-v', container], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            subprocess.run(['docker', 'network', 'rm', network], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            daemon.terminate()
            try:
                daemon.wait(timeout=10)
            except subprocess.TimeoutExpired:
                daemon.kill()
                daemon.wait()
