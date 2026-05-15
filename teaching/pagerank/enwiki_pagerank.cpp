#include <iostream>
#include <fstream>
#include <string>
#include <unordered_map>
#include <unordered_set>
#include <vector>
#include <chrono>
#include <iomanip>
#include <algorithm>
#include <sstream>
#include <numeric>
#include <climits>
#include <map>
#include <unistd.h>
#include <cstdlib>
#include <cstdio>
#include <cstring>
#include <filesystem>
#include <sys/stat.h>

// Constants
const int DEFAULT_YEAR = 2003;
const int DEFAULT_ITERATIONS = 3;

class StreamingENWikiPageRank {
public:
    std::unordered_map<int, int> wiki_id_to_our_id;
    std::vector<int> our_id_to_wiki_id; // Reverse mapping for O(1) lookups
    std::vector<int> outdegree;
    std::vector<int> indegree;
    std::vector<double> probability;
    std::vector<double> new_probability;
    std::vector<double> iteration_1_probability; // Store probabilities after iteration 1
    std::unordered_set<int> needed_wiki_ids; // IDs we need titles for
    std::unordered_map<int, std::string> wiki_id_to_title;
    int N; // Number of unique nodes
    double current_l1_distance;
    std::vector<double> l1_distances; // Store L1 distance for each iteration
    int total_edges;
    std::string csv_filename;

    // Function to escape special characters for JSON
    std::string escapeJSON(const std::string& input) {
        std::string output;
        for (char c : input) {
            switch (c) {
                case '\\': output += "\\\\"; break;
                case '"':  output += "\\\""; break;
                case '\b': output += "\\b"; break;
                case '\f': output += "\\f"; break;
                case '\n': output += "\\n"; break;
                case '\r': output += "\\r"; break;
                case '\t': output += "\\t"; break;
                default:
                    if (c >= 0x20 && c <= 0x7E) {
                        output += c;
                    } else {
                        // Replace other control characters with space
                        output += ' ';
                    }
            }
        }
        return output;
    }

    void downloadFile(const std::string& url, const std::string& filename) {
        if (fileExists(filename)) {
            std::cout << "✓ Using cached " << filename << std::endl;
            return;
        }

        std::cout << "↓ Downloading " << url << std::endl;
        std::string cmd = "wget -q --show-progress -O \"" + filename + "\" \"" + url + "\"";
        int rc = std::system(cmd.c_str());
        if (rc != 0) {
            throw std::runtime_error("wget failed for " + url);
        }
        std::cout << "✓ Downloaded " << filename << std::endl;
    }

    bool fileExists(const std::string& filename) {
        return ::access(filename.c_str(), F_OK) == 0;
    }

    void buildIdMapping(const std::string& filename) {
        std::cout << "🗺️ Building ID mapping from " << filename << "..." << std::endl;
        csv_filename = filename;

        std::ifstream file(filename);
        if (!file.is_open()) {
            throw std::runtime_error("Cannot open file: " + filename);
        }

        std::string line;
        std::getline(file, line); // Skip header
        std::cout << "   📋 CSV header: " << line << std::endl;

        std::unordered_set<int> unique_ids;
        int processed = 0;

        auto start_time = std::chrono::high_resolution_clock::now();

        // First pass: collect all unique IDs
        std::cout << "   🔍 Pass 1: Collecting unique IDs..." << std::endl;
        while (std::getline(file, line)) {
            auto fields = splitTabLine(line);
            if (fields.size() >= 4) {
                int from_id = std::stoi(fields[0]);
                int to_id = std::stoi(fields[2]);

                unique_ids.insert(from_id);
                unique_ids.insert(to_id);

                processed++;
                if (processed % 2000000 == 0) {
                    auto elapsed = std::chrono::duration_cast<std::chrono::seconds>(
                        std::chrono::high_resolution_clock::now() - start_time);
                    std::cout << "     📊 Processed " << processed << " edges, found " << unique_ids.size()
                              << " unique IDs (elapsed: " << elapsed.count() << "s)" << std::endl;
                }
            }
        }

        N = unique_ids.size();
        std::cout << "   ✅ Found " << N << " unique page IDs" << std::endl;

        // Build compact mapping: wiki_id -> our_id (0 to N-1)
        std::cout << "   🔗 Creating compact ID mapping..." << std::endl;
        our_id_to_wiki_id.resize(N); // Initialize reverse mapping vector
        int our_id = 0;
        for (int wiki_id : unique_ids) {
            wiki_id_to_our_id[wiki_id] = our_id;
            our_id_to_wiki_id[our_id] = wiki_id; // Build reverse mapping
            our_id++;
        }

        // Memory calculation
        size_t mapping_memory = wiki_id_to_our_id.size() * (sizeof(int) + sizeof(int) + 32); // rough estimate
        size_t vector_memory = N * (sizeof(int) + 2 * sizeof(double));
        std::cout << "   💾 Memory estimate:" << std::endl;
        std::cout << "     • ID mapping: ~" << (mapping_memory / 1024 / 1024) << " MB" << std::endl;
        std::cout << "     • Vectors: ~" << (vector_memory / 1024 / 1024) << " MB" << std::endl;
        std::cout << "     • Total: ~" << ((mapping_memory + vector_memory) / 1024 / 1024) << " MB" << std::endl;

        // Initialize compact vectors
        std::cout << "   🔧 Initializing vectors of size " << N << "..." << std::endl;
        outdegree.assign(N, 0);
        probability.assign(N, 1.0 / N); // Uniform initialization
        new_probability.assign(N, 0.0);

        file.close();
        std::cout << "✅ ID mapping built successfully" << std::endl;
    }

private:

