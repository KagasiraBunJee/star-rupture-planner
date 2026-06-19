from __future__ import annotations

import argparse
import json
import os
import re
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
MAX_LINEAR_ISSUES = 40
MAX_LINEAR_DESCRIPTION_CHARS = 2400
LINEAR_ISSUE_PATTERN = re.compile(r"\b[A-Z][A-Z0-9]+-\d+\b")
NON_APP_EXACT_PATHS = (
    "agents.md",
)
NON_APP_PATH_PREFIXES = (
    ".github/",
    ".vscode/",
    "scripts/",
)
NON_APP_PATH_PARTS = (
    "/workflows/",
)
NON_APP_SUBJECT_KEYWORDS = (
    "agent instructions",
    "agents.md",
    "workflow",
    "github action",
    "ci",
    "release automation",
    "release notes",
)


@dataclass(frozen=True)
class CommitInfo:
    sha: str
    author: str
    date: str
    subject: str
    paths: tuple[str, ...]


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
        paths_output = run_git(["diff-tree", "--no-commit-id", "--name-only", "-r", parts[0]], check=False)
        paths = tuple(path.strip().replace("\\", "/") for path in paths_output.splitlines() if path.strip())
        commits.append(CommitInfo(sha=parts[0], author=parts[1], date=parts[2], subject=parts[3], paths=paths))
    return commits


def is_non_app_path(path: str) -> bool:
    normalized = path.strip().replace("\\", "/").lower()
    return (
        normalized in NON_APP_EXACT_PATHS
        or normalized.startswith(NON_APP_PATH_PREFIXES)
        or any(part in normalized for part in NON_APP_PATH_PARTS)
    )


def is_non_app_subject(subject: str) -> bool:
    normalized = subject.strip().lower()
    return any(keyword in normalized for keyword in NON_APP_SUBJECT_KEYWORDS)


def is_app_commit(commit: CommitInfo) -> bool:
    if commit.paths and all(is_non_app_path(path) for path in commit.paths):
        return False
    if is_non_app_subject(commit.subject) and not commit.paths:
        return False
    return True


def filter_app_commits(commits: list[CommitInfo]) -> list[CommitInfo]:
    return [commit for commit in commits if is_app_commit(commit)]


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


def linear_graphql(query: str, variables: dict[str, Any], api_key: str) -> dict[str, Any]:
    request = urllib.request.Request(
        "https://api.linear.app/graphql",
        data=json.dumps({"query": query, "variables": variables}).encode("utf-8"),
        headers={
            "Authorization": api_key,
            "Content-Type": "application/json",
            "User-Agent": "StarRupturePlannerReleaseNotes",
        },
        method="POST",
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
                "_head_ref": ((pull_request.get("head") or {}).get("ref") or ""),
            }
            if len(pull_requests) >= MAX_PULL_REQUESTS:
                break
    return list(pull_requests.values())


def filter_app_pull_requests(pull_requests: list[dict[str, Any]], token: str) -> list[dict[str, Any]]:
    app_pull_requests: list[dict[str, Any]] = []
    for pull_request in pull_requests:
        number = pull_request["number"]
        files = github_json(f"pulls/{number}/files?per_page=100", token)
        paths = tuple(file_info.get("filename", "") for file_info in files)
        title = pull_request.get("title", "")
        body = pull_request.get("body", "")
        if paths and all(is_non_app_path(path) for path in paths):
            continue
        if is_non_app_subject(title) and not body.strip():
            continue
        app_pull_requests.append(pull_request)
    return app_pull_requests


def extract_linear_issue_ids(commits: list[CommitInfo], pull_requests: list[dict[str, Any]]) -> list[str]:
    issue_ids: set[str] = set()

    for commit in commits:
        for match in LINEAR_ISSUE_PATTERN.findall(commit.subject.upper()):
            issue_ids.add(match)

    for pull_request in pull_requests:
        for field in ("title", "body", "_head_ref"):
            value = str(pull_request.get(field) or "").upper()
            for match in LINEAR_ISSUE_PATTERN.findall(value):
                issue_ids.add(match)

    return sorted(issue_ids)[:MAX_LINEAR_ISSUES]


def is_non_app_linear_issue(issue: dict[str, Any]) -> bool:
    searchable = f"{issue.get('title', '')}\n{issue.get('description', '')}"
    return is_non_app_subject(searchable)


