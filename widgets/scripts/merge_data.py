#!/usr/bin/env python3
import json
import csv
from pathlib import Path

# Set paths relative to script location
base_dir = Path(__file__).parent.parent
dist_dir = base_dir / 'dist'
data_dir = base_dir / 'data'
public_dir = base_dir / 'public'

# Load all JSON files from dist
with open(dist_dir / 'country_uniques.json', 'r') as f:
    site_data = json.load(f)

with open(dist_dir / 'country_population.json', 'r') as f:
    population_data = json.load(f)

with open(dist_dir / 'country_flags.json', 'r') as f:
    flags_data = json.load(f)

with open(dist_dir / 'country_name_map.json', 'r') as f:
    name_map = json.load(f)

# Load Wikipedia pageviews if available
wikipedia_data = {}
wiki_path = dist_dir / 'wikipedia_country_pageviews.json'
if wiki_path.exists():
    with open(wiki_path, 'r') as f:
        wiki_raw = json.load(f)
        # Convert ISO codes to country names
        for iso, views in wiki_raw.items():
            country_name = name_map['iso2ToName'].get(iso, iso)
            wikipedia_data[country_name] = views

# Load GDP (PPP) data if available
gdp_ppp_data = {}
gdp_path = dist_dir / 'country_gdp_ppp.json'
if gdp_path.exists():
    with open(gdp_path, 'r') as f:
        gdp_ppp_data = json.load(f)

# Load YouTube CSV and convert ISO codes to country names
youtube_data = {}
iso2_to_name = name_map['iso2ToName']
csv_path = data_dir / 'Geography 2021-08-22_2025-10-17 Polylog' / 'Table data.csv'
with open(csv_path, 'r') as f:
    reader = csv.DictReader(f)
    for row in reader:
        geo = row['Geography']
        if geo != 'Total':
            # Convert ISO code to country name if it's a code
            country_name = iso2_to_name.get(geo, geo)
            youtube_data[country_name] = row['Views']

# Create reverse mapping: country name -> ISO code
name_to_iso = {}
for iso, name in iso2_to_name.items():
    name_to_iso[name] = iso

# Collect all unique country names
all_countries = set()
all_countries.update(site_data.keys())
all_countries.update(population_data.keys())
all_countries.update(youtube_data.keys())
all_countries.update(wikipedia_data.keys())

# Build the merged data
merged = []
for country in sorted(all_countries):
    # Get ISO code
    iso_code = name_to_iso.get(country, '')

    # Get flag
    flag = flags_data.get(iso_code, '') if iso_code else ''

    # Get data
    population = population_data.get(country, '')
    site_visits = site_data.get(country, '')
    youtube_views = youtube_data.get(country, '')
    wikipedia_views = wikipedia_data.get(country, '')
    gdp_ppp = gdp_ppp_data.get(country, '')

    merged.append({
        'Country': country,
        'Flag': flag,
        'Population': population,
        'GDP (PPP) per capita': gdp_ppp,
        'Site Visits': site_visits,
        'YouTube Views': youtube_views,
        'Wikipedia Views': wikipedia_views
    })

# Write consolidated CSV to both dist and public
for output_dir in [dist_dir, public_dir]:
    output_path = output_dir / 'country_analytics.csv'
    with open(output_path, 'w', newline='', encoding='utf-8') as f:
        fieldnames = ['Country', 'Flag', 'Population', 'GDP (PPP) per capita', 'Site Visits', 'YouTube Views', 'Wikipedia Views']
        writer = csv.DictWriter(f, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(merged)
    print(f"Created {output_path} with {len(merged)} countries")