    std::vector<std::string> splitTabLine(const std::string& line) {
        std::vector<std::string> result;
        std::stringstream ss(line);
        std::string field;

        while (std::getline(ss, field, '\t')) {
            result.push_back(field);
        }

        return result;
    }

public:
    void computeOutdegrees() {
        std::cout << "📊 Computing outdegrees in streaming pass..." << std::endl;

        std::ifstream file(csv_filename);
        if (!file.is_open()) {
            throw std::runtime_error("Cannot open file: " + csv_filename);
        }

        std::string line;
        std::getline(file, line); // Skip header

        total_edges = 0;
        int processed = 0;
        int skipped_self_loops = 0;
        int skipped_missing_ids = 0;

        auto start_time = std::chrono::high_resolution_clock::now();
        std::cout << "   🔍 Pass 2: Computing outdegrees..." << std::endl;

        while (std::getline(file, line)) {
            auto fields = splitTabLine(line);
            if (fields.size() >= 4) {
                int from_wiki_id = std::stoi(fields[0]);
                int to_wiki_id = std::stoi(fields[2]);

                // Skip self-loops
                if (from_wiki_id == to_wiki_id) {
                    skipped_self_loops++;
                } else {
                    auto from_it = wiki_id_to_our_id.find(from_wiki_id);
                    auto to_it = wiki_id_to_our_id.find(to_wiki_id);

                    if (from_it != wiki_id_to_our_id.end() && to_it != wiki_id_to_our_id.end()) {
                        int from_our_id = from_it->second;
                        outdegree[from_our_id]++;
                        total_edges++;
                    } else {
                        skipped_missing_ids++;
                    }
                }

                processed++;
                if (processed % 2000000 == 0) {
                    auto elapsed = std::chrono::duration_cast<std::chrono::seconds>(
                        std::chrono::high_resolution_clock::now() - start_time);
                    std::cout << "     📊 Processed " << processed << " edges, found " << total_edges << " valid edges"
                              << " (elapsed: " << elapsed.count() << "s)" << std::endl;
                    std::cout << "       ⚠️  Skipped: " << skipped_self_loops << " self-loops, "
                              << skipped_missing_ids << " missing IDs" << std::endl;
                }
            }
        }

        // Analyze outdegree distribution
        int dangling_nodes = 0;
        int max_outdegree = 0;
        long long total_outdegree = 0;
        std::vector<int> degree_bins(10, 0); // 0, 1-10, 11-50, 51-100, etc.

        for (int i = 0; i < N; i++) {
            int deg = outdegree[i];
            if (deg == 0) {
                dangling_nodes++;
                degree_bins[0]++;
            } else {
                total_outdegree += deg;
                max_outdegree = std::max(max_outdegree, deg);

                if (deg <= 10) degree_bins[1]++;
                else if (deg <= 50) degree_bins[2]++;
                else if (deg <= 100) degree_bins[3]++;
                else if (deg <= 500) degree_bins[4]++;
                else if (deg <= 1000) degree_bins[5]++;
                else if (deg <= 5000) degree_bins[6]++;
                else if (deg <= 10000) degree_bins[7]++;
                else if (deg <= 50000) degree_bins[8]++;
                else degree_bins[9]++;
            }
        }

        double avg_outdegree = (double)total_outdegree / (N - dangling_nodes);

        std::cout << "✅ Outdegree computation complete!" << std::endl;
        std::cout << "   📈 Edge statistics:" << std::endl;
        std::cout << "     • Total valid edges: " << total_edges << std::endl;
        std::cout << "     • Skipped self-loops: " << skipped_self_loops << std::endl;
        std::cout << "     • Skipped missing IDs: " << skipped_missing_ids << std::endl;
        std::cout << "   📊 Outdegree statistics:" << std::endl;
        std::cout << "     • Dangling nodes: " << dangling_nodes << " (" << std::fixed << std::setprecision(2)
                  << (100.0 * dangling_nodes / N) << "%)" << std::endl;
        std::cout << "     • Max outdegree: " << max_outdegree << std::endl;
        std::cout << "     • Avg outdegree: " << std::fixed << std::setprecision(2) << avg_outdegree << std::endl;
        std::cout << "   📋 Outdegree distribution:" << std::endl;
        std::cout << "     • 0: " << degree_bins[0] << std::endl;
        std::cout << "     • 1-10: " << degree_bins[1] << std::endl;
        std::cout << "     • 11-50: " << degree_bins[2] << std::endl;
        std::cout << "     • 51-100: " << degree_bins[3] << std::endl;
        std::cout << "     • 101-500: " << degree_bins[4] << std::endl;
        std::cout << "     • 501-1000: " << degree_bins[5] << std::endl;
        std::cout << "     • 1001-5000: " << degree_bins[6] << std::endl;
        std::cout << "     • 5001-10000: " << degree_bins[7] << std::endl;
        std::cout << "     • 10001-50000: " << degree_bins[8] << std::endl;
        std::cout << "     • 50000+: " << degree_bins[9] << std::endl;

        file.close();
    }

