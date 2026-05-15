#!/usr/bin/env python3
"""
Fetch exact Wikipedia pageviews by country using the differential-privacy dataset.
Downloads daily TSVs for a month and aggregates with DuckDB to get real integer counts.
"""
import json
import duckdb
import requests
from pathlib import Path
from bs4 import BeautifulSoup

# --- CONFIG ---
MONTH = "2024-09"
PROJECT = "en.wikipedia"
BASE_URL = "https://analytics.wikimedia.org/published/datasets/country_project_page"

print(f"Fetching exact Wikipedia pageviews for {PROJECT} ({MONTH})...")

# Create data directory
data_dir = Path(__file__).parent.parent / 'data'
data_dir.mkdir(exist_ok=True)

# 1) List all daily TSV files for the month
print(f"\n1. Listing daily TSV files from {BASE_URL}...")
response = requests.get(f"{BASE_URL}/", headers={"user-agent": "vaclavrozhon.github.io"})
response.raise_for_status()

soup = BeautifulSoup(response.text, 'html.parser')
tsv_files = []
for link in soup.find_all('a'):
    href = link.get('href', '')
    if href.startswith(MONTH) and href.endswith('.tsv'):
        tsv_files.append(href)

tsv_files.sort()
print(f"Found {len(tsv_files)} daily files for {MONTH}")

# 2) Download TSV files
print(f"\n2. Downloading TSV files to {data_dir}...")
for i, tsv_file in enumerate(tsv_files, 1):
    file_path = data_dir / tsv_file
    if file_path.exists():
        print(f"  [{i}/{len(tsv_files)}] {tsv_file} (cached)")
        continue

    print(f"  [{i}/{len(tsv_files)}] Downloading {tsv_file}...")
    tsv_response = requests.get(f"{BASE_URL}/{tsv_file}",
                                headers={"user-agent": "vaclavrozhon.github.io"})
    tsv_response.raise_for_status()

    with open(file_path, 'wb') as f:
        f.write(tsv_response.content)

# 3) Aggregate by country using DuckDB
print(f"\n3. Aggregating data with DuckDB for {PROJECT}...")
con = duckdb.connect(':memory:')

# Load all TSVs for the month
tsv_pattern = str(data_dir / f"{MONTH}-*.tsv")
con.execute(f"""
    CREATE TABLE pv AS
    SELECT * FROM read_csv_auto('{tsv_pattern}', delim='\t', header=TRUE)
""")

# Aggregate by country for the specific project
result = con.execute(f"""
    WITH enwiki AS (
        SELECT country, CAST(gbc AS BIGINT) AS views
        FROM pv
        WHERE project = '{PROJECT}'
    )
    SELECT
        country AS iso2,
        SUM(views) AS total_views
    FROM enwiki
    GROUP BY country
    ORDER BY total_views DESC
""").fetchall()

con.close()

# 4) Convert to dictionary and save
wiki_data = {row[0]: int(row[1]) for row in result}

output_dir = Path(__file__).parent.parent / 'dist'
output_dir.mkdir(exist_ok=True)
output_path = output_dir / 'wikipedia_country_pageviews.json'

with open(output_path, 'w') as f:
    json.dump(wiki_data, f, indent=2)

print(f"\n4. Top 10 countries (exact counts):")
for iso2, views in sorted(wiki_data.items(), key=lambda x: x[1], reverse=True)[:10]:
    print(f"  {iso2}: {views:>15,}")

print(f"\nSaved to: {output_path}")
print(f"Total countries: {len(wiki_data)}")
print(f"Total views: {sum(wiki_data.values()):,}")
print(f"\n✓ Exact integer counts (DP-noised, not bucketed)")
