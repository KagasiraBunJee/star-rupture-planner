from __future__ import annotations

import json
from pathlib import Path
from typing import Any

from starlette.applications import Starlette
from starlette.background import BackgroundTask
from starlette.responses import FileResponse, JSONResponse, Response
from starlette.routing import Mount, Route
from starlette.staticfiles import StaticFiles

from .config import Settings, settings
from .mcp_app import create_mcp_app
from .service import DataNotFoundError, ResourceService


service = ResourceService(settings)


def json_response(payload: Any, status_code: int = 200) -> JSONResponse:
    return JSONResponse(payload, status_code=status_code)


async def get_meta(request) -> Response:
    return json_response(service.get_meta())


async def list_items(request) -> Response:
    params = request.query_params
    produced = _optional_bool(params.get("produced"))
    used = _optional_bool(params.get("used"))
    payload = service.list_items(
        q=params.get("q"),
        produced=produced,
        used=used,
        limit=int(params.get("limit", 100)),
        offset=int(params.get("offset", 0)),
        lang=params.get("lang"),
    )
    return json_response(payload)


async def get_item(request) -> Response:
    try:
        return json_response(service.get_item_detail(request.path_params["item_id"], lang=request.query_params.get("lang")))
    except DataNotFoundError:
        return json_response({"error": "item_not_found"}, status_code=404)


async def list_buildings(request) -> Response:
    return json_response(service.list_buildings(lang=request.query_params.get("lang")))


async def list_corporations(request) -> Response:
    return json_response(service.get_corporations(lang=request.query_params.get("lang")))


async def get_corporation(request) -> Response:
    try:
        return json_response(service.get_corporation_detail(
            request.path_params["corporation_id"],
            lang=request.query_params.get("lang"),
        ))
    except DataNotFoundError:
        return json_response({"error": "corporation_not_found"}, status_code=404)


async def get_planner_catalog(request) -> Response:
    return json_response(service.get_planner_catalog(lang=request.query_params.get("lang")))


async def get_planner_suggestions(request) -> Response:
    params = request.query_params
    item_id = params.get("item_id")
    direction = params.get("direction")
    if not item_id or not direction:
        return json_response(
            {"error": "direction and item_id query parameters are required"},
            status_code=400,
        )
    payload = service.get_planner_suggestions(direction=direction, item_id=item_id, lang=params.get("lang"))
    if "error" in payload:
        return json_response(payload, status_code=400)
    return json_response(payload)


async def get_transport_tiers(request) -> Response:
    return json_response(service.get_transport_tiers(lang=request.query_params.get("lang")))


async def refresh_dataset(request) -> Response:
    # This is intentionally synchronous for v1 so callers receive the completed summary.
    try:
        return json_response(service.refresh_dataset())
    except Exception as exc:
        return json_response({"status": "failed", "error": str(exc)}, status_code=500)


def _optional_bool(value: str | None) -> bool | None:
    if value is None:
        return None
    return value.lower() in {"1", "true", "yes", "y", "on"}


def create_app(cfg: Settings = settings) -> Starlette:
    cfg.data_dir.mkdir(parents=True, exist_ok=True)
    cfg.item_asset_dir.mkdir(parents=True, exist_ok=True)
    cfg.building_asset_dir.mkdir(parents=True, exist_ok=True)
    routes = [
        Route("/api/meta", get_meta, methods=["GET"]),
        Route("/api/items", list_items, methods=["GET"]),
        Route("/api/items/{item_id}", get_item, methods=["GET"]),
        Route("/api/buildings", list_buildings, methods=["GET"]),
        Route("/api/corporations", list_corporations, methods=["GET"]),
        Route("/api/corporations/{corporation_id}", get_corporation, methods=["GET"]),
        Route("/api/planner/catalog", get_planner_catalog, methods=["GET"]),
        Route("/api/planner/suggestions", get_planner_suggestions, methods=["GET"]),
        Route("/api/planner/transport-tiers", get_transport_tiers, methods=["GET"]),
        Route("/api/admin/refresh", refresh_dataset, methods=["POST"]),
        Mount("/assets/items", StaticFiles(directory=str(cfg.item_asset_dir)), name="item-assets"),
        Mount(
            "/assets/buildings",
            StaticFiles(directory=str(cfg.building_asset_dir)),
            name="building-assets",
        ),
        Mount("/", create_mcp_app(service), name="mcp"),
    ]
    return Starlette(debug=False, routes=routes)


app = create_app(settings)