    void saveDegreeDistributions(int year) {
        std::cout << "📊 Calculating degree distributions..." << std::endl;

        // Calculate in-degree distribution by streaming through the CSV
        std::cout << "   🔍 Computing in-degree distribution..." << std::endl;
        indegree.resize(N, 0);
        std::ifstream file(csv_filename);
        if (!file.is_open()) {
            throw std::runtime_error("Cannot open file: " + csv_filename);
        }

        std::string line;
        std::getline(file, line); // Skip header

        while (std::getline(file, line)) {
            auto fields = splitTabLine(line);
            if (fields.size() >= 4) {
                int from_wiki_id = std::stoi(fields[0]);
                int to_wiki_id = std::stoi(fields[2]);

                if (from_wiki_id != to_wiki_id) {
                    auto from_it = wiki_id_to_our_id.find(from_wiki_id);
                    auto to_it = wiki_id_to_our_id.find(to_wiki_id);

                    if (from_it != wiki_id_to_our_id.end() && to_it != wiki_id_to_our_id.end()) {
                        int to_our_id = to_it->second;
                        indegree[to_our_id]++;
                    }
                }
            }
        }
        file.close();

        // Calculate degree distributions (using map for automatic sorting)
        std::map<int, int> in_degree_dist;
        std::map<int, int> out_degree_dist;

        for (int degree : indegree) {
            in_degree_dist[degree]++;
        }
        for (int degree : outdegree) {
            out_degree_dist[degree]++;
        }

        // Save degree distributions to JSON
        std::ofstream outfile(getYearDirectory(year) + "degree_distributions.json");
        outfile << "{\n";

        // In-degree distribution (std::map automatically sorts by key)
        outfile << "  \"in_degree_distribution\": [\n";
        bool first = true;
        for (const auto& pair : in_degree_dist) {
            if (!first) outfile << ",\n";
            outfile << "    {\"degree\": " << pair.first << ", \"count\": " << pair.second << "}";
            first = false;
        }

        outfile << "\n  ],\n";

        // Out-degree distribution (std::map automatically sorts by key)
        outfile << "  \"out_degree_distribution\": [\n";
        first = true;
        for (const auto& pair : out_degree_dist) {
            if (!first) outfile << ",\n";
            outfile << "    {\"degree\": " << pair.first << ", \"count\": " << pair.second << "}";
            first = false;
        }

        outfile << "\n  ],\n";
        outfile << "  \"stats\": {\n";
        outfile << "    \"total_nodes\": " << N << ",\n";
        outfile << "    \"total_edges\": " << total_edges << ",\n";

        // Calculate basic statistics
        double avg_degree = (double)total_edges / N;
        int max_in_degree = *std::max_element(indegree.begin(), indegree.end());
        int max_out_degree = *std::max_element(outdegree.begin(), outdegree.end());

        outfile << "    \"avg_in_degree\": " << avg_degree << ",\n";
        outfile << "    \"avg_out_degree\": " << avg_degree << ",\n";
        outfile << "    \"max_in_degree\": " << max_in_degree << ",\n";
        outfile << "    \"max_out_degree\": " << max_out_degree << "\n";
        outfile << "  }\n";
        outfile << "}\n";
        outfile.close();

        std::cout << "✅ Degree distributions saved to " << getYearDirectory(year) << "degree_distributions.json" << std::endl;
    }

    // Helper functions for year-specific directory management
    std::string getYearDirectory(int year) {
        return "public/" + std::to_string(year) + "/";
    }

    void ensureYearDirectoryExists(int year) {
        std::string dir = getYearDirectory(year);
        std::filesystem::create_directories(dir);
    }

    void saveCurrentYear(int year) {
        std::ofstream file("public/current_year.txt");
        file << year << std::endl;
        file.close();
        std::cout << "💾 Current year saved to public/current_year.txt: " << year << std::endl;
    }

    void runPageRank(double alpha = 0.9, int iterations = DEFAULT_ITERATIONS, int year = DEFAULT_YEAR, bool update_current_year = false) {
        if (N == 0) {
            std::cerr << "❌ No nodes in graph. Cannot run PageRank." << std::endl;
            return;
        }

        std::cout << "🎯 Running streaming PageRank algorithm:" << std::endl;
        std::cout << "   📊 Parameters: α=" << alpha << ", iterations=" << iterations << std::endl;
        std::cout << "   📊 Graph size: " << N << " nodes" << std::endl;

        // Initialize L1 distances vector
        l1_distances.resize(iterations + 1, 0.0); // Index 0 for initial, then 1..iterations
        l1_distances[0] = 0.0; // Initial state has no L1 distance

        saveIteration(0, probability, year);

        // Run power iterations
        std::cout << "   🔄 Starting streaming power iteration method..." << std::endl;
        for (int iter = 1; iter <= iterations; iter++) {
            auto start = std::chrono::high_resolution_clock::now();

            // Reset new_probability
            std::fill(new_probability.begin(), new_probability.end(), 0.0);

            // Stream through CSV to distribute PageRank
            streamPageRankIteration(alpha);

            // Calculate convergence
            double l1_change = 0.0;
            for (int i = 0; i < N; i++) {
                l1_change += std::abs(new_probability[i] - probability[i]);
            }

            // Swap vectors
            probability.swap(new_probability);
            current_l1_distance = l1_change;
            l1_distances[iter] = l1_change; // Store L1 distance for this iteration

            auto end = std::chrono::high_resolution_clock::now();
            auto duration = std::chrono::duration_cast<std::chrono::milliseconds>(end - start);

            // Show progress
            std::vector<int> top3 = getTopK(3);
            std::cout << "   📈 Iter " << iter << " (" << duration.count() << "ms): "
                      << "L1Δ=" << std::scientific << std::setprecision(2) << l1_change << std::endl;

            saveIteration(iter, probability, year);

            // Store probabilities after iteration 1 for change analysis
            if (iter == 1) {
                iteration_1_probability = probability;
                std::cout << "   💾 Stored iteration 1 probabilities for change analysis" << std::endl;
            }

            std::cout << "   ✅ Iteration " << iter << " completed" << std::endl;
        }

        std::cout << "✅ PageRank computation complete!" << std::endl;

        // Calculate and save biggest PageRank changes (this adds more IDs to needed_wiki_ids)
        saveBiggestChanges(year, iterations);

        // Final pass to lookup titles for all tracked IDs (including new ones from biggest changes)
        lookupTitlesForNeededIds();

        // Re-save all iterations with titles
        resaveIterationsWithTitles(iterations, year);

        // Save metadata including year
        saveMetadata(year, iterations);

        // Save titles to separate file for UI to combine with scores
        saveTitles(year);

        showFinalResults(25);
    }

private:
    void streamPageRankIteration(double alpha) {
        std::ifstream file(csv_filename);
        if (!file.is_open()) {
            throw std::runtime_error("Cannot open file: " + csv_filename);
        }

        std::string line;
        std::getline(file, line); // Skip header

        int processed = 0;
        int valid_transfers = 0;
        auto start_time = std::chrono::high_resolution_clock::now();

        // Stream through edges and distribute PageRank
        while (std::getline(file, line)) {
            auto fields = splitTabLine(line);
            if (fields.size() >= 4) {
                int from_wiki_id = std::stoi(fields[0]);
                int to_wiki_id = std::stoi(fields[2]);

                // Skip self-loops
                if (from_wiki_id != to_wiki_id) {
                    auto from_it = wiki_id_to_our_id.find(from_wiki_id);
                    auto to_it = wiki_id_to_our_id.find(to_wiki_id);

                    if (from_it != wiki_id_to_our_id.end() && to_it != wiki_id_to_our_id.end()) {
                        int from_our_id = from_it->second;
                        int to_our_id = to_it->second;

                        // Distribute PageRank from source to target
                        if (outdegree[from_our_id] > 0) {
                            double share = probability[from_our_id] / outdegree[from_our_id];
                            new_probability[to_our_id] += alpha * share;
                            valid_transfers++;
                        }
                    }
                }

                processed++;
                if (processed % 5000000 == 0) {
                    auto elapsed = std::chrono::duration_cast<std::chrono::seconds>(
                        std::chrono::high_resolution_clock::now() - start_time);
                    std::cout << "     🔄 Processed " << processed << " edges, " << valid_transfers
                              << " valid transfers (elapsed: " << elapsed.count() << "s)" << std::endl;
                }
            }
        }

        // Handle dangling mass and teleportation
        double dangling_mass = 0.0;
        int dangling_count = 0;
        for (int i = 0; i < N; i++) {
            if (outdegree[i] == 0) {
                dangling_mass += probability[i];
                dangling_count++;
            }
        }

        // Add teleportation and dangling mass
        double uniform_share = (alpha * dangling_mass + (1.0 - alpha)) / N;
        for (int i = 0; i < N; i++) {
            new_probability[i] += uniform_share;
        }

        std::cout << "     ⚡ Distributed " << valid_transfers << " PageRank transfers" << std::endl;
        std::cout << "     🌊 Dangling mass: " << std::scientific << std::setprecision(4) << dangling_mass
                  << " from " << dangling_count << " nodes" << std::endl;
        std::cout << "     📡 Uniform share per node: " << std::scientific << std::setprecision(4) << uniform_share << std::endl;

        file.close();
    }

