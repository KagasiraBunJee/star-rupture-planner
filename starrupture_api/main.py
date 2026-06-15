from __future__ import annotations

import argparse
import json

import uvicorn

from .config import settings
from .http_app import create_app
from .service import ResourceService


def main() -> None:
    parser = argparse.ArgumentParser(description="StarRupture resource API and MCP server")
    subparsers = parser.add_subparsers(dest="command")

    subparsers.add_parser("refresh", help="Scrape and rebuild the local dataset")

    serve = subparsers.add_parser("serve", help="Run the HTTP API and MCP SSE server")
    serve.add_argument("--host", default=settings.host)
    serve.add_argument("--port", type=int, default=settings.port)

    item = subparsers.add_parser("item", help="Print an item detail payload")
    item.add_argument("item_id")

    args = parser.parse_args()
    service = ResourceService(settings)

    if args.command == "refresh":
        print(json.dumps(service.refresh_dataset(), indent=2))
    elif args.command == "item":
        print(json.dumps(service.get_item_detail(args.item_id), indent=2))
    else:
        uvicorn.run(create_app(settings), host=getattr(args, "host", settings.host), port=getattr(args, "port", settings.port))


if __name__ == "__main__":
    main()

