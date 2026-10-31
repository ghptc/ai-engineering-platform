#!/usr/bin/env python3
"""Explicit, disposable Compose verification. Never prints credentials or cookie material."""
import argparse
import http.cookiejar
import http.client
import json
import os
import re
import subprocess
import time
import urllib.error
import urllib.request

parser = argparse.ArgumentParser()
parser.add_argument('--project', required=True)
parser.add_argument('--port', required=True, type=int)
args = parser.parse_args()
if not re.fullmatch(r'aep-[a-z0-9-]+-review', args.project) or args.port < 10000 or args.port > 65535:
    parser.error('Use an explicit disposable aep-*-review project and dedicated high port.')
password = os.environ.get('AEP_TEST_OWNER_PASSWORD', '')
if not password:
    parser.error('AEP_TEST_OWNER_PASSWORD is required; never use a production owner credential.')
base = f'http://127.0.0.1:{args.port}'
jar = http.cookiejar.CookieJar()
opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))
def request(path, body=None, cookie=None):
    headers = {'X-Requested-With': 'AIEngineeringPlatform'}
    if body is not None: headers['Content-Type'] = 'application/json'
    if cookie is not None: headers['Cookie'] = cookie
    req = urllib.request.Request(base + path, data=None if body is None else json.dumps(body).encode(), headers=headers)
    try:
        with opener.open(req, timeout=10) as response: return response.status
    except urllib.error.HTTPError as error: return error.code

def compose(*command):
    subprocess.run(['docker', 'compose', '-p', args.project, *command], check=True, timeout=60, stdout=subprocess.DEVNULL)

def ready():
    for attempt in range(60):
        try:
            if request('/health/ready') == 200: return
        except (OSError, http.client.HTTPException): pass
        time.sleep(0.5)
    raise RuntimeError('Review API did not become ready')

ready()
assert request('/api/v1/auth/login', {'password': password}) == 200
assert len(list(jar)) == 1
cookie = list(jar)[0]
assert cookie.expires and time.time() < cookie.expires <= time.time() + 8 * 3600
captured = cookie.name + '=' + cookie.value
assert request('/api/v1/auth/me') == 200
try:
    compose('stop', 'worker')
    assert request('/') == 200 and request('/api/v1/auth/me') == 200
    compose('restart', 'api')
    ready()
    assert request('/api/v1/auth/me') == 200
finally:
    compose('start', 'worker')
assert request('/api/v1/auth/logout', {}) == 204
assert request('/api/v1/auth/me', cookie=captured) == 401
print(json.dumps({'login': 'passed', 'persistentCookie': 'passed', 'uiDuringWorkerStop': 'passed', 'sessionAfterApiRestart': 'passed', 'logoutRevokesCapturedCookie': 'passed', 'absoluteLifetimeHours': 8}))