    void lookupTitlesForNeededIds() {
        std::cout << "🔍 Looking up titles for " << needed_wiki_ids.size() << " needed Wikipedia IDs..." << std::endl;

        std::ifstream file(csv_filename);
        if (!file.is_open()) {
            throw std::runtime_error("Cannot open file: " + csv_filename);
        }

        std::string line;
        std::getline(file, line); // Skip header

        int processed = 0;
        int found_count = 0;

        while (std::getline(file, line) && found_count < needed_wiki_ids.size()) {
            auto fields = splitTabLine(line);
            if (fields.size() >= 4) {
                int from_id = std::stoi(fields[0]);
                int to_id = std::stoi(fields[2]);

                // Check if we need the from_id title
                if (needed_wiki_ids.find(from_id) != needed_wiki_ids.end() &&
                    wiki_id_to_title.find(from_id) == wiki_id_to_title.end()) {
                    wiki_id_to_title[from_id] = fields[1]; // page_title_from
                    found_count++;
                }

                // Check if we need the to_id title
                if (needed_wiki_ids.find(to_id) != needed_wiki_ids.end() &&
                    wiki_id_to_title.find(to_id) == wiki_id_to_title.end()) {
                    wiki_id_to_title[to_id] = fields[3]; // page_title_to
                    found_count++;
                }

                processed++;
                if (processed % 5000000 == 0) {
                    std::cout << "     📊 Processed " << processed << " edges, found " << found_count
                              << "/" << needed_wiki_ids.size() << " titles" << std::endl;
                }
            }
        }

        file.close();
        std::cout << "✅ Found titles for " << found_count << "/" << needed_wiki_ids.size() << " needed IDs" << std::endl;

        // Debug: show any missing titles
        for (int wiki_id : needed_wiki_ids) {
            if (wiki_id_to_title.find(wiki_id) == wiki_id_to_title.end()) {
                std::cout << "⚠️  Missing title for Wiki ID: " << wiki_id << std::endl;
            }
        }
    }

    void resaveIterationsWithTitles(int iterations, int year) {
        std::cout << "💾 Re-saving all iterations with titles..." << std::endl;

        for (int iter = 0; iter <= iterations; iter++) {
            std::ostringstream check_filename;
            check_filename << getYearDirectory(year) << "pagerank_iter_" << std::setfill('0') << std::setw(2) << iter << ".json";

            std::ifstream check_file(check_filename.str());
            if (!check_file.good()) {
                break; // No more iteration files
            }
            check_file.close();

            saveIterationWithTitles(iter, year);
        }
        std::cout << "✅ All iterations re-saved with titles" << std::endl;
    }

    void saveMetadata(int year, int iterations) {
        std::ofstream file(getYearDirectory(year) + "metadata.json");
        file << "{\n";
        file << "  \"year\": " << year << ",\n";
        file << "  \"dataset\": \"enwiki.wikilink_graph." << year << "-03-01.csv.gz\",\n";
        file << "  \"total_nodes\": " << N << ",\n";
        file << "  \"total_edges\": " << total_edges << ",\n";
        file << "  \"iterations\": " << iterations << "\n";
        file << "}\n";
        file.close();
        std::cout << "💾 Metadata saved to " << getYearDirectory(year) << "metadata.json" << std::endl;
    }

    void saveIterationWithTitles(int iteration, int year) {
        // DISABLE this function - it overwrites correct scores with wrong ones!
        // The original saveIteration() calls during the algorithm already save the correct scores
        // This function was trying to add titles but accidentally overwrites scores with final iteration values
        return;
    }

    void saveTitles(int year) {
        std::ostringstream filename;
        filename << getYearDirectory(year) << "titles.json";

        std::ofstream file(filename.str());
        file << "{\n";
        bool first = true;

        for (const auto& pair : wiki_id_to_title) {
            if (!first) file << ",\n";
            first = false;

            std::string title = pair.second;
            std::replace(title.begin(), title.end(), '_', ' ');

            file << "  \"" << pair.first << "\": \"" << escapeJSON(title) << "\"";
        }

        file << "\n}\n";
        file.close();
        std::cout << "💾 Titles saved to " << getYearDirectory(year) << "titles.json (" << wiki_id_to_title.size() << " titles)" << std::endl;
    }

