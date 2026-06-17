from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
import textwrap
import urllib.error
import urllib.request
from dataclasses import dataclass
from pathlib import Path
from typing import Any


MAX_COMMITS = 80
MAX_PULL_REQUESTS = 30
MAX_BODY_CHARS = 1800


@dataclass(frozen=True)
class CommitInfo:
    sha: str
    author: str
    date: str
    subject: str


def run_git(args: list[str], *, check: bool = True) -> str:
    result = subprocess.run(
        ["git", *args],
        check=False,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        encoding="utf-8",
    )
    if check and result.returncode != 0:
        raise RuntimeError(f"git {' '.join(args)} failed: {result.stderr.strip()}")
    return result.stdout.strip()


def find_previous_tag(current_tag: str) -> str | None:
    tags_output = run_git(["tag", "--merged", "HEAD", "--sort=-creatordate"], check=False)
    tags = [tag.strip() for tag in tags_output.splitlines() if tag.strip()]
    for tag in tags:
        if tag != current_tag:
            return tag
    return None


def collect_commits(previous_tag: str | None) -> list[CommitInfo]:
    if previous_tag:
        revision_range = f"{previous_tag}..HEAD"
    else:
        revision_range = "HEAD"

    output = run_git(
        [
            "log",
            revision_range,
            f"--max-count={MAX_COMMITS}",
            "--date=short",
            "--pretty=format:%H%x1f%an%x1f%ad%x1f%s%x1e",
        ]
    )

    commits: list[CommitInfo] = []
    for record in output.split("\x1e"):
        record = record.strip()
        if not record:
            continue
        parts = record.split("\x1f")
        if len(parts) != 4:
            continue
        commits.append(CommitInfo(sha=parts[0], author=parts[1], date=parts[2], subject=parts[3]))
    return commits


def github_json(path: str, token: str) -> Any:
    repo = os.environ.get("GITHUB_REPOSITORY", "")
    if not repo:
        raise RuntimeError("GITHUB_REPOSITORY is not set.")

    url = f"https://api.github.com/repos/{repo}/{path.lstrip('/')}"
    request = urllib.request.Request(
        url,
        headers={
            "Accept": "application/vnd.github+json",
            "Authorization": f"Bearer {token}",
            "X-GitHub-Api-Version": "2022-11-28",
            "User-Agent": "StarRupturePlannerReleaseNotes",
        },
    )
    with urllib.request.urlopen(request, timeout=30) as response:
        return json.loads(response.read().decode("utf-8"))


def truncate(value: str | None, max_chars: int) -> str:
    if not value:
        return ""
    value = value.strip()
    if len(value) <= max_chars:
        return value
    return value[: max_chars - 20].rstrip() + "\n...[truncated]"


def collect_pull_requests(commits: list[CommitInfo], token: str) -> list[dict[str, Any]]:
    pull_requests: dict[int, dict[str, Any]] = {}
    for commit in commits:
        if len(pull_requests) >= MAX_PULL_REQUESTS:
            break
        try:
            associated = github_json(f"commits/{commit.sha}/pulls", token)
        except urllib.error.HTTPError as exc:
            raise RuntimeError(f"GitHub PR lookup failed for {commit.sha[:7]}: HTTP {exc.code}") from exc

        for pull_request in associated:
            number = int(pull_request["number"])
            if number in pull_requests:
                continue
            pull_requests[number] = {
                "number": number,
                "title": pull_request.get("title", ""),
                "url": pull_request.get("html_url", ""),
                "author": (pull_request.get("user") or {}).get("login", ""),
                "merged_at": pull_request.get("merged_at", ""),
                "body": truncate(pull_request.get("body"), MAX_BODY_CHARS),
            }
            if len(pull_requests) >= MAX_PULL_REQUESTS:
                break
    return list(pull_requests.values())


