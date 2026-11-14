"""Add (or replace) one version in the Jellyfin plugin catalog file manifest.json."""
import argparse
import datetime
import json

p = argparse.ArgumentParser()
p.add_argument("manifest")
p.add_argument("--version", required=True)
p.add_argument("--abi", required=True)
p.add_argument("--jellyfin", required=True)
p.add_argument("--url", required=True)
p.add_argument("--checksum", required=True)
a = p.parse_args()

with open(a.manifest, encoding="utf-8") as f:
    manifest = json.load(f)

plugin = manifest[0]
versions = [v for v in plugin.get("versions", []) if v.get("version") != a.version]
versions.insert(0, {
    "version": a.version,
    "changelog": f"Built for Jellyfin {a.jellyfin}.",
    "targetAbi": a.abi,
    "sourceUrl": a.url,
    "checksum": a.checksum,
    "timestamp": datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
})
plugin["versions"] = versions

with open(a.manifest, "w", encoding="utf-8") as f:
    json.dump(manifest, f, indent=2)
    f.write("\n")
print(f"manifest: {a.version} (Jellyfin {a.jellyfin})")