    void saveBiggestChanges(int year, int iterations) {
        if (iteration_1_probability.empty()) {
            std::cout << "⚠️  No iteration 1 data available for change analysis" << std::endl;
            return;
        }

        std::cout << "📊 Analyzing biggest PageRank changes between iteration 1 and " << iterations << "..." << std::endl;

        // Calculate ratios for all nodes
        std::vector<std::pair<int, double>> ratios; // pair of (our_id, ratio)
        for (int i = 0; i < N; i++) {
            double iter1_score = iteration_1_probability[i];
            double final_score = probability[i];

            // Avoid division by zero - use a very small minimum value
            double ratio = final_score / std::max(iter1_score, 1e-15);
            ratios.push_back({i, ratio});
        }

        // Sort by ratio (descending for increases - highest multipliers first)
        std::partial_sort(ratios.begin(), ratios.begin() + std::min(25, N), ratios.end(),
                         [](const std::pair<int, double>& a, const std::pair<int, double>& b) {
                             return a.second > b.second; // Highest ratios first
                         });

        // Sort by ratio (ascending for decreases - lowest ratios first)
        std::vector<std::pair<int, double>> decreases = ratios;
        std::partial_sort(decreases.begin(), decreases.begin() + std::min(25, N), decreases.end(),
                         [](const std::pair<int, double>& a, const std::pair<int, double>& b) {
                             return a.second < b.second; // Lowest ratios first
                         });

        // Calculate indegree/pagerank ratios for all nodes
        std::vector<std::pair<int, double>> indegree_ratios; // pair of (our_id, indegree/pagerank)
        for (int i = 0; i < N; i++) {
            double final_score = probability[i];
            int node_indegree = indegree[i];

            // Avoid division by zero - use a very small minimum value
            double indegree_ratio = node_indegree / std::max(final_score, 1e-15);
            indegree_ratios.push_back({i, indegree_ratio});
        }

        // Sort by indegree/pagerank ratio (descending - high indegree, low pagerank first)
        std::partial_sort(indegree_ratios.begin(), indegree_ratios.begin() + std::min(25, N), indegree_ratios.end(),
                         [](const std::pair<int, double>& a, const std::pair<int, double>& b) {
                             return a.second > b.second; // Highest indegree/pagerank ratios first
                         });

        // Sort by indegree/pagerank ratio (ascending - low indegree, high pagerank first)
        std::vector<std::pair<int, double>> low_indegree_ratios = indegree_ratios;
        std::sort(low_indegree_ratios.begin(), low_indegree_ratios.end(),
                  [](const std::pair<int, double>& a, const std::pair<int, double>& b) {
                      return a.second < b.second; // Lowest indegree/pagerank ratios first
                  });

        // Save to JSON
        std::ostringstream filename;
        filename << getYearDirectory(year) << "biggest_changes.json";
        std::ofstream file(filename.str());
        file << "{\n";
        file << "  \"analysis\": {\n";
        file << "    \"from_iteration\": 1,\n";
        file << "    \"to_iteration\": " << iterations << ",\n";
        file << "    \"total_nodes\": " << N << "\n";
        file << "  },\n";

        // Top 25 increases (highest ratios)
        file << "  \"biggest_increases\": [\n";
        for (int i = 0; i < std::min(25, N); i++) {
            int our_id = ratios[i].first;
            int wiki_id = getWikiIdFromOurId(our_id);
            double ratio = ratios[i].second;
            double iter1_score = iteration_1_probability[our_id];
            double final_score = probability[our_id];
            double change = final_score - iter1_score;

            needed_wiki_ids.insert(wiki_id); // Track this ID for title lookup

            if (i > 0) file << ",\n";
            file << "    {\"rank\": " << (i + 1) << ", \"wiki_id\": " << wiki_id
                 << ", \"ratio\": " << std::scientific << std::setprecision(6) << ratio
                 << ", \"change\": " << std::scientific << std::setprecision(6) << change
                 << ", \"iter1_score\": " << std::scientific << std::setprecision(6) << iter1_score
                 << ", \"final_score\": " << std::scientific << std::setprecision(6) << final_score << "}";
        }
        file << "\n  ],\n";

        // Top 25 decreases (lowest ratios)
        file << "  \"biggest_decreases\": [\n";
        for (int i = 0; i < std::min(25, N); i++) {
            int our_id = decreases[i].first;
            int wiki_id = getWikiIdFromOurId(our_id);
            double ratio = decreases[i].second;
            double iter1_score = iteration_1_probability[our_id];
            double final_score = probability[our_id];
            double change = final_score - iter1_score;

            needed_wiki_ids.insert(wiki_id); // Track this ID for title lookup

            if (i > 0) file << ",\n";
            file << "    {\"rank\": " << (i + 1) << ", \"wiki_id\": " << wiki_id
                 << ", \"ratio\": " << std::scientific << std::setprecision(6) << ratio
                 << ", \"change\": " << std::scientific << std::setprecision(6) << change
                 << ", \"iter1_score\": " << std::scientific << std::setprecision(6) << iter1_score
                 << ", \"final_score\": " << std::scientific << std::setprecision(6) << final_score << "}";
        }
        file << "\n  ],\n";

        // Top 25 underperformers (high indegree, low pagerank)
        file << "  \"underperformers\": [\n";
        for (int i = 0; i < std::min(25, N); i++) {
            int our_id = indegree_ratios[i].first;
            int wiki_id = getWikiIdFromOurId(our_id);
            double indegree_ratio = indegree_ratios[i].second;
            int node_indegree = indegree[our_id];
            double final_score = probability[our_id];

            needed_wiki_ids.insert(wiki_id); // Track this ID for title lookup

            if (i > 0) file << ",\n";
            file << "    {\"rank\": " << (i + 1) << ", \"wiki_id\": " << wiki_id
                 << ", \"indegree_ratio\": " << std::scientific << std::setprecision(6) << indegree_ratio
                 << ", \"indegree\": " << node_indegree
                 << ", \"final_score\": " << std::scientific << std::setprecision(6) << final_score << "}";
        }
        file << "\n  ],\n";

        // Top 25 overperformers (low indegree, high pagerank) - only for nodes with indegree >= 1
        file << "  \"overperformers\": [\n";
        int overperformer_count = 0;
        for (int i = 0; i < N && overperformer_count < 25; i++) {
            int our_id = low_indegree_ratios[i].first;
            int node_indegree = indegree[our_id];

            // Only include nodes with indegree >= 1
            if (node_indegree >= 1) {
                int wiki_id = getWikiIdFromOurId(our_id);
                double indegree_ratio = low_indegree_ratios[i].second;
                double final_score = probability[our_id];

                needed_wiki_ids.insert(wiki_id); // Track this ID for title lookup

                if (overperformer_count > 0) file << ",\n";
                file << "    {\"rank\": " << (overperformer_count + 1) << ", \"wiki_id\": " << wiki_id
                     << ", \"indegree_ratio\": " << std::scientific << std::setprecision(6) << indegree_ratio
                     << ", \"indegree\": " << node_indegree
                     << ", \"final_score\": " << std::scientific << std::setprecision(6) << final_score << "}";
                overperformer_count++;
            }
        }
        file << "\n  ],\n";

        // Top 100 by indegree
        std::vector<std::pair<int, int>> indegree_sorted; // pair of (our_id, indegree)
        for (int i = 0; i < N; i++) {
            indegree_sorted.push_back({i, indegree[i]});
        }
        std::partial_sort(indegree_sorted.begin(), indegree_sorted.begin() + std::min(100, N), indegree_sorted.end(),
                         [](const auto& a, const auto& b) {
                             return a.second > b.second; // Highest indegree first
                         });

        file << "  \"top_by_indegree\": [\n";
        for (int i = 0; i < std::min(100, N); i++) {
            int our_id = indegree_sorted[i].first;
            int wiki_id = getWikiIdFromOurId(our_id);
            int node_indegree = indegree_sorted[i].second;
            double final_score = probability[our_id];

            needed_wiki_ids.insert(wiki_id); // Track this ID for title lookup

            if (i > 0) file << ",\n";
            file << "    {\"wiki_id\": " << wiki_id
                 << ", \"indegree\": " << node_indegree
                 << ", \"pagerank\": " << std::scientific << std::setprecision(6) << final_score << "}";
        }
        file << "\n  ]\n}\n";
        file.close();

        std::cout << "💾 Biggest changes saved to " << getYearDirectory(year) << "biggest_changes.json" << std::endl;
    }

public:
    std::vector<int> getTopK(int k) {
        return getTopK(k, probability);
    }

    std::vector<int> getTopK(int k, const std::vector<double>& rank_values) {
        std::vector<int> indices(N);
        std::iota(indices.begin(), indices.end(), 0);

        std::partial_sort(indices.begin(), indices.begin() + std::min(k, N),
                         indices.end(), [&rank_values, this](int a, int b) {
            // Primary sort: by rank value (descending)
            if (rank_values[a] != rank_values[b]) {
                return rank_values[a] > rank_values[b];
            }
            // Secondary sort: by wiki_id (ascending) when ranks are equal
            // Note: Titles aren't loaded yet during sorting, so we use wiki_id for deterministic results
            int wiki_id_a = getWikiIdFromOurId(a);
            int wiki_id_b = getWikiIdFromOurId(b);

            return wiki_id_a < wiki_id_b;
        });

        indices.resize(std::min(k, N));
        return indices;
    }

    std::vector<int> getBottomK(int k) {
        return getBottomK(k, probability);
    }

    std::vector<int> getBottomK(int k, const std::vector<double>& rank_values) {
        std::vector<int> indices(N);
        std::iota(indices.begin(), indices.end(), 0);

        std::partial_sort(indices.begin(), indices.begin() + std::min(k, N),
                         indices.end(), [&rank_values, this](int a, int b) {
            // Primary sort: by rank value (ascending for bottom K)
            if (rank_values[a] != rank_values[b]) {
                return rank_values[a] < rank_values[b];
            }
            // Secondary sort: by wiki_id (descending for bottom K to get different results from top K)
            // Note: Titles aren't loaded yet during sorting, so we use wiki_id for deterministic results
            int wiki_id_a = getWikiIdFromOurId(a);
            int wiki_id_b = getWikiIdFromOurId(b);

            return wiki_id_a > wiki_id_b;  // Descending to get different results from top K
        });

        indices.resize(std::min(k, N));
        return indices;
    }

    void saveIteration(int iteration, const std::vector<double>& current_ranks, int year) {
        std::ostringstream filename;
        filename << getYearDirectory(year) << "pagerank_iter_" << std::setfill('0') << std::setw(2) << iteration << ".json";

        std::ofstream file(filename.str());
        file << "{\n";
        file << "  \"iteration\": " << iteration << ",\n";
        file << "  \"l1_distance\": " << l1_distances[iteration] << ",\n";
        file << "  \"dataset_stats\": {\n";
        file << "    \"total_articles\": " << N << ",\n";
        file << "    \"total_edges\": " << total_edges << "\n";
        file << "  },\n";
        file << "  \"top_results\": [\n";

        std::vector<int> top_indices = getTopK(100, current_ranks);  // Save top 100
        for (int i = 0; i < top_indices.size(); i++) {
            int our_idx = top_indices[i];
            int wiki_id = getWikiIdFromOurId(our_idx);
            needed_wiki_ids.insert(wiki_id); // Track this ID for title lookup

            if (i > 0) file << ",\n";
            file << "    {\"rank\": " << (i + 1) << ", \"wiki_id\": " << wiki_id
                 << ", \"score\": " << std::scientific << std::setprecision(6) << current_ranks[our_idx]
                 << ", \"indegree\": " << (indegree.empty() ? 0 : indegree[our_idx]) << "}";
        }

        file << "\n  ],\n";
        file << "  \"bottom_results\": [\n";

        std::vector<int> bottom_indices = getBottomK(100, current_ranks);  // Save bottom 100
        for (int i = 0; i < bottom_indices.size(); i++) {
            int our_idx = bottom_indices[i];
            int wiki_id = getWikiIdFromOurId(our_idx);
            needed_wiki_ids.insert(wiki_id); // Track this ID for title lookup

            if (i > 0) file << ",\n";
            file << "    {\"rank\": " << (N - bottom_indices.size() + i + 1) << ", \"wiki_id\": " << wiki_id
                 << ", \"score\": " << std::scientific << std::setprecision(6) << current_ranks[our_idx] << "}";
        }

        file << "\n  ]\n}\n";
        file.close();

        if (iteration == 0) {
            std::cout << "💾 Saving iteration results to pagerank_iter_XX.json files..." << std::endl;
        }
    }

private:
    int getWikiIdFromOurId(int our_id) {
        if (our_id >= 0 && our_id < N) {
            return our_id_to_wiki_id[our_id]; // O(1) lookup
        }
        return -1; // Should never happen
    }