def build_context(tag: str, previous_tag: str | None, commits: list[CommitInfo], pull_requests: list[dict[str, Any]]) -> dict[str, Any]:
    return {
        "project": "StarRupture Planner",
        "tag": tag,
        "previous_tag": previous_tag,
        "download_assets": [
            "Windows installer: StarRupturePlanner-<tag>-win-x64-Setup.exe",
            "Manual extraction archive: StarRupturePlanner-<tag>-win-x64.zip",
            "SHA256 checksum files for downloads",
        ],
        "commits": [
            {
                "sha": commit.sha[:12],
                "author": commit.author,
                "date": commit.date,
                "subject": commit.subject,
            }
            for commit in commits
        ],
        "pull_requests": pull_requests,
    }


def call_ollama(context: dict[str, Any]) -> str:
    base_url = os.environ.get("OLLAMA_BASE_URL", "https://ollama.com").rstrip("/")
    model = os.environ.get("OLLAMA_MODEL", "gpt-oss:120b").strip()
    api_key = os.environ.get("OLLAMA_API_KEY", "").strip()

    if not base_url:
        raise RuntimeError("OLLAMA_BASE_URL is empty.")
    if not model:
        raise RuntimeError("OLLAMA_MODEL is empty.")
    if not api_key:
        raise RuntimeError("OLLAMA_API_KEY is required for Ollama Cloud release notes.")

    prompt = textwrap.dedent(
        f"""
        Write GitHub release notes in Markdown for this release.

        Requirements:
        - Use only the provided commits and pull request descriptions.
        - Do not invent features, fixes, dates, links, or contributors.
        - Mention that downloads include the Windows installer, the manual extraction zip, and SHA256 checksums.
        - Do not mention a portable self-extracting EXE.
        - Prefer sections named Highlights, Changes, Fixes, Packaging, and Downloads when relevant.
        - Keep it concise and useful for users.

        Release context JSON:
        {json.dumps(context, ensure_ascii=False, indent=2)}
        """
    ).strip()

    payload = {
        "model": model,
        "stream": False,
        "options": {"temperature": 0.2},
        "messages": [
            {
                "role": "system",
                "content": "You write accurate, concise GitHub release notes from repository metadata.",
            },
            {"role": "user", "content": prompt},
        ],
    }

    headers = {"Content-Type": "application/json", "Authorization": f"Bearer {api_key}"}
    chat_endpoint = f"{base_url}/chat" if base_url.endswith("/api") else f"{base_url}/api/chat"

    request = urllib.request.Request(
        chat_endpoint,
        data=json.dumps(payload).encode("utf-8"),
        headers=headers,
        method="POST",
    )

    try:
        with urllib.request.urlopen(request, timeout=120) as response:
            body = json.loads(response.read().decode("utf-8"))
    except urllib.error.HTTPError as exc:
        detail = exc.read().decode("utf-8", errors="replace")
        raise RuntimeError(f"Ollama request failed: HTTP {exc.code}: {detail}") from exc
    except urllib.error.URLError as exc:
        raise RuntimeError(f"Ollama request failed: {exc.reason}") from exc

    content = ((body.get("message") or {}).get("content") or "").strip()
    if not content:
        raise RuntimeError("Ollama returned an empty release note.")
    return content


def main() -> int:
    parser = argparse.ArgumentParser(description="Generate GitHub release notes with Ollama.")
    parser.add_argument("--tag", required=True, help="Release tag, for example v0.4.0-alpha.")
    parser.add_argument("--output", required=True, help="Markdown file to overwrite.")
    args = parser.parse_args()

    token = os.environ.get("GITHUB_TOKEN", "").strip()
    if not token:
        raise RuntimeError("GITHUB_TOKEN is required to collect pull request descriptions.")

    previous_tag = find_previous_tag(args.tag)
    commits = collect_commits(previous_tag)
    pull_requests = collect_pull_requests(commits, token)
    context = build_context(args.tag, previous_tag, commits, pull_requests)
    notes = call_ollama(context)

    output_path = Path(args.output)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(notes + "\n", encoding="utf-8")
    print(f"Wrote Ollama release notes to {output_path}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(f"release notes generation failed: {exc}", file=sys.stderr)
        raise SystemExit(1)
