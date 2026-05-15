import React, { useState, useEffect } from 'react';
import {
  BarChart, Bar, ScatterChart, Scatter, LineChart, Line,
  XAxis, YAxis, CartesianGrid, Tooltip,
  ResponsiveContainer
} from 'recharts';

const App = () => {
  const [iterationData, setIterationData] = useState({});
  const [selectedIteration, setSelectedIteration] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [availableIterations, setAvailableIterations] = useState([]);
  const [rankingMode, setRankingMode] = useState('pagerank'); // 'pagerank' or 'indegree'
  const [degreeData, setDegreeData] = useState(null);
  const [metadata, setMetadata] = useState(null);
  const [currentYear, setCurrentYear] = useState(null);
  const [titles, setTitles] = useState({});
  const [titlesLoaded, setTitlesLoaded] = useState(false);
  const [biggestChanges, setBiggestChanges] = useState(null);
  const [inDegreeLogX, setInDegreeLogX] = useState(false);
  const [inDegreeLogY, setInDegreeLogY] = useState(false);
  const [outDegreeLogX, setOutDegreeLogX] = useState(false);
  const [outDegreeLogY, setOutDegreeLogY] = useState(false);
  const [outDegreeFilterGte2, setOutDegreeFilterGte2] = useState(false);
  const [convergenceLogY, setConvergenceLogY] = useState(false);

  useEffect(() => {
    loadCurrentYear();
  }, []);

  useEffect(() => {
    if (currentYear) {
      loadTitles();
      loadDegreeDistribution();
      loadMetadata();
      loadBiggestChanges();
    }
  }, [currentYear]);

  useEffect(() => {
    if (currentYear && titlesLoaded) {
      loadAllIterations();
    }
  }, [currentYear, titlesLoaded]);

  const loadCurrentYear = async () => {
    try {
      const response = await fetch('./current_year.txt');
      if (response.ok) {
        const year = await response.text();
        const yearNumber = parseInt(year.trim());
        setCurrentYear(yearNumber);
        console.log('✓ Current year loaded:', yearNumber);
      } else {
        console.log('✗ Current year file not found, defaulting to 2003');
        setCurrentYear(2003);
      }
    } catch (err) {
      console.error('Error loading current year:', err);
      setCurrentYear(2003);
    }
  };

  const loadTitles = async () => {
    try {
      const response = await fetch(`./${currentYear}/titles.json`);
      if (response.ok) {
        const titlesData = await response.json();
        setTitles(titlesData);
        console.log('✓ Titles loaded:', Object.keys(titlesData).length, 'titles');
      } else {
        console.log('✗ Titles file not found');
        setTitles({});
      }
    } catch (err) {
      console.error('Error loading titles:', err);
      setTitles({});
    } finally {
      setTitlesLoaded(true);
    }
  };

  const loadDegreeDistribution = async () => {
    try {
      const response = await fetch(`./${currentYear}/degree_distributions.json`);
      if (response.ok) {
        const data = await response.json();
        setDegreeData(data);
        console.log('✓ Degree distribution data loaded:', data);
      } else {
        console.log('✗ Degree distribution file not found');
      }
    } catch (err) {
      console.error('Error loading degree distribution:', err);
    }
  };

  const loadMetadata = async () => {
    try {
      const response = await fetch(`./${currentYear}/metadata.json`);
      if (response.ok) {
        const data = await response.json();
        setMetadata(data);
        console.log('✓ Metadata loaded:', data);
      } else {
        console.log('✗ Metadata file not found');
      }
    } catch (err) {
      console.error('Error loading metadata:', err);
    }
  };

  const loadBiggestChanges = async () => {
    try {
      const response = await fetch(`./${currentYear}/biggest_changes.json`);
      if (response.ok) {
        const data = await response.json();
        setBiggestChanges(data);
        console.log('✓ Biggest changes loaded:', data);
      } else {
        console.log('✗ Biggest changes file not found');
      }
    } catch (err) {
      console.error('Error loading biggest changes:', err);
    }
  };

  const enrichWithTitles = (data) => {
    const enrichedData = { ...data };
    if (enrichedData.top_results) {
      enrichedData.top_results = enrichedData.top_results.map(item => ({
        ...item,
        title: titles[item.wiki_id] || `Unknown (ID: ${item.wiki_id})`
      }));
    }
    if (enrichedData.bottom_results) {
      enrichedData.bottom_results = enrichedData.bottom_results.map(item => ({
        ...item,
        title: titles[item.wiki_id] || `Unknown (ID: ${item.wiki_id})`
      }));
    }
    if (enrichedData.results) {
      enrichedData.results = enrichedData.results.map(item => ({
        ...item,
        title: titles[item.wiki_id] || `Unknown (ID: ${item.wiki_id})`
      }));
    }
    return enrichedData;
  };

  const loadAllIterations = async () => {
    setLoading(true);
    setError(null);
    console.log('Starting to load PageRank iterations...');

    try {
      const iterations = {};
      const available = [];

      for (let i = 0; i <= 20; i++) {
        try {
          const filename = `./${currentYear}/pagerank_iter_${i.toString().padStart(2, '0')}.json`;
          const response = await fetch(filename);

          if (response.ok) {
            const text = await response.text();
            if (text.trim().startsWith('<!DOCTYPE') || text.trim().startsWith('<html')) {
              break;
            }

            try {
              const data = JSON.parse(text);
              if (data.top_results || data.results) {
                const enrichedData = enrichWithTitles(data);
                iterations[i] = enrichedData;
                available.push(i);
              }
            } catch (parseErr) {
              console.error(`Failed to parse JSON for iteration ${i}:`, parseErr);
            }
          } else if (response.status === 404) {
            break;
          }
        } catch (err) {
          continue;
        }
      }

      if (available.length === 0) {
        throw new Error('No PageRank result files found. Please run the C++ program first.');
      }

      setIterationData(iterations);
      setAvailableIterations(available);
      setSelectedIteration(available[available.length - 1]); // Default to last iteration
    } catch (err) {
      console.error('Error in loadAllIterations:', err);
      setError(err.message);
    } finally {
      setLoading(false);
    }
  };

  const formatScore = (score) => {
    return parseFloat(score).toExponential(3);
  };

  const getRankClass = (rank) => {
    if (rank <= 3) return 'rank-row top-3';
    if (rank <= 10) return 'rank-row top-10';
    if (rank <= 25) return 'rank-row top-25';
    return 'rank-row';
  };

  // Data is now embedded directly in the JSON files:
  // - top_results items have 'indegree' field
  // - top_by_indegree items have 'pagerank' field

  if (loading) {
    return (
      <div className="app">
        <div className="loading">
          Loading PageRank results...
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="app">
        <div className="header">
          <h1>PageRank Viewer</h1>
          <p>English Wikipedia PageRank Visualization</p>
        </div>
        <div className="error">
          <h2>Error</h2>
          <p>{error}</p>
          <p><strong>Instructions:</strong></p>
          <ol style={{textAlign: 'left', marginTop: '1rem'}}>
            <li>Compile: <code>make</code></li>
            <li>Run: <code>./pagerank</code></li>
            <li>Start server: <code>npm start</code></li>
          </ol>
        </div>
      </div>
    );
  }

  const currentData = iterationData[selectedIteration];
  if (!currentData) {
    return (
      <div className="app">
        <div className="no-data">No data available for iteration {selectedIteration}</div>
      </div>
    );
  }

  const maxIteration = availableIterations[availableIterations.length - 1];

  return (
    <div className="app">
      <div className="header">
        <h1>PageRank Viewer</h1>
        <p>English Wikipedia ({currentYear || metadata?.year || '2003'}) PageRank Visualization</p>
        {metadata && (
          <p style={{fontSize: '0.9em', opacity: 0.8}}>
            Dataset: {metadata.dataset} | {metadata.total_nodes.toLocaleString()} nodes | {metadata.total_edges.toLocaleString()} edges
          </p>
        )}
      </div>

      <div className="results-container">
        {/* Global Data Section */}
        {degreeData && (
          <div className="degree-distribution">
            <h3>Degree Distribution Analysis</h3>
            <div className="degree-stats">
              <div className="stats-grid">
                <div className="stat-item">
                  <span className="stat-label">Total Nodes:</span>
                  <span className="stat-value">{degreeData?.stats?.total_nodes?.toLocaleString() || 'N/A'}</span>
                </div>
                <div className="stat-item">
                  <span className="stat-label">Total Edges:</span>
                  <span className="stat-value">{degreeData?.stats?.total_edges?.toLocaleString() || 'N/A'}</span>
                </div>
                <div className="stat-item">
                  <span className="stat-label">Avg In-Degree:</span>
                  <span className="stat-value">{degreeData?.stats?.avg_in_degree?.toFixed(2) || 'N/A'}</span>
                </div>
                <div className="stat-item">
                  <span className="stat-label">Max In-Degree:</span>
                  <span className="stat-value">{degreeData?.stats?.max_in_degree?.toLocaleString() || 'N/A'}</span>
                </div>
              </div>
            </div>

            {/* Chart Options */}
            <div className="chart-controls" style={{margin: '1rem 0', padding: '1rem', background: 'rgba(255,255,255,0.1)', borderRadius: '8px'}}>
              <h4>Chart Options</h4>
              <div style={{display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: '1rem'}}>
                <div>
                  <h5 style={{color: '#667eea', margin: '0 0 0.5rem 0'}}>In-Degree Chart</h5>
                  <label style={{display: 'flex', alignItems: 'center', gap: '0.5rem', color: 'white', marginBottom: '0.5rem'}}>
                    <input type="checkbox" checked={inDegreeLogX} onChange={(e) => setInDegreeLogX(e.target.checked)} />
                    X-axis log scale
                  </label>
                  <label style={{display: 'flex', alignItems: 'center', gap: '0.5rem', color: 'white'}}>
                    <input type="checkbox" checked={inDegreeLogY} onChange={(e) => setInDegreeLogY(e.target.checked)} />
                    Y-axis log scale
                  </label>
                </div>
                <div>
                  <h5 style={{color: '#764ba2', margin: '0 0 0.5rem 0'}}>Out-Degree Chart</h5>
                  <label style={{display: 'flex', alignItems: 'center', gap: '0.5rem', color: 'white', marginBottom: '0.5rem'}}>
                    <input type="checkbox" checked={outDegreeLogX} onChange={(e) => setOutDegreeLogX(e.target.checked)} />
                    X-axis log scale
                  </label>
                  <label style={{display: 'flex', alignItems: 'center', gap: '0.5rem', color: 'white'}}>
                    <input type="checkbox" checked={outDegreeLogY} onChange={(e) => setOutDegreeLogY(e.target.checked)} />
                    Y-axis log scale
                  </label>
                </div>
                <div>
                  <h5 style={{color: '#10b981', margin: '0 0 0.5rem 0'}}>Convergence Chart</h5>
                  <label style={{display: 'flex', alignItems: 'center', gap: '0.5rem', color: 'white'}}>
                    <input type="checkbox" checked={convergenceLogY} onChange={(e) => setConvergenceLogY(e.target.checked)} />
                    Y-axis log scale
                  </label>
                </div>
              </div>
            </div>

            <div className="degree-charts">
              <div className="degree-chart">
                <h4>In-Degree Distribution {inDegreeLogX && inDegreeLogY ? '(Log-Log)' : inDegreeLogX ? '(Log-X)' : inDegreeLogY ? '(Log-Y)' : ''}</h4>
                {(() => {
                  const data = degreeData?.in_degree_distribution || [];
                  let filteredData = data;
                  if (inDegreeLogX || inDegreeLogY) {
                    filteredData = data.filter(item => item.degree > 0 && item.count > 0);
                  } else {
                    filteredData = data.filter(item => item.degree <= 50);
                  }
                  if (filteredData.length === 0) return <div style={{color: 'white', padding: '20px'}}>No data</div>;
                  const isScatter = inDegreeLogX || inDegreeLogY;
                  const chartData = filteredData.map(item => ({ degree: item.degree, count: item.count }));

                  return (
                    <ResponsiveContainer width={500} height={300}>
                      {isScatter ? (
                        <ScatterChart data={chartData} margin={{ top: 20, right: 20, bottom: 40, left: 60 }}>
                          <CartesianGrid strokeDasharray="3 3" stroke="#444" />
                          <XAxis dataKey="degree" scale={inDegreeLogX ? "log" : "linear"} domain={['dataMin', 'dataMax']} type="number" stroke="white" tick={{ fill: 'white' }} />
                          <YAxis dataKey="count" scale={inDegreeLogY ? "log" : "linear"} domain={['dataMin', 'dataMax']} type="number" stroke="white" tick={{ fill: 'white' }} />
                          <Tooltip formatter={(value) => value.toLocaleString()} contentStyle={{ backgroundColor: '#333', border: '1px solid #667eea' }} />
                          <Scatter dataKey="count" fill="#667eea" />
                        </ScatterChart>
                      ) : (
                        <BarChart data={chartData} margin={{ top: 20, right: 20, bottom: 40, left: 60 }}>
                          <CartesianGrid strokeDasharray="3 3" stroke="#444" />
                          <XAxis dataKey="degree" stroke="white" tick={{ fill: 'white' }} />
                          <YAxis stroke="white" tick={{ fill: 'white' }} />
                          <Tooltip contentStyle={{ backgroundColor: '#333', border: '1px solid #667eea' }} />
                          <Bar dataKey="count" fill="#667eea" />
                        </BarChart>
                      )}
                    </ResponsiveContainer>
                  );
                })()}
              </div>

              <div className="degree-chart">
                <h4>Out-Degree Distribution {outDegreeLogX && outDegreeLogY ? '(Log-Log)' : outDegreeLogX ? '(Log-X)' : outDegreeLogY ? '(Log-Y)' : ''}</h4>
                {(() => {
                  const data = degreeData?.out_degree_distribution || [];
                  let filteredData = data;
                  if (outDegreeLogX || outDegreeLogY) {
                    filteredData = data.filter(item => item.degree > 0 && item.count > 0);
                  } else {
                    filteredData = data.filter(item => item.degree <= 50);
                  }
                  if (filteredData.length === 0) return <div style={{color: 'white', padding: '20px'}}>No data</div>;
                  const isScatter = outDegreeLogX || outDegreeLogY;
                  const chartData = filteredData.map(item => ({ degree: item.degree, count: item.count }));

                  return (
                    <ResponsiveContainer width={500} height={300}>
                      {isScatter ? (
                        <ScatterChart data={chartData} margin={{ top: 20, right: 20, bottom: 40, left: 60 }}>
                          <CartesianGrid strokeDasharray="3 3" stroke="#444" />
                          <XAxis dataKey="degree" scale={outDegreeLogX ? "log" : "linear"} domain={['dataMin', 'dataMax']} type="number" stroke="white" tick={{ fill: 'white' }} />
                          <YAxis dataKey="count" scale={outDegreeLogY ? "log" : "linear"} domain={['dataMin', 'dataMax']} type="number" stroke="white" tick={{ fill: 'white' }} />
                          <Tooltip formatter={(value) => value.toLocaleString()} contentStyle={{ backgroundColor: '#333', border: '1px solid #764ba2' }} />
                          <Scatter dataKey="count" fill="#764ba2" />
                        </ScatterChart>
                      ) : (
                        <BarChart data={chartData} margin={{ top: 20, right: 20, bottom: 40, left: 60 }}>
                          <CartesianGrid strokeDasharray="3 3" stroke="#444" />
                          <XAxis dataKey="degree" stroke="white" tick={{ fill: 'white' }} />
                          <YAxis stroke="white" tick={{ fill: 'white' }} />
                          <Tooltip contentStyle={{ backgroundColor: '#333', border: '1px solid #764ba2' }} />
                          <Bar dataKey="count" fill="#764ba2" />
                        </BarChart>
                      )}
                    </ResponsiveContainer>
                  );
                })()}
              </div>
            </div>
          </div>
        )}

        {/* Convergence Graph */}
        <div className="convergence-graph">
          <h3>Convergence Graph {convergenceLogY ? '(Log Y-axis)' : ''}</h3>
          <p>L1 distance between consecutive iterations</p>
          {(() => {
            const convergenceData = (availableIterations || []).slice(1)
              .map(iter => {
                const data = iterationData[iter];
                if (!data || data.l1_distance === undefined) return null;
                return { iteration: iter, l1_distance: data.l1_distance };
              })
              .filter(item => item !== null);

            if (convergenceData.length === 0) return <div style={{color: 'white', padding: '20px'}}>No convergence data</div>;

            return (
              <ResponsiveContainer width={600} height={250}>
                <LineChart data={convergenceData} margin={{ top: 20, right: 30, left: 60, bottom: 40 }}>
                  <CartesianGrid strokeDasharray="3 3" stroke="#444" />
                  <XAxis dataKey="iteration" type="number" domain={['dataMin', 'dataMax']} stroke="white" tick={{ fill: 'white' }} />
                  <YAxis dataKey="l1_distance" scale={convergenceLogY ? "log" : "linear"} domain={convergenceLogY ? ['auto', 'auto'] : [0, 'dataMax']} stroke="white" tick={{ fill: 'white' }} tickFormatter={(value) => value.toExponential(1)} />
                  <Tooltip formatter={(value) => [value.toExponential(3), 'L1 Distance']} contentStyle={{ backgroundColor: '#333', border: '1px solid #10b981' }} />
                  <Line dataKey="l1_distance" stroke="#10b981" strokeWidth={3} dot={{ fill: '#10b981', strokeWidth: 2, r: 5, stroke: '#fff' }} />
                </LineChart>
              </ResponsiveContainer>
            );
          })()}
        </div>

        {/* Unified Top 100 Table */}
        <div className="iteration-info">
          <h2>Top 100 Articles</h2>

          {/* Mode Toggle - Slider style */}
          <div style={{display: 'flex', justifyContent: 'center', alignItems: 'center', gap: '1rem', margin: '1rem 0'}}>
            <span style={{color: rankingMode === 'pagerank' ? '#667eea' : '#888', fontWeight: rankingMode === 'pagerank' ? 'bold' : 'normal'}}>
              By PageRank
            </span>
            <label className="switch" style={{position: 'relative', display: 'inline-block', width: '60px', height: '34px'}}>
              <input
                type="checkbox"
                checked={rankingMode === 'indegree'}
                onChange={(e) => setRankingMode(e.target.checked ? 'indegree' : 'pagerank')}
                style={{opacity: 0, width: 0, height: 0}}
              />
              <span style={{
                position: 'absolute', cursor: 'pointer', top: 0, left: 0, right: 0, bottom: 0,
                backgroundColor: rankingMode === 'indegree' ? '#60a5fa' : '#667eea',
                transition: '.4s', borderRadius: '34px'
              }}>
                <span style={{
                  position: 'absolute', content: '""', height: '26px', width: '26px',
                  left: rankingMode === 'indegree' ? '30px' : '4px', bottom: '4px',
                  backgroundColor: 'white', transition: '.4s', borderRadius: '50%'
                }}></span>
              </span>
            </label>
            <span style={{color: rankingMode === 'indegree' ? '#60a5fa' : '#888', fontWeight: rankingMode === 'indegree' ? 'bold' : 'normal'}}>
              By Indegree
            </span>
          </div>

          {/* Iteration Slider - only show for PageRank mode */}
          {rankingMode === 'pagerank' && (
            <div style={{display: 'flex', justifyContent: 'center', alignItems: 'center', gap: '1rem', margin: '1rem 0'}}>
              <span style={{color: 'white'}}>Iteration:</span>
              <input
                type="range"
                min={availableIterations[0]}
                max={maxIteration}
                value={selectedIteration}
                onChange={(e) => setSelectedIteration(parseInt(e.target.value))}
                style={{width: '200px'}}
              />
              <span style={{color: 'white', minWidth: '80px'}}>
                {selectedIteration === 0 ? 'Initial' : `Iter ${selectedIteration}`}
              </span>
              {selectedIteration > 0 && iterationData[selectedIteration]?.l1_distance && (
                <span className="l1-badge" style={{fontSize: '0.8em', padding: '2px 8px', background: '#10b981', borderRadius: '4px'}}>
                  L1: {iterationData[selectedIteration].l1_distance.toExponential(2)}
                </span>
              )}
            </div>
          )}

          <div className="results-table-container">
            <table className="results-table">
              <thead>
                <tr>
                  <th>Rank</th>
                  <th>Article Title</th>
                  {rankingMode === 'pagerank' ? (
                    <>
                      <th>PageRank Score</th>
                      <th>Indegree</th>
                    </>
                  ) : (
                    <>
                      <th>Indegree</th>
                      <th>PageRank (iter {maxIteration})</th>
                    </>
                  )}
                </tr>
              </thead>
              <tbody>
                {rankingMode === 'pagerank' ? (
                  (currentData.top_results || currentData.results || []).map((item, index) => (
                    <tr key={index} className={getRankClass(item.rank)}>
                      <td className="rank-cell">
                        #{item.rank}
                        {item.rank === 1 && ' 🥇'}
                        {item.rank === 2 && ' 🥈'}
                        {item.rank === 3 && ' 🥉'}
                      </td>
                      <td className="title-cell">{item.title}</td>
                      <td className="score-cell">{formatScore(item.score)}</td>
                      <td className="score-cell">{item.indegree !== undefined ? item.indegree.toLocaleString() : '—'}</td>
                    </tr>
                  ))
                ) : (
                  (biggestChanges?.top_by_indegree || []).map((item, index) => (
                    <tr key={index} className={getRankClass(index + 1)}>
                      <td className="rank-cell">
                        #{index + 1}
                        {index === 0 && ' 🥇'}
                        {index === 1 && ' 🥈'}
                        {index === 2 && ' 🥉'}
                      </td>
                      <td className="title-cell">{titles[item.wiki_id] || `Unknown (ID: ${item.wiki_id})`}</td>
                      <td className="score-cell">{item.indegree.toLocaleString()}</td>
                      <td className="score-cell">{item.pagerank !== undefined ? formatScore(item.pagerank) : '—'}</td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        </div>

        {/* Biggest Changes Section */}
        {biggestChanges && (
          <div className="changes-section" style={{marginTop: '3rem'}}>
            <h2 style={{textAlign: 'center', marginBottom: '0.5rem'}}>Biggest PageRank Changes</h2>
            <p style={{textAlign: 'center', marginBottom: '1.5rem', opacity: 0.8}}>
              Changes between iteration {biggestChanges.analysis.from_iteration} and {biggestChanges.analysis.to_iteration}
            </p>

            <div style={{display: 'flex', gap: '2rem', justifyContent: 'center', flexWrap: 'wrap'}}>
              {/* Biggest Increases */}
              <div className="changes-column" style={{flex: '1', minWidth: '400px', maxWidth: '600px'}}>
                <h3 style={{color: '#4ade80', textAlign: 'center', marginBottom: '1rem'}}>Top 25 Increases</h3>
                <div className="changes-table-container" style={{maxHeight: '400px', overflowY: 'auto', border: '1px solid #374151', borderRadius: '8px'}}>
                  <table className="changes-table" style={{width: '100%', borderCollapse: 'collapse'}}>
                    <thead style={{position: 'sticky', top: 0, background: '#1f2937', zIndex: 1}}>
                      <tr>
                        <th style={{padding: '0.75rem', borderBottom: '1px solid #374151', color: '#4ade80'}}>Rank</th>
                        <th style={{padding: '0.75rem', borderBottom: '1px solid #374151', color: '#4ade80'}}>Title</th>
                        <th style={{padding: '0.75rem', borderBottom: '1px solid #374151', color: '#4ade80'}}>Change</th>
                      </tr>
                    </thead>
                    <tbody>
                      {biggestChanges.biggest_increases.map((item, index) => (
                        <tr key={`inc-${index}`} style={{borderBottom: '1px solid #374151'}}>
                          <td style={{padding: '0.5rem', textAlign: 'center'}}>#{item.rank}</td>
                          <td style={{padding: '0.5rem'}}>{titles[item.wiki_id] || `Unknown (ID: ${item.wiki_id})`}</td>
                          <td style={{padding: '0.5rem', textAlign: 'right', color: '#4ade80'}}>
                            {formatScore(item.iter1_score)} → {formatScore(item.final_score)}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </div>

              {/* Biggest Decreases */}
              <div className="changes-column" style={{flex: '1', minWidth: '400px', maxWidth: '600px'}}>
                <h3 style={{color: '#f87171', textAlign: 'center', marginBottom: '1rem'}}>Top 25 Decreases</h3>
                <div className="changes-table-container" style={{maxHeight: '400px', overflowY: 'auto', border: '1px solid #374151', borderRadius: '8px'}}>
                  <table className="changes-table" style={{width: '100%', borderCollapse: 'collapse'}}>
                    <thead style={{position: 'sticky', top: 0, background: '#1f2937', zIndex: 1}}>
                      <tr>
                        <th style={{padding: '0.75rem', borderBottom: '1px solid #374151', color: '#f87171'}}>Rank</th>
                        <th style={{padding: '0.75rem', borderBottom: '1px solid #374151', color: '#f87171'}}>Title</th>
                        <th style={{padding: '0.75rem', borderBottom: '1px solid #374151', color: '#f87171'}}>Change</th>
                      </tr>
                    </thead>
                    <tbody>
                      {biggestChanges.biggest_decreases.map((item, index) => (
                        <tr key={`dec-${index}`} style={{borderBottom: '1px solid #374151'}}>
                          <td style={{padding: '0.5rem', textAlign: 'center'}}>#{item.rank}</td>
                          <td style={{padding: '0.5rem'}}>{titles[item.wiki_id] || `Unknown (ID: ${item.wiki_id})`}</td>
                          <td style={{padding: '0.5rem', textAlign: 'right', color: '#f87171'}}>
                            {formatScore(item.iter1_score)} → {formatScore(item.final_score)}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </div>
            </div>

            {/* Indegree vs PageRank Analysis */}
            <div style={{marginTop: '2rem'}}>
              <h3 style={{textAlign: 'center', marginBottom: '1.5rem'}}>Indegree vs PageRank Analysis</h3>
              <div style={{display: 'flex', gap: '2rem', justifyContent: 'center', flexWrap: 'wrap'}}>
                {biggestChanges.overperformers && (
                  <div className="changes-column" style={{flex: '1', minWidth: '400px', maxWidth: '600px'}}>
                    <h3 style={{color: '#8b5cf6', textAlign: 'center', marginBottom: '1rem'}}>Top 25 Overperformers</h3>
                    <p style={{textAlign: 'center', fontSize: '0.8rem', marginBottom: '1rem', opacity: 0.7}}>Low indegree, high PageRank</p>
                    <div className="changes-table-container" style={{maxHeight: '400px', overflowY: 'auto', border: '1px solid #374151', borderRadius: '8px'}}>
                      <table className="changes-table" style={{width: '100%', borderCollapse: 'collapse'}}>
                        <thead style={{position: 'sticky', top: 0, background: '#1f2937', zIndex: 1}}>
                          <tr>
                            <th style={{padding: '0.75rem', borderBottom: '1px solid #374151', color: '#8b5cf6'}}>Rank</th>
                            <th style={{padding: '0.75rem', borderBottom: '1px solid #374151', color: '#8b5cf6'}}>Title</th>
                            <th style={{padding: '0.75rem', borderBottom: '1px solid #374151', color: '#8b5cf6'}}>Indegree</th>
                            <th style={{padding: '0.75rem', borderBottom: '1px solid #374151', color: '#8b5cf6'}}>PageRank</th>
                          </tr>
                        </thead>
                        <tbody>
                          {biggestChanges.overperformers.map((item, index) => (
                            <tr key={`over-${index}`} style={{borderBottom: '1px solid #374151'}}>
                              <td style={{padding: '0.5rem', textAlign: 'center'}}>#{item.rank}</td>
                              <td style={{padding: '0.5rem'}}>{titles[item.wiki_id] || `Unknown`}</td>
                              <td style={{padding: '0.5rem', textAlign: 'right'}}>{item.indegree}</td>
                              <td style={{padding: '0.5rem', textAlign: 'right'}}>{formatScore(item.final_score)}</td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  </div>
                )}

                {biggestChanges.underperformers && (
                  <div className="changes-column" style={{flex: '1', minWidth: '400px', maxWidth: '600px'}}>
                    <h3 style={{color: '#f59e0b', textAlign: 'center', marginBottom: '1rem'}}>Top 25 Underperformers</h3>
                    <p style={{textAlign: 'center', fontSize: '0.8rem', marginBottom: '1rem', opacity: 0.7}}>High indegree, low PageRank</p>
                    <div className="changes-table-container" style={{maxHeight: '400px', overflowY: 'auto', border: '1px solid #374151', borderRadius: '8px'}}>
                      <table className="changes-table" style={{width: '100%', borderCollapse: 'collapse'}}>
                        <thead style={{position: 'sticky', top: 0, background: '#1f2937', zIndex: 1}}>
                          <tr>
                            <th style={{padding: '0.75rem', borderBottom: '1px solid #374151', color: '#f59e0b'}}>Rank</th>
                            <th style={{padding: '0.75rem', borderBottom: '1px solid #374151', color: '#f59e0b'}}>Title</th>
                            <th style={{padding: '0.75rem', borderBottom: '1px solid #374151', color: '#f59e0b'}}>Indegree</th>
                            <th style={{padding: '0.75rem', borderBottom: '1px solid #374151', color: '#f59e0b'}}>PageRank</th>
                          </tr>
                        </thead>
                        <tbody>
                          {biggestChanges.underperformers.map((item, index) => (
                            <tr key={`under-${index}`} style={{borderBottom: '1px solid #374151'}}>
                              <td style={{padding: '0.5rem', textAlign: 'center'}}>#{item.rank}</td>
                              <td style={{padding: '0.5rem'}}>{titles[item.wiki_id] || `Unknown`}</td>
                              <td style={{padding: '0.5rem', textAlign: 'right'}}>{item.indegree}</td>
                              <td style={{padding: '0.5rem', textAlign: 'right'}}>{formatScore(item.final_score)}</td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  </div>
                )}
              </div>
            </div>
          </div>
        )}

        <div style={{textAlign: 'center', marginTop: '2rem', opacity: 0.7}}>
          <p>
            English Wikipedia ({currentYear || '2003'}) PageRank | {availableIterations.length} iterations
          </p>
        </div>
      </div>
    </div>
  );
};

export default App;