    void showFinalResults(int k) {
        std::cout << "\n🏆 Top " << k << " Wikipedia Pages by PageRank:" << std::endl;

        std::vector<int> top_indices = getTopK(k);

        for (int i = 0; i < top_indices.size(); i++) {
            int our_idx = top_indices[i];
            int wiki_id = getWikiIdFromOurId(our_idx);

            std::cout << std::setw(2) << (i + 1) << ". "
                      << "Wiki ID: " << std::setw(8) << std::left << wiki_id
                      << " Score: " << std::scientific << std::setprecision(6) << probability[our_idx] << std::endl;
        }
    }

public:
    void investigateIncomingLinks(int target_wiki_id, int year) {
        std::cout << "🔍 Investigating incoming links to wiki_id " << target_wiki_id << "..." << std::endl;

        // Check if this wiki_id exists in our mapping
        auto target_it = wiki_id_to_our_id.find(target_wiki_id);
        if (target_it == wiki_id_to_our_id.end()) {
            std::cerr << "❌ Wiki ID " << target_wiki_id << " not found in the graph" << std::endl;
            return;
        }
        int target_our_id = target_it->second;

        // Collect all pages that link to this target
        std::vector<std::tuple<int, int, double, std::string>> incoming_links; // (wiki_id, our_id, pagerank, title)

        std::ifstream file(csv_filename);
        if (!file.is_open()) {
            throw std::runtime_error("Cannot open file: " + csv_filename);
        }

        std::string line;
        std::getline(file, line); // Skip header

        int processed = 0;
        auto start_time = std::chrono::high_resolution_clock::now();

        while (std::getline(file, line)) {
            auto fields = splitTabLine(line);
            if (fields.size() >= 4) {
                int from_wiki_id = std::stoi(fields[0]);
                int to_wiki_id = std::stoi(fields[2]);

                // Skip self-loops
                if (from_wiki_id != to_wiki_id && to_wiki_id == target_wiki_id) {
                    auto from_it = wiki_id_to_our_id.find(from_wiki_id);
                    if (from_it != wiki_id_to_our_id.end()) {
                        int from_our_id = from_it->second;
                        std::string title = fields[1]; // page_title_from
                        std::replace(title.begin(), title.end(), '_', ' ');
                        incoming_links.push_back({from_wiki_id, from_our_id, probability[from_our_id], title});
                    }
                }

                // Also capture the target's title when we see it as a destination
                if (to_wiki_id == target_wiki_id && wiki_id_to_title.find(target_wiki_id) == wiki_id_to_title.end()) {
                    std::string target_title = fields[3]; // page_title_to
                    std::replace(target_title.begin(), target_title.end(), '_', ' ');
                    wiki_id_to_title[target_wiki_id] = target_title;
                }

                processed++;
                if (processed % 5000000 == 0) {
                    auto elapsed = std::chrono::duration_cast<std::chrono::seconds>(
                        std::chrono::high_resolution_clock::now() - start_time);
                    std::cout << "     📊 Processed " << processed << " edges, found " << incoming_links.size()
                              << " incoming links (elapsed: " << elapsed.count() << "s)" << std::endl;
                }
            }
        }
        file.close();

        // Sort by pagerank (descending)
        std::sort(incoming_links.begin(), incoming_links.end(),
                  [](const auto& a, const auto& b) {
                      return std::get<2>(a) > std::get<2>(b);
                  });

        // Get target page info
        std::string target_title = wiki_id_to_title.count(target_wiki_id) ? wiki_id_to_title[target_wiki_id] : "Unknown";
        double target_pagerank = probability[target_our_id];
        int target_indegree = indegree.empty() ? 0 : indegree[target_our_id];

        std::cout << "\n📄 Target page: " << target_title << " (wiki_id: " << target_wiki_id << ")" << std::endl;
        std::cout << "   PageRank: " << std::scientific << std::setprecision(6) << target_pagerank << std::endl;
        std::cout << "   In-degree: " << target_indegree << std::endl;
        std::cout << "   Found " << incoming_links.size() << " incoming links" << std::endl;

        // Save to JSON file
        std::ostringstream filename;
        filename << getYearDirectory(year) << "investigate_" << target_wiki_id << ".json";

        std::ofstream outfile(filename.str());
        outfile << "{\n";
        outfile << "  \"target\": {\n";
        outfile << "    \"wiki_id\": " << target_wiki_id << ",\n";
        outfile << "    \"title\": \"" << escapeJSON(target_title) << "\",\n";
        outfile << "    \"pagerank\": " << std::scientific << std::setprecision(6) << target_pagerank << ",\n";
        outfile << "    \"indegree\": " << target_indegree << "\n";
        outfile << "  },\n";
        outfile << "  \"incoming_links\": [\n";

        for (size_t i = 0; i < incoming_links.size(); i++) {
            const auto& link = incoming_links[i];
            if (i > 0) outfile << ",\n";
            outfile << "    {\"wiki_id\": " << std::get<0>(link)
                    << ", \"pagerank\": " << std::scientific << std::setprecision(6) << std::get<2>(link)
                    << ", \"title\": \"" << escapeJSON(std::get<3>(link)) << "\"}";
        }

        outfile << "\n  ],\n";
        outfile << "  \"summary\": {\n";
        outfile << "    \"total_incoming\": " << incoming_links.size() << ",\n";

        // Calculate total pagerank contribution from incoming links
        double total_contribution = 0.0;
        for (const auto& link : incoming_links) {
            int from_our_id = std::get<1>(link);
            if (outdegree[from_our_id] > 0) {
                total_contribution += std::get<2>(link) / outdegree[from_our_id];
            }
        }
        outfile << "    \"total_pagerank_contribution\": " << std::scientific << std::setprecision(6) << total_contribution << "\n";
        outfile << "  }\n";
        outfile << "}\n";
        outfile.close();

        std::cout << "💾 Investigation results saved to " << filename.str() << std::endl;

        // Print top 20 incoming links
        std::cout << "\n🔗 Top 20 pages linking to this page (by PageRank):" << std::endl;
        for (size_t i = 0; i < std::min(incoming_links.size(), (size_t)20); i++) {
            const auto& link = incoming_links[i];
            std::cout << std::setw(3) << (i + 1) << ". "
                      << std::scientific << std::setprecision(3) << std::get<2>(link) << " | "
                      << std::get<3>(link) << " (wiki_id: " << std::get<0>(link) << ")" << std::endl;
        }
    }
};

int main(int argc, char* argv[]) {
    double ALPHA = 0.9;
    int ITERATIONS = DEFAULT_ITERATIONS;
    int YEAR = DEFAULT_YEAR;
    int investigate_wiki_id = -1;
    bool update_year = false;

    // Parse arguments
    for (int i = 1; i < argc; i++) {
        std::string arg = argv[i];
        if (arg == "--investigate" && i + 1 < argc) {
            investigate_wiki_id = std::atoi(argv[++i]);
        } else if (arg == "--update-year") {
            update_year = true;
        } else if (arg == "--year" && i + 1 < argc) {
            YEAR = std::atoi(argv[++i]);
        } else if (arg == "--alpha" && i + 1 < argc) {
            ALPHA = std::atof(argv[++i]);
        } else if (arg == "--iterations" && i + 1 < argc) {
            ITERATIONS = std::atoi(argv[++i]);
        } else if (arg[0] != '-') {
            // Legacy positional arguments: alpha, iterations, year
            if (i == 1) ALPHA = std::atof(argv[i]);
            else if (i == 2) ITERATIONS = std::atoi(argv[i]);
            else if (i == 3) YEAR = std::atoi(argv[i]);
        }
    }

    const std::string ZENODO_BASE = "https://zenodo.org/records/2539424/files";
    const std::string FNAME = "enwiki.wikilink_graph." + std::to_string(YEAR) + "-03-01.csv.gz";
    const std::string URL = ZENODO_BASE + "/" + FNAME;

    try {
        std::cout << "🌐 WikiLinkGraphs English Wikipedia " << YEAR << " PageRank Demo (C++)" << std::endl;
        std::cout << "📥 Dataset: " << FNAME << std::endl;
        std::cout << "💡 Usage: ./enwiki_pagerank [options]" << std::endl;
        std::cout << "   --alpha N        Damping factor (default: 0.9)" << std::endl;
        std::cout << "   --iterations N   Number of iterations (default: " << DEFAULT_ITERATIONS << ")" << std::endl;
        std::cout << "   --year N         Wikipedia year (default: " << DEFAULT_YEAR << ")" << std::endl;
        std::cout << "   --update-year    Update public/current_year.txt" << std::endl;
        std::cout << "   --investigate ID Investigate incoming links to wiki_id" << std::endl;

        auto start = std::chrono::high_resolution_clock::now();

        StreamingENWikiPageRank pagerank;

        // Download file
        pagerank.downloadFile(URL, "data/" + FNAME);

        // Note: For simplicity, we'll assume the file is already decompressed to .csv
        // In a full implementation, we'd handle gzip decompression
        std::string csv_filename = "data/" + FNAME;
        csv_filename.replace(csv_filename.find(".gz"), 3, ""); // Remove .gz extension

        // Check if we need to decompress
        if (pagerank.fileExists("data/" + FNAME) && !pagerank.fileExists(csv_filename)) {
            std::cout << "⇣ Decompressing " << FNAME << "..." << std::endl;
            std::string decompress_cmd = "gunzip -k \"data/" + FNAME + "\"";
            int rc = std::system(decompress_cmd.c_str());
            if (rc != 0) {
                throw std::runtime_error("Failed to decompress file");
            }
            std::cout << "✓ Decompressed to " << csv_filename << std::endl;
        }

        // Build ID mapping and initialize vectors
        pagerank.buildIdMapping(csv_filename);

        // Compute outdegrees in streaming pass
        pagerank.computeOutdegrees();

        // Setup year-specific directory and save degree distributions
        pagerank.ensureYearDirectoryExists(YEAR);
        if (update_year) {
            pagerank.saveCurrentYear(YEAR);
        }
        pagerank.saveDegreeDistributions(YEAR);

        // Run PageRank
        pagerank.runPageRank(ALPHA, ITERATIONS, YEAR);

        // If investigate mode, run investigation after PageRank
        if (investigate_wiki_id >= 0) {
            pagerank.investigateIncomingLinks(investigate_wiki_id, YEAR);
        }

        auto end = std::chrono::high_resolution_clock::now();
        auto duration = std::chrono::duration_cast<std::chrono::seconds>(end - start);

        std::cout << "\n⏱️  Total execution time: " << duration.count() << " seconds" << std::endl;
        std::cout << "✅ Results saved to pagerank_iter_XX.json files" << std::endl;
        std::cout << "🚀 Start React UI: npm start" << std::endl;

    } catch (const std::exception& e) {
        std::cerr << "❌ Error: " << e.what() << std::endl;
        return 1;
    }

    return 0;
}