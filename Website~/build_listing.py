#!/usr/bin/env python3
"""Generate a VPM listing (index.json) from this repository's GitHub releases.

Every release published by .github/workflows/release.yml attaches two assets:
  - <package>-<version>.zip   the package itself
  - package.json              the manifest for that version

This script reads each release's package.json asset, rewrites its "url" to point
at that release's zip, and collects them all into the VPM repository format:

    { "name", "id", "url", "author", "packages": { "<pkg>": { "versions": {...} } } }

It only needs the GitHub REST API, so it works from CI with the default
GITHUB_TOKEN and needs no third-party action.

Usage:
    python build_listing.py --repo owner/name --out dist
"""

import argparse
import json
import os
import sys
import urllib.error
import urllib.request

LISTING_NAME = "Kawaii Studio"
LISTING_ID = "com.kawaiistudio.listing"
LISTING_AUTHOR = "Kawaii Studio"
LISTING_DESCRIPTION = "Kawaii Studio Unity tools for VRChat creators."


def api_get(url, token):
    request = urllib.request.Request(url)
    request.add_header("Accept", "application/vnd.github+json")
    if token:
        request.add_header("Authorization", f"Bearer {token}")
    with urllib.request.urlopen(request) as response:
        return json.loads(response.read().decode("utf-8"))


def download_json(url, token):
    request = urllib.request.Request(url)
    # Release assets need the octet-stream Accept header to return raw bytes.
    request.add_header("Accept", "application/octet-stream")
    if token:
        request.add_header("Authorization", f"Bearer {token}")
    with urllib.request.urlopen(request) as response:
        return json.loads(response.read().decode("utf-8"))


def collect_releases(repo, token):
    releases = []
    page = 1
    while True:
        batch = api_get(
            f"https://api.github.com/repos/{repo}/releases?per_page=100&page={page}",
            token,
        )
        if not batch:
            break
        releases.extend(batch)
        page += 1
    return releases


def build(repo, listing_url, token):
    packages = {}

    for release in collect_releases(repo, token):
        if release.get("draft"):
            continue

        assets = {asset["name"]: asset for asset in release.get("assets", [])}
        manifest_asset = assets.get("package.json")
        zip_asset = next(
            (a for a in release.get("assets", []) if a["name"].endswith(".zip")), None
        )

        if manifest_asset is None or zip_asset is None:
            print(f"  skip '{release.get('tag_name')}': missing package.json or zip asset")
            continue

        try:
            manifest = download_json(manifest_asset["url"], token)
        except (urllib.error.URLError, json.JSONDecodeError) as exc:
            print(f"  skip '{release.get('tag_name')}': cannot read package.json ({exc})")
            continue

        name = manifest.get("name")
        version = manifest.get("version")
        if not name or not version:
            print(f"  skip '{release.get('tag_name')}': package.json has no name/version")
            continue

        # Point the manifest at the actual downloadable asset for this release.
        manifest["url"] = zip_asset["browser_download_url"]

        packages.setdefault(name, {"versions": {}})["versions"][version] = manifest
        print(f"  + {name} {version}")

    return {
        "name": LISTING_NAME,
        "id": LISTING_ID,
        "url": listing_url,
        "author": LISTING_AUTHOR,
        "description": LISTING_DESCRIPTION,
        "packages": packages,
    }


INDEX_HTML = """<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Kawaii Studio - VPM Listing</title>
<style>
  body {{ font-family: system-ui, sans-serif; max-width: 40rem; margin: 4rem auto; padding: 0 1rem;
         background: #16121c; color: #efeaf5; line-height: 1.6; }}
  a.button {{ display: inline-block; background: #7c3aed; color: #fff; padding: .75rem 1.25rem;
              border-radius: .5rem; text-decoration: none; font-weight: 600; }}
  code {{ background: #241d2e; padding: .15rem .4rem; border-radius: .25rem; }}
</style>
</head>
<body>
<h1>Kawaii Studio</h1>
<p>Unity editor tools for VRChat creators.</p>
<p><a class="button" href="vcc://vpm/addRepo?url={listing_url}">Add to VRChat Creator Companion</a></p>
<p>Or add this URL manually in VCC &rarr; Settings &rarr; Packages &rarr; Add Repository:</p>
<p><code>{listing_url}</code></p>
</body>
</html>
"""


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo", required=True, help="owner/name")
    parser.add_argument("--out", default="dist")
    parser.add_argument("--listing-url", default=None)
    args = parser.parse_args()

    owner, name = args.repo.split("/", 1)
    listing_url = args.listing_url or f"https://{owner}.github.io/{name}/index.json"
    token = os.environ.get("GITHUB_TOKEN")

    print(f"Building listing for {args.repo} -> {listing_url}")
    listing = build(args.repo, listing_url, token)

    total = sum(len(p["versions"]) for p in listing["packages"].values())
    if total == 0:
        print("::warning::No package versions found; publishing an empty listing.")

    os.makedirs(args.out, exist_ok=True)
    with open(os.path.join(args.out, "index.json"), "w", encoding="utf-8") as handle:
        json.dump(listing, handle, indent=2)
    with open(os.path.join(args.out, "index.html"), "w", encoding="utf-8") as handle:
        handle.write(INDEX_HTML.format(listing_url=listing_url))

    print(f"Wrote {args.out}/index.json with {total} version(s).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
