#!/usr/bin/env python3
import json
import os
import sys
import time

assert sys.argv[1:] == ['app-server', '--stdio']
mode = os.environ.get('PETFOLIO_FAKE_MODE', 'success')
def emit(value):
    print(json.dumps(value), flush=True)
for line in sys.stdin:
    value = json.loads(line)
    method = value.get('method')
    if method == 'initialize':
        emit({'method': 'notification'})
        emit({'id': 99, 'result': {}})
        emit({'id': value['id'], 'result': {}})
    elif method == 'account/rateLimits/read':
        if mode == 'timeout':
            time.sleep(20)
        elif mode == 'eof':
            break
        elif mode == 'error':
            emit({'id': value['id'], 'error': {'message': 'Synthetic error'}})
        else:
            emit({'id': value['id'], 'result': {'rateLimitsByLimitId': {'codex': {
                'primary': {'usedPercent': 17}, 'secondary': {'usedPercent': 39}}}}})
