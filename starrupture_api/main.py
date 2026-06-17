from __future__ import annotations

import argparse
import json
import os
import sys

import uvicorn

from .config import settings
from .http_app import create_app
from .service import ResourceService


_standard_stream_sinks = []


def ensure_standard_streams() -> None:
    if sys.stdout is None:
        sys.stdout = open(os.devnull, "w", encoding="utf-8", buffering=1)
        _standard_stream_sinks.append(sys.stdout)

    if sys.stderr is None:
        sys.stderr = open(os.devnull, "w", encoding="utf-8", buffering=1)
        _standard_stream_sinks.append(sys.stderr)


def main() -> None:
    ensure_standard_streams()

    parser = argparse.ArgumentParser(description="StarRupture resource API and MCP server")
    subparsers = parser.add_subparsers(dest="command")

    serve = subparsers.add_parser("serve", help="Run the HTTP API and MCP SSE server")
    serve.add_argument("--host", default=settings.host)
    serve.add_argument("--port", type=int, default=settings.port)

    item = subparsers.add_parser("item", help="Print an item detail payload")
    item.add_argument("item_id")

    args = parser.parse_args()
    service = ResourceService(settings)

    if args.command == "item":
        print(json.dumps(service.get_item_detail(args.item_id), indent=2))
    else:
        uvicorn.run(create_app(settings), host=getattr(args, "host", settings.host), port=getattr(args, "port", settings.port))


if __name__ == "__main__":
    main()
