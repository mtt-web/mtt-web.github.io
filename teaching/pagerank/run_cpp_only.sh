#!/bin/bash

echo "🚀 Running C++ PageRank algorithm without React interface..."
echo "📋 This will preserve all debug output in terminal"
echo

# Build the C++ program
echo "🔨 Building C++ program..."
make clean
make

if [ $? -eq 0 ]; then
    echo "✅ Build successful!"
    echo
    echo "🎯 Running PageRank algorithm..."
    echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"

    # Run the algorithm and capture all output
    ./enwiki_pagerank 2>&1 | tee pagerank_debug_output.log

    echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    echo "✅ Algorithm complete!"
    echo "📄 Debug output saved to: pagerank_debug_output.log"
    echo
    echo "🌐 To view results in web interface after completion:"
    echo "   npm start"
else
    echo "❌ Build failed!"
    exit 1
fi