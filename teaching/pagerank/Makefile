CXX = g++
CXXFLAGS = -std=c++17 -O3 -march=native -flto -DNDEBUG

.PHONY: all clean debug enwiki_pagerank

all: enwiki_pagerank

enwiki_pagerank:
	$(CXX) $(CXXFLAGS) -o enwiki_pagerank enwiki_pagerank.cpp

clean:
	rm -f enwiki_pagerank pagerank_iter_*.json public/pagerank_iter_*.json enwiki.wikilink_graph.*.csv* *.tmp

debug: CXXFLAGS = -std=c++17 -O0 -g -fsanitize=address
debug: enwiki_pagerank

.DEFAULT_GOAL := all