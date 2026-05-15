# English Wikipedia PageRank Demo (2010)

**Two implementations** using historical English Wikipedia data (WikiLinkGraphs) with React visualization:
- 🐍 **Python** (with igraph) - Easy to understand, fast execution
- ⚡ **C++** - Maximum performance, detailed debug output

## Quick Start

### Option 1: Python (Recommended)
```bash
# 1. Install dependencies
make install-deps

# 2. Run PageRank (downloads 2005 Wikipedia automatically)
python3 enwiki_pagerank.py

# 3. View results in React UI
npm install && npm start
```

### Option 2: C++ (High Performance)
```bash
# 1. Build C++ version
make cpp

# 2. Run PageRank (uses downloaded data from Python version)
./enwiki_pagerank

# 3. View results in React UI
npm start
```

## What This Does

1. **Downloads** English Wikipedia 2005 snapshot (~93MB) from WikiLinkGraphs
2. **Handles** Zenodo's double-gzip compression automatically
3. **Builds** directed graph with clean article→article links (8.5M edges, ~600K nodes)
4. **Runs** 8 PageRank iterations with α=0.85
5. **Shows** top articles from early Wikipedia era
6. **Saves** results as JSON files for React visualization

## Why 2005 Wikipedia?

- ✅ **Small & fast** (~93MB vs multi-GB modern Wikipedia)
- ✅ **Clean data** (redirects resolved, templates removed)
- ✅ **Historical interest** (see what was important in early Wikipedia)
- ✅ **Perfect for demos** (runs in ~2 minutes on laptops)
- ✅ **Educational** (manageable size for understanding PageRank)

## Debug Output Features

Both implementations provide detailed progress tracking:

### Python Version
- Download progress with tqdm
- Graph building progress
- PageRank iteration progress with top articles
- Timing information

### C++ Version
- Line counting and CSV parsing progress
- Memory-efficient processing with progress percentages
- Detailed graph construction statistics
- Per-iteration timing and convergence metrics
- Top articles shown during iterations

## Performance Comparison

| Feature | Python | C++ |
|---------|--------|-----|
| **Graph Loading** | ~30s | ~60s (more detailed parsing) |
| **PageRank** | ~5s | ~2s |
| **Memory Usage** | Higher | Lower |
| **Debug Output** | Good | Excellent |
| **Ease of Use** | ✅ | Moderate |

## Files

- `enwiki_pagerank.py` - Python implementation (igraph backend)
- `enwiki_pagerank.cpp` - C++ implementation (pure algorithms)
- `pagerank_iter_XX.json` - Results for each iteration (React UI)
- `src/App.js` - Interactive React visualization component

## Algorithm Details

- **Dataset**: English Wikipedia (2010-03-01) via WikiLinkGraphs
- **Source**: Zenodo DOI: 10.5281/zenodo.2539424
- **Nodes**: ~600,000 Wikipedia articles
- **Edges**: ~8.5 million links between articles
- **Damping**: α = 0.85 (standard PageRank)
- **Iterations**: 8 (sufficient for convergence demonstration)

Perfect for demonstrating PageRank convergence on real historical Wikipedia data with both educational clarity and high performance options!