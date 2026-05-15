#!/usr/bin/env python3
"""
Fetch Wikipedia pageviews by country using the simple AQS API.
Gets pre-aggregated monthly totals (much faster than the DP dataset).
"""
import math, json, requests
from pathlib import Path

# --- CONFIG ---
PROJECT = "en.wikipedia.org"   # English Wikipedia
ACCESS  = "all-access"
YEAR    = 2024
MONTH   = 9  # September 2024

print(f"Fetching Wikipedia pageviews by country for {PROJECT} ({YEAR}-{MONTH:02d})...")

url = f"https://wikimedia.org/api/rest_v1/metrics/pageviews/top-by-country/{PROJECT}/{ACCESS}/{YEAR}/{MONTH:02d}"
headers = {"accept": "application/json", "user-agent": "vaclavrozhon.github.io"}

r = requests.get(url, headers=headers)
r.raise_for_status()

items = r.json()["items"][0]["countries"]

print(f"Received data for {len(items)} countries")

def bucket_midpoint(s):
    """Convert view bucket (e.g. '1000000-9999999') to estimated midpoint"""
    lo, hi = [int(x) for x in s.split("-")]
    # geometric mean for buckets spanning orders of magnitude
    return int(math.sqrt(lo * hi))

# Convert to simple dict: ISO code -> estimated views
wiki_data = {}
for c in items:
    iso = c["country"]
    views = bucket_midpoint(c["views"])
    wiki_data[iso] = views

# Save to dist directory
output_dir = Path(__file__).parent.parent / 'dist'
output_path = output_dir / 'wikipedia_country_pageviews.json'

with open(output_path, 'w') as f:
    json.dump(wiki_data, f, indent=2)

print(f"\nTop 10 countries:")
sorted_countries = sorted(wiki_data.items(), key=lambda x: x[1], reverse=True)
for iso, views in sorted_countries[:10]:
    print(f"  {iso}: {views:>15,}")

print(f"\nSaved to: {output_path}")
print(f"Total countries: {len(wiki_data)}")
print(f"\nNote: Values are estimated from buckets (not exact counts)")
