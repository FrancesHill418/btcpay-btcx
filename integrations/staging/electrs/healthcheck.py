#!/usr/bin/env python3
import json
import socket
import sys

host = sys.argv[1]
port = int(sys.argv[2])
request = json.dumps({"id": 1, "method": "server.version", "params": ["btcx-staging-health", "1.4"]}) + "\n"

with socket.create_connection((host, port), timeout=3) as connection:
    connection.settimeout(3)
    connection.sendall(request.encode("utf-8"))
    response = connection.makefile("rb").readline(65537)

message = json.loads(response)
if message.get("id") != 1 or not isinstance(message.get("result"), list):
    raise SystemExit("electrs did not return a valid server.version response")
