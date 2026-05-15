#!/usr/bin/env python3
"""
Fetch Wikipedia pageviews by country from the differential-privacy dataset.
Aggregates data for the most recent complete month available.
"""
import csv, io, requests, pandas as pd
from datetime import date, timedelta
from pathlib import Path

# --- CONFIG ---
# Fetch data for September 2024 (most recent complete month with likely complete data)
MONTH = "2024-09"
BASE = "https://analytics.wikimedia.org/published/datasets/country_project_page"
ONLY_WIKIPEDIA = True

print(f"Fetching Wikipedia pageviews by country for {MONTH}...")

# Generate list of days for the month
y, m = map(int, MONTH.split("-"))
d0 = date(y, m, 1)
d1 = date(y + (m == 12), (m % 12) + 1, 1)
days = [(d0 + timedelta(n)).isoformat() for n in range((d1 - d0).days)]

print(f"Downloading {len(days)} daily files...")

dfs = []
for i, day in enumerate(days):
    url = f"{BASE}/{day}.tsv"
    print(f"  [{i+1}/{len(days)}] {day}...", end=" ", flush=True)

    try:
        r = requests.get(url, headers={"user-agent": "vaclavrozhon.github.io", "accept": "text/tab-separated-values"})
        r.raise_for_status()

        # Parse TSV
        df = pd.read_csv(io.StringIO(r.text), sep="\t", dtype=str)

        # Normalize column name for count (historical uses private_count; current uses gbc)
        cnt_col = "gbc" if "gbc" in df.columns else "private_count"
        df[cnt_col] = pd.to_numeric(df[cnt_col], errors="coerce").fillna(0)

        # Filter to Wikipedia projects only
        if ONLY_WIKIPEDIA:
            df = df[df["project"].str.endswith(".wikipedia", na=False)]

        dfs.append(df[["country", cnt_col]])
        print("✓")
    except Exception as e:
        print(f"✗ ({e})")
        continue

if not dfs:
    print("Error: No data files could be downloaded")
    exit(1)

print(f"\nAggregating data from {len(dfs)} files...")

# Concatenate all dataframes
full = pd.concat(dfs, ignore_index=True)

# Group by country and sum
by_country = full.groupby("country", as_index=False).sum()
by_country.rename(columns={by_country.columns[1]: "views"}, inplace=True)
by_country = by_country.sort_values("views", ascending=False)

# Calculate shares
total = by_country["views"].sum()
by_country["share"] = by_country["views"] / total

print(f"\nTotal pageviews: {total:,.0f}")
print(f"Countries with data: {len(by_country)}")
print(f"\nTop 10 countries:")
for i, row in by_country.head(10).iterrows():
    print(f"  {row['country']:3s}: {row['views']:>15,.0f} ({row['share']*100:5.2f}%)")

# Save to dist directory
output_dir = Path(__file__).parent.parent / 'dist'
output_path = output_dir / 'wikipedia_country_pageviews.json'

# Convert to JSON format (similar to other data files)
wiki_data = {row['country']: int(row['views']) for _, row in by_country.iterrows()}

import json
with open(output_path, 'w') as f:
    json.dump(wiki_data, f, indent=2)

print(f"\nSaved to: {output_path}")
print(f"Total countries: {len(wiki_data)}")
