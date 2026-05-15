#!/usr/bin/env python3
"""Find articles with high out-degree from the CSV data."""

import csv
import gc
from collections import defaultdict

def find_high_outdegree_articles():
    print("🔍 Analyzing out-degrees from CSV data...")

    # Count outgoing links for each article with memory optimization
    out_degree = defaultdict(int)

    # Use generator to process file line by line without loading everything into memory
    with open('data/enwiki.wikilink_graph.2010-03-01.csv', 'r', encoding='utf-8') as f:
        reader = csv.reader(f, delimiter='\t')

        for i, row in enumerate(reader):
            if len(row) >= 4:
                from_article = row[1]  # page_title_from
                out_degree[from_article] += 1

            if i % 1000000 == 0:
                print(f"   📊 Processed {i:,} edges...")
                # Periodic garbage collection for large datasets
                if i % 5000000 == 0:
                    gc.collect()

    print(f"✅ Found {len(out_degree):,} unique articles")

    # Find articles with out-degree > 1000
    high_outdegree = [(article, degree) for article, degree in out_degree.items() if degree > 1000]
    high_outdegree.sort(key=lambda x: x[1], reverse=True)

    print(f"\n📈 Articles with out-degree > 1000 ({len(high_outdegree)} total):")
    print("=" * 60)

    for i, (article, degree) in enumerate(high_outdegree):
        print(f"{i+1:2d}. {article:<40} {degree:,} links")
        if i >= 19:  # Show top 20
            break

    if len(high_outdegree) > 20:
        print(f"    ... and {len(high_outdegree) - 20} more")

if __name__ == "__main__":
    find_high_outdegree_articles()