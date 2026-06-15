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
    def search_items(query: str, limit: int = 20):
        """Search kept StarRupture resource items."""
        return service.search_items(query, limit=limit)

    @mcp.tool()
    def get_item_detail(item_id: str):
        """Return the full production/usage preview for a resource item."""
        try:
            return service.get_item_detail(item_id)
        except DataNotFoundError:
            return {"error": "item_not_found", "item_id": item_id}

    @mcp.tool()
    def refresh_dataset():
        """Manually refresh the local StarRupture dataset from the source site."""
        return service.refresh_dataset()

    @mcp.tool()
    def get_dataset_meta():
        """Return dataset counts and latest refresh status."""
        return service.get_meta()

    @mcp.tool()
    def list_corporations():
        """Return StarRupture corporation levels and unlock rewards."""
        return service.get_corporations()

    @mcp.tool()
    def get_corporation_detail(corporation_id: str):
        """Return one StarRupture corporation with all level rewards."""
        try:
            return service.get_corporation_detail(corporation_id)
        except DataNotFoundError:
            return {"error": "corporation_not_found", "corporation_id": corporation_id}

    return mcp.sse_app(mount_path="")
