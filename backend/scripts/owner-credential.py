#!/usr/bin/env python3
"""Generate the environment credential hash without echoing or storing a password."""
import base64
import getpass
import hashlib
import secrets
password = getpass.getpass('Owner credential (at least 12 characters): ')
if len(password) < 12:
    raise SystemExit('Use at least 12 characters.')
if password != getpass.getpass('Confirm owner credential: '):
    raise SystemExit('Credentials did not match.')
salt = secrets.token_bytes(32)
value = hashlib.pbkdf2_hmac('sha256', password.encode(), salt, 600000, 32)
print('600000:' + base64.b64encode(salt).decode() + ':' + base64.b64encode(value).decode())
