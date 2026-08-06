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
import hashlib
import json
import os
import sys
import urllib.error
import urllib.parse
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


def sha256_of(url, token):
    """SHA-256 of a release asset, streamed so a large zip never lands in memory."""
    request = urllib.request.Request(url)
    request.add_header("Accept", "application/octet-stream")
    if token:
        request.add_header("Authorization", f"Bearer {token}")
    digest = hashlib.sha256()
    with urllib.request.urlopen(request) as response:
        for chunk in iter(lambda: response.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


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

        # zipSHA256 is what VCC uses to invalidate its cached copy of a package
        # ("Currently, the zipSHA256 property is only used for cache invalidation"
        # -- vcc.docs.vrchat.com/vpm/packages). Without it, a user who already pulled a
        # version can keep being served the stale zip. It belongs in the LISTING entry,
        # not in the in-repo package.json.
        try:
            manifest["zipSHA256"] = sha256_of(zip_asset["url"], token)
        except (urllib.error.URLError, OSError) as exc:
            print(f"  !! {name} {version}: cannot hash zip ({exc}); shipping without zipSHA256")

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


# The vcc:// URL is percent-encoded exactly like VRChat's own listing template does it
# (its Website/app.js calls encodeURIComponent on the listing URL). Passing the raw URL
# leaves an unescaped "://" and "/" sitting inside a query parameter.
#
# This page exists because GitHub strips custom URL schemes from README markdown: a vcc://
# link there renders as plain text with no anchor at all (verified against GitHub's own
# markdown API). So the README links here, and the real one-click button lives on this page.
INDEX_HTML = """<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>KS Unity Tools &mdash; Install</title>
<meta name="description" content="Add KS Unity Tools to the VRChat Creator Companion in one click.">
<style>
  * {{ box-sizing: border-box; }}
  body {{ font-family: system-ui, -apple-system, "Segoe UI", sans-serif; max-width: 46rem;
         margin: 0 auto; padding: 3rem 1.25rem 4rem; background: #16121c; color: #efeaf5;
         line-height: 1.65; }}
  h1 {{ font-size: 2.1rem; margin: 0 0 .25rem; letter-spacing: -.02em; }}
  .sub {{ color: #b9a9d4; margin: 0 0 2.25rem; }}
  .card {{ background: #1e1828; border: 1px solid #33294a; border-radius: .85rem;
           padding: 1.5rem; margin: 0 0 1.15rem; }}
  .card h2 {{ font-size: 1.02rem; margin: 0 0 .3rem; text-transform: uppercase;
              letter-spacing: .08em; color: #c9b8e8; }}
  .card p {{ margin: .35rem 0 1rem; color: #cfc3e4; font-size: .95rem; }}
  a.button {{ display: inline-block; padding: .8rem 1.4rem; border-radius: .55rem;
              text-decoration: none; font-weight: 700; letter-spacing: .02em; }}
  a.primary {{ background: linear-gradient(135deg,#8b46f0,#c05cf5); color: #fff;
               box-shadow: 0 6px 20px rgba(140,70,240,.32); }}
  a.secondary {{ background: #2b2338; color: #efeaf5; border: 1px solid #443359; }}
  code {{ background: #120f18; border: 1px solid #33294a; padding: .55rem .7rem;
          border-radius: .4rem; display: block; word-break: break-all; font-size: .86rem;
          color: #d9c9f5; margin-top: .5rem; }}
  ol {{ margin: .5rem 0 0; padding-left: 1.15rem; color: #cfc3e4; font-size: .95rem; }}
  footer {{ margin-top: 2.5rem; color: #8b7ba8; font-size: .85rem; }}
  a.plain {{ color: #c05cf5; }}
</style>
</head>
<body>
<h1>KS Unity Tools</h1>
<p class="sub">Unity editor tools for VRChat creators &mdash; Kawaii Studio.</p>

<div class="card">
  <h2>Creator Companion &mdash; recommended</h2>
  <p>One click. VCC opens, adds the listing, and every future release shows up as an update automatically.</p>
  <p><a class="button primary" href="vcc://vpm/addRepo?url={listing_url_enc}">Add to VRChat Creator Companion</a></p>
  <p style="margin-top:1.15rem">Prefer to add it by hand? In VCC go to <em>Settings &rarr; Packages &rarr; Add Repository</em> and paste:</p>
  <code>{listing_url}</code>
</div>

<div class="card">
  <h2>No VCC? Use the .unitypackage</h2>
  <p>Double-click it with your Unity project open. Everything installs under <em>Assets/Kawaii Studio</em>; the VRChat tools switch on by themselves when the SDK is present.</p>
  <p><a class="button secondary" href="{unitypackage_url}">Download .unitypackage</a></p>
</div>

<footer>
  <a class="plain" href="{repo_url}">View on GitHub</a> &middot;
  <a class="plain" href="{listing_url}">index.json</a>
</footer>
</body>
</html>
"""


# One-click bounce page. The README button can't link to vcc:// directly -- GitHub
# strips custom URL schemes from markdown -- so it links here instead, and this page
# fires the vcc:// hand-off itself the moment it loads. That makes it a single click from
# GitHub to "VCC is opening", with a visible fallback button in case the browser wants an
# explicit gesture before launching an external protocol.
ADD_HTML = """<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Opening VRChat Creator Companion…</title>
<style>
  body {{ font-family: system-ui, -apple-system, "Segoe UI", sans-serif; background: #16121c;
         color: #efeaf5; display: flex; min-height: 100vh; margin: 0; align-items: center;
         justify-content: center; text-align: center; }}
  .card {{ max-width: 30rem; padding: 2rem 1.5rem; }}
  h1 {{ font-size: 1.4rem; margin: 0 0 .75rem; }}
  p {{ color: #c5b9dc; line-height: 1.6; margin: .4rem 0 1.5rem; }}
  a.button {{ display: inline-block; padding: .85rem 1.5rem; border-radius: .6rem;
              text-decoration: none; font-weight: 700; background: linear-gradient(135deg,#8b46f0,#c05cf5);
              color: #fff; box-shadow: 0 6px 20px rgba(140,70,240,.32); }}
  a.plain {{ color: #c05cf5; display: inline-block; margin-top: 1.25rem; font-size: .9rem; }}
</style>
</head>
<body>
  <div class="card">
    <h1>Opening VRChat Creator Companion…</h1>
    <p>Your browser should be handing off to VCC now. If nothing happened, click the button.</p>
    <a class="button" id="go" href="vcc://vpm/addRepo?url={listing_url_enc}">Add to VRChat Creator Companion</a>
    <div><a class="plain" href="./">More install options</a></div>
  </div>
  <script>
    // Fire the hand-off on load. The click that brought the user here counts as the
    // user gesture most browsers require before launching an external protocol.
    setTimeout(function () {{ window.location.href = document.getElementById("go").href; }}, 300);
  </script>
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
    listing_url_enc = urllib.parse.quote(listing_url, safe="")
    with open(os.path.join(args.out, "index.html"), "w", encoding="utf-8") as handle:
        handle.write(INDEX_HTML.format(
            listing_url=listing_url,
            listing_url_enc=listing_url_enc,
            repo_url=f"https://github.com/{args.repo}",
            unitypackage_url=(f"https://github.com/{args.repo}/releases/latest/download/"
                              "KSUnityTools.unitypackage"),
        ))
    # One-click bounce page for the README's "Add to VCC" button.
    with open(os.path.join(args.out, "add.html"), "w", encoding="utf-8") as handle:
        handle.write(ADD_HTML.format(listing_url_enc=listing_url_enc))

    print(f"Wrote {args.out}/index.json with {total} version(s).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
