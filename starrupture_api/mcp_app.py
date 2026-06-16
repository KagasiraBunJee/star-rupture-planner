from __future__ import annotations

from mcp.server.fastmcp import FastMCP

from .service import DataNotFoundError, ResourceService


def create_mcp_app(service: ResourceService):
    mcp = FastMCP(
        "StarRupture Resource Data",
        instructions=(
            "Search StarRupture resource items and inspect production recipes, "
            "unlock requirements, rates, and reverse usages."
        ),
        mount_path="",
        sse_path="/mcp/sse",
        message_path="/mcp/messages/",
    )

    @mcp.tool()
    def search_items(query: str, limit: int = 20, language: str = "en"):
        """Search kept StarRupture resource items."""
        return service.search_items(query, limit=limit, lang=language)

    @mcp.tool()
    def get_item_detail(item_id: str, language: str = "en"):
        """Return the full production/usage preview for a resource item."""
        try:
            return service.get_item_detail(item_id, lang=language)
        except DataNotFoundError:
            return {"error": "item_not_found", "item_id": item_id}

    @mcp.tool()
    def get_dataset_meta():
        """Return local dataset counts and supported languages."""
        return service.get_meta()

    @mcp.tool()
    def list_corporations(language: str = "en"):
        """Return StarRupture corporation levels and unlock rewards."""
        return service.get_corporations(lang=language)

    @mcp.tool()
    def get_corporation_detail(corporation_id: str, language: str = "en"):
        """Return one StarRupture corporation with all level rewards."""
        try:
            return service.get_corporation_detail(corporation_id, lang=language)
        except DataNotFoundError:
            return {"error": "corporation_not_found", "corporation_id": corporation_id}

    return mcp.sse_app(mount_path="")
