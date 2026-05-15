#!/usr/bin/env python3
"""
Convert Wikipedia CSV data to JSON format
"""
import csv
import json
from pathlib import Path

# Read the CSV from data directory
csv_path = Path(__file__).parent.parent / 'data' / 'wikipedia_country_pageviews.csv'
output_dir = Path(__file__).parent.parent / 'dist'
output_dir.mkdir(exist_ok=True)
output_path = output_dir / 'wikipedia_country_pageviews.json'

wiki_data = {}

with open(csv_path, 'r') as f:
    reader = csv.DictReader(f)
    for row in reader:
        iso_code = row['country']
        # Skip the unknown country code
        if iso_code == '--':
            continue
        views = int(row['total.total'])
        wiki_data[iso_code] = views

# Save to JSON
with open(output_path, 'w') as f:
    json.dump(wiki_data, f, indent=2)

print(f"Converted {len(wiki_data)} countries")
print(f"\nTop 10 countries:")
for iso, views in sorted(wiki_data.items(), key=lambda x: x[1], reverse=True)[:10]:
    print(f"  {iso}: {views:>15,}")

print(f"\nSaved to: {output_path}")
print(f"Total views: {sum(wiki_data.values()):,}")