def collect_linear_issues(issue_ids: list[str]) -> list[dict[str, Any]]:
    api_key = os.environ.get("LINEAR_API_KEY", "").strip()
    if not api_key or not issue_ids:
        return []

    query = """
    query ReleaseIssue($id: String!) {
      issue(id: $id) {
        identifier
        title
        description
        url
        state {
          name
          type
        }
        labels {
          nodes {
            name
          }
        }
        project {
          name
        }
        projectMilestone {
          name
        }
      }
    }
    """

    issues: list[dict[str, Any]] = []
    for issue_id in issue_ids:
        try:
            body = linear_graphql(query, {"id": issue_id}, api_key)
        except (urllib.error.HTTPError, urllib.error.URLError, TimeoutError) as exc:
            print(f"warning: Linear lookup skipped for {issue_id}: {exc}", file=sys.stderr)
            continue

        if body.get("errors"):
            print(f"warning: Linear lookup skipped for {issue_id}: {body['errors']}", file=sys.stderr)
            continue

        issue = (body.get("data") or {}).get("issue")
        if not issue:
            continue

        normalized = {
            "id": issue.get("identifier", issue_id),
            "title": issue.get("title", ""),
            "description": truncate(issue.get("description"), MAX_LINEAR_DESCRIPTION_CHARS),
            "url": issue.get("url", ""),
            "state": (issue.get("state") or {}).get("name", ""),
            "labels": [node.get("name", "") for node in ((issue.get("labels") or {}).get("nodes") or []) if node.get("name")],
            "project": (issue.get("project") or {}).get("name", ""),
            "milestone": (issue.get("projectMilestone") or {}).get("name", ""),
        }
        if is_non_app_linear_issue(normalized):
            continue
        issues.append(normalized)

    return issues


def build_context(
    tag: str,
    previous_tag: str | None,
    commits: list[CommitInfo],
    pull_requests: list[dict[str, Any]],
    linear_issues: list[dict[str, Any]],
) -> dict[str, Any]:
    return {
        "project": "StarRupture Planner",
        "tag": tag,
        "previous_tag": previous_tag,
        "download_assets": [
            "Windows installer: StarRupturePlanner-<tag>-win-x64-Installer.exe",
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
        "pull_requests": [
            {key: value for key, value in pull_request.items() if not key.startswith("_")}
            for pull_request in pull_requests
        ],
        "linear_issues": linear_issues,
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
        - Write only about user-visible app, data, planner, local API, localization, installer, or packaging outcomes.
        - Use only the provided app commits, pull request descriptions, and Linear issue descriptions.
        - Prefer Linear issue descriptions for intent and acceptance details when they are available.
        - Do not invent features, fixes, dates, links, or contributors.
        - Ignore CI, GitHub Actions, release automation, workflow, agent instructions, secret, script, and repository maintenance details.
        - Never mention changes that only affect AGENTS.md, automated workflows, Linear process rules, branch naming, or release-note generation.
        - Do not mention implementation details, source file names, class names, view names, resource dictionary names, JSON/XAML file names, refactors, splits, or internal architecture.
        - Convert internal wording into high-level user outcomes.
        - Bad: "CanvasView was refactored"; good: "Canvas navigation and selection feel smoother."
        - Bad: "added Strings.de.xaml"; good: "Added German localization."
        - Bad: "updated LightTheme.xaml and ControlStyles.xaml"; good: "Improved light theme readability and control styling."
        - Bad: "AlertsBarView was added"; good: "Alerts are easier to scan and can focus the affected machine."
        - If the provided context has no user-visible app changes, say that this release has no user-facing app changes.
        - Mention that downloads include the Windows installer, the manual extraction zip, and SHA256 checksums.
        - Do not mention a portable self-extracting EXE.
        - Prefer sections named Highlights, Improvements, Fixes, Packaging, and Downloads when relevant.
        - Keep Changes/Improvements bullets high-level; do not create a section that lists internal development tasks.
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
                "content": "You write accurate, concise user-facing release notes for a desktop app. You never expose implementation details unless they directly affect users.",
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
    app_commits = filter_app_commits(commits)
    pull_requests = collect_pull_requests(app_commits, token)
    app_pull_requests = filter_app_pull_requests(pull_requests, token)
    linear_issue_ids = extract_linear_issue_ids(app_commits, app_pull_requests)
    linear_issues = collect_linear_issues(linear_issue_ids)
    context = build_context(args.tag, previous_tag, app_commits, app_pull_requests, linear_issues)
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
