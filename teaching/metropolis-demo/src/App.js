import React, { useState, useCallback, useMemo } from 'react';
import 'katex/dist/katex.min.css';
import { InlineMath, BlockMath } from 'react-katex';
import './App.css';

const GRID_SIZE = 15;
const CELL_TYPES = {
  GRASS: 0,
  TREE: 1,
  LAKE: 2
};

const CELL_COLORS = {
  [CELL_TYPES.GRASS]: '#90EE90',
  [CELL_TYPES.TREE]: '#228B22',
  [CELL_TYPES.LAKE]: '#4169E1'
};

const CELL_NAMES = {
  [CELL_TYPES.GRASS]: 'Grass',
  [CELL_TYPES.TREE]: 'Tree',
  [CELL_TYPES.LAKE]: 'Lake'
};

const CELL_EMOJIS = {
  [CELL_TYPES.GRASS]: '🌱',
  [CELL_TYPES.TREE]: '🌲',
  [CELL_TYPES.LAKE]: '💧'
};

function App() {
  const [mode, setMode] = useState('independent'); // 'independent' or 'metropolis'

  // Independent sampling weights (probabilities)
  const [grassProb, setGrassProb] = useState(0.4);
  const [treeProb, setTreeProb] = useState(0.3);
  const [lakeProb, setLakeProb] = useState(0.3);

  // Dependent sampling pairwise weights
  const [pairWeights, setPairWeights] = useState({
    'grass-grass': 1.0,
    'tree-tree': 1.0,
    'lake-lake': 1.0,
    'grass-tree': 0.5,
    'grass-lake': 0.5,
    'lake-tree': 0.5
  });

  // Unary weights (cell preferences) — control marginal proportions
  const [unaryWeights, setUnaryWeights] = useState({
    [CELL_TYPES.GRASS]: 1.0,
    [CELL_TYPES.TREE]: 1.0,
    [CELL_TYPES.LAKE]: 1.0
  });

  // Interaction strength (beta in [0,1]); beta=0 ≈ independent (unary only)
  const [beta, setBeta] = useState(1.0);

  // Grid state
  const [grid, setGrid] = useState(() => initializeGrid());

  // Dependent sampling visualization state
  const [isRunning, setIsRunning] = useState(false);
  const [highlightedCell, setHighlightedCell] = useState(null);
  const [acceptanceFlash, setAcceptanceFlash] = useState(null); // 'accept' or 'reject'
  const [stepCount, setStepCount] = useState(0);
  const [numSteps, setNumSteps] = useState(10000);
  const [continuousRunning, setContinuousRunning] = useState(false);
  const [flashingCells, setFlashingCells] = useState([]); // Array of {i, j, type: 'accept'|'reject', timestamp}
  const [stepDetails, setStepDetails] = useState(null); // Details for single step visualization
  const [acceptanceHistory, setAcceptanceHistory] = useState([]); // Rolling window for acceptance rate

  function initializeGrid() {
    const newGrid = [];
    for (let i = 0; i < GRID_SIZE; i++) {
      const row = [];
      for (let j = 0; j < GRID_SIZE; j++) {
        row.push(Math.floor(Math.random() * 3));
      }
      newGrid.push(row);
    }
    return newGrid;
  }

  // Independent sampling
  const generateIndependentSample = useCallback(() => {
    const total = grassProb + treeProb + lakeProb;
    const normGrass = grassProb / total;
    const normTree = treeProb / total;

    const newGrid = [];
    for (let i = 0; i < GRID_SIZE; i++) {
      const row = [];
      for (let j = 0; j < GRID_SIZE; j++) {
        const r = Math.random();
        if (r < normGrass) {
          row.push(CELL_TYPES.GRASS);
        } else if (r < normGrass + normTree) {
          row.push(CELL_TYPES.TREE);
        } else {
          row.push(CELL_TYPES.LAKE);
        }
      }
      newGrid.push(row);
    }
    setGrid(newGrid);
  }, [grassProb, treeProb, lakeProb]);

  // (Removed unused global energy calculation; single-site updates use local edge deltas.)

  function getPairKey(type1, type2) {
    const name1 = CELL_NAMES[type1].toLowerCase();
    const name2 = CELL_NAMES[type2].toLowerCase();
    // Ensure consistent ordering
    if (name1 <= name2) {
      return `${name1}-${name2}`;
    } else {
      return `${name2}-${name1}`;
    }
  }

  // Single dependent-sampling step (using local delta for O(1) computation)
  const metropolisStep = useCallback(() => {
    // Choose random cell
    const i = Math.floor(Math.random() * GRID_SIZE);
    const j = Math.floor(Math.random() * GRID_SIZE);

    // Choose new random type
    const newType = Math.floor(Math.random() * 3);
    const oldType = grid[i][j];

    if (newType === oldType) {
      // No change to grid, but we treat as an accepted noop and record it
      setAcceptanceHistory(prev => {
        const newHistory = [...prev, 1];
        return newHistory.slice(-1000);
      });
      return { i, j, accepted: true, oldType, newType, deltaLogProb: 0, acceptProb: 1 };
    }

    // Calculate local energy change (only affected edges)
    const neighbors = [
      [i - 1, j], [i, j + 1], [i + 1, j], [i, j - 1]
    ];

    // Unary contribution (only the flipped site changes)
    let logProbBefore = Math.log(unaryWeights[oldType]);
    let logProbAfter = Math.log(unaryWeights[newType]);

    for (const [ni, nj] of neighbors) {
      if (ni >= 0 && ni < GRID_SIZE && nj >= 0 && nj < GRID_SIZE) {
        const neighborType = grid[ni][nj];

        const oldKey = getPairKey(oldType, neighborType);
        const oldWeight = pairWeights[oldKey];
        if (oldWeight === 0) {
          logProbBefore = -Infinity;
          break;
        }
        logProbBefore += beta * Math.log(oldWeight);

        const newKey = getPairKey(newType, neighborType);
        const newWeight = pairWeights[newKey];
        if (newWeight === 0) {
          logProbAfter = -Infinity;
        } else {
          logProbAfter += beta * Math.log(newWeight);
        }
      }
    }

    // Accept/reject based on Metropolis criterion with robust handling of infinities
    let acceptProb;
    let deltaLogProb = logProbAfter - logProbBefore;
    if (logProbBefore === -Infinity && logProbAfter > -Infinity) {
      // Old state impossible, new possible -> accept with prob 1
      acceptProb = 1;
    } else if (logProbAfter === -Infinity && logProbBefore > -Infinity) {
      // New state impossible, old possible -> reject with prob 0
      acceptProb = 0;
    } else if (logProbBefore === -Infinity && logProbAfter === -Infinity) {
      // Both impossible -> define as reject
      acceptProb = 0;
      deltaLogProb = 0;
    } else {
      acceptProb = Math.min(1, Math.exp(deltaLogProb));
    }
    const accepted = Math.random() < acceptProb;

    if (accepted) {
      const newGrid = grid.map(row => [...row]);
      newGrid[i][j] = newType;
      setGrid(newGrid);
    }

    // Track acceptance for statistics
    setAcceptanceHistory(prev => {
      const newHistory = [...prev, accepted ? 1 : 0];
      // Keep only last 1000 steps
      return newHistory.slice(-1000);
    });

    return { i, j, accepted, oldType, newType, deltaLogProb, acceptProb };
  }, [grid, pairWeights, unaryWeights, beta]);

  // Run single step (with visualization)
  const runSingleStep = useCallback(() => {
    const result = metropolisStep();
    setHighlightedCell({ i: result.i, j: result.j, newType: result.newType });

    // Calculate neighbors and their weights for detailed view
    const i = result.i;
    const j = result.j;
    const neighbors = [
      [i - 1, j], [i, j + 1], [i + 1, j], [i, j - 1]
    ];

    const oldWeights = [];
    const newWeights = [];

    for (const [ni, nj] of neighbors) {
      if (ni >= 0 && ni < GRID_SIZE && nj >= 0 && nj < GRID_SIZE) {
        const neighborType = grid[ni][nj];
        const oldKey = getPairKey(result.oldType, neighborType);
        const newKey = getPairKey(result.newType, neighborType);
        const oldVal = pairWeights[oldKey];
        const newVal = pairWeights[newKey];
        oldWeights.push({ key: oldKey, value: oldVal, neighborType, isBoundary: false });
        newWeights.push({ key: newKey, value: newVal, neighborType, isBoundary: false });
      } else {
        // Off-grid sides contribute multiplicative identity 1
        oldWeights.push({ key: null, value: 1, neighborType: null, isBoundary: true });
        newWeights.push({ key: null, value: 1, neighborType: null, isBoundary: true });
      }
    }

    // Build LaTeX for exactly four terms in numerator/denominator
    const labelFor = (centerType, w) => (w.isBoundary ? '1' : `w(${CELL_EMOJIS[centerType]},${CELL_EMOJIS[w.neighborType]})`);
    const valueFor = (w) => (w.value !== undefined ? w.value.toFixed(2) : '1.00');

    const latexNumTerms = newWeights.map(w => labelFor(result.newType, w)).join(' \\cdot ');
    const latexDenTerms = oldWeights.map(w => labelFor(result.oldType, w)).join(' \\cdot ');
    const numProd = newWeights.reduce((p, w) => p * (w.value !== undefined ? w.value : 1), 1);
    const denProd = oldWeights.reduce((p, w) => p * (w.value !== undefined ? w.value : 1), 1);
    const ratioPair = (denProd === 0 ? (numProd > 0 ? Infinity : NaN) : numProd / denProd);
    const latexNumVals = newWeights.map(w => valueFor(w)).join(' \\cdot ');
    const latexDenVals = oldWeights.map(w => valueFor(w)).join(' \\cdot ');
    const uOld = unaryWeights[result.oldType];
    const uNew = unaryWeights[result.newType];
    const uRatio = (uOld === 0 ? (uNew > 0 ? Infinity : NaN) : (uNew / uOld));
    const combinedRatio = (() => {
      const pr = Number.isFinite(ratioPair) ? Math.pow(ratioPair, beta) : (ratioPair === Infinity ? Infinity : NaN);
      if (pr === Infinity || uRatio === Infinity) return Infinity;
      if (Number.isFinite(pr) && Number.isFinite(uRatio)) return pr * uRatio;
      if ((pr === 0 || uRatio === 0) && !(isNaN(pr) || isNaN(uRatio))) return 0;
      return NaN;
    })();
    const ratioDisplay = Number.isFinite(combinedRatio)
      ? combinedRatio.toFixed(3)
      : (combinedRatio === Infinity ? '\\infty' : 'undefined');
    const accProbShown = (typeof result.acceptProb === 'number' && isFinite(result.acceptProb)) ? result.acceptProb : 0;
    const betaStr = beta.toFixed(2);
    const latexCombined = `\\begin{aligned}
      &\\left(\\frac{${latexNumTerms}}{${latexDenTerms}}\\right)^{${betaStr}} \\cdot \\frac{u(${CELL_EMOJIS[result.newType]})}{u(${CELL_EMOJIS[result.oldType]})} \\\\
      &= \\left(\\frac{${latexNumVals}}{${latexDenVals}}\\right)^{${betaStr}} \\cdot \\frac{${uNew.toFixed(2)}}{${uOld.toFixed(2)}} \\\\
      &= ${ratioDisplay} \\\\
      p &= ${accProbShown.toFixed(3)} ${result.accepted ? '\\text{ (Accepted ✓)}' : '\\text{ (Rejected ×)}'}
    \\end{aligned}`;

    setStepDetails({
      i,
      j,
      oldType: result.oldType,
      newType: result.newType,
      accepted: result.accepted,
      neighbors,
      oldWeights,
      newWeights,
      ratio: Number.isFinite(combinedRatio) ? combinedRatio : null,
      acceptProb: result.acceptProb,
      latexCombined
    });

    setTimeout(() => {
      setAcceptanceFlash(result.accepted ? 'accept' : 'reject');
      setTimeout(() => {
        setHighlightedCell(null);
        setAcceptanceFlash(null);
      }, 300);
    }, 200);

    setStepCount(prev => prev + 1);
  }, [metropolisStep, grid, pairWeights]);

  // Continuous run with visual feedback
  const runContinuousStep = useCallback(() => {
    const result = metropolisStep();

    // Add the new flash to the list
    const newFlash = {
      i: result.i,
      j: result.j,
      type: result.accepted ? 'accept' : 'reject',
      timestamp: Date.now()
    };

    setFlashingCells(prev => {
      // Remove flashes older than 1.5 seconds
      const now = Date.now();
      const filtered = prev.filter(cell => now - cell.timestamp < 1500);
      return [...filtered, newFlash];
    });

    setStepCount(prev => prev + 1);
  }, [metropolisStep]);

  // Effect to handle continuous running
  React.useEffect(() => {
    if (continuousRunning) {
      const interval = setInterval(() => {
        runContinuousStep();
      }, 100); // Run a step every 100ms

      return () => clearInterval(interval);
    }
  }, [continuousRunning, runContinuousStep]);

  // Clear any lingering flash/highlight state when stopping continuous run
  React.useEffect(() => {
    if (!continuousRunning) {
      setFlashingCells([]);
      setAcceptanceFlash(null);
      setHighlightedCell(null);
    }
  }, [continuousRunning]);

  // Toggle continuous running
  const toggleContinuousRun = useCallback(() => {
    setContinuousRunning(prev => !prev);
  }, []);

  // Run many steps quickly
  const runManySteps = useCallback(() => {
    setIsRunning(true);
    // Ensure no leftover visual effects from continuous mode
    setFlashingCells([]);
    setAcceptanceFlash(null);
    setHighlightedCell(null);
    setStepDetails(null);
    let newGrid = grid.map(row => [...row]);

    for (let step = 0; step < numSteps; step++) {
      // Choose random cell
      const i = Math.floor(Math.random() * GRID_SIZE);
      const j = Math.floor(Math.random() * GRID_SIZE);
      const newType = Math.floor(Math.random() * 3);
      const oldType = newGrid[i][j];

      if (newType !== oldType) {
        // Unary contribution (only flipped site)
        let energyBefore = Math.log(unaryWeights[oldType]);
        const neighbors = [
          [i - 1, j], [i + 1, j], [i, j - 1], [i, j + 1]
        ];
        for (const [ni, nj] of neighbors) {
          if (ni >= 0 && ni < GRID_SIZE && nj >= 0 && nj < GRID_SIZE) {
            const neighborType = newGrid[ni][nj];
            const key = getPairKey(oldType, neighborType);
            const pairWeight = pairWeights[key];
            if (pairWeight === 0) {
              energyBefore = -Infinity;
              break;
            }
            energyBefore += beta * Math.log(pairWeight);
          }
        }

        // After: unary + pairwise around site
        let energyAfter = Math.log(unaryWeights[newType]);
        for (const [ni, nj] of neighbors) {
          if (ni >= 0 && ni < GRID_SIZE && nj >= 0 && nj < GRID_SIZE) {
            const neighborType = newGrid[ni][nj];
            const key = getPairKey(newType, neighborType);
            const pairWeight = pairWeights[key];
            if (pairWeight === 0) {
              energyAfter = -Infinity;
              break;
            }
            energyAfter += beta * Math.log(pairWeight);
          }
        }

        const deltaE = energyAfter - energyBefore;
        const acceptProb = Math.min(1, Math.exp(deltaE));

        if (Math.random() < acceptProb) {
          newGrid[i][j] = newType;
        }
      }
    }

    setGrid(newGrid);
    setStepCount(prev => prev + numSteps);
    setIsRunning(false);
  }, [grid, pairWeights, unaryWeights, beta, numSteps]);

  // Reset grid and counter
  const resetGrid = useCallback(() => {
    setGrid(initializeGrid());
    setStepCount(0);
    setHighlightedCell(null);
    setAcceptanceFlash(null);
    setAcceptanceHistory([]);
    setStepDetails(null);
  }, []);

  // Generate LaTeX formulas
  const independentFormula = useMemo(() => {
    const total = grassProb + treeProb + lakeProb;
    const pG = (grassProb / total).toFixed(3);
    const pT = (treeProb / total).toFixed(3);
    const pL = (lakeProb / total).toFixed(3);
    return `\\begin{aligned} P(\\text{grid}) &= \\prod_{i,j} P(x_{ij}) \\\\ \\text{where } P(🌱) &= ${pG}, \\; P(🌲) = ${pT}, \\; P(💧) = ${pL} \\end{aligned}`;
  }, [grassProb, treeProb, lakeProb]);

  const metropolisFormula = useMemo(() => {
    return `P(\\text{grid}) \\propto \\prod_i u(x_i) \\cdot \\left( \\prod_{\\text{edge neighbors}} w(x_i, x_j) \\right)^{\\beta}`;
  }, []);

  const metropolisWeights = useMemo(() => {
    const wGG = pairWeights['grass-grass'].toFixed(2);
    const wTT = pairWeights['tree-tree'].toFixed(2);
    const wLL = pairWeights['lake-lake'].toFixed(2);
    const wGT = pairWeights['grass-tree'].toFixed(2);
    const wGL = pairWeights['grass-lake'].toFixed(2);
    const wTL = pairWeights['lake-tree'].toFixed(2);

    return `w(🌱,🌱) = ${wGG}, w(🌲,🌲) = ${wTT}, w(💧,💧) = ${wLL} \\\\
    w(🌱,🌲) = ${wGT}, w(🌱,💧) = ${wGL}, w(🌲,💧) = ${wTL}`;
  }, [pairWeights]);

  const unaryWeightsLatex = useMemo(() => {
    const uG = unaryWeights[CELL_TYPES.GRASS].toFixed(2);
    const uT = unaryWeights[CELL_TYPES.TREE].toFixed(2);
    const uL = unaryWeights[CELL_TYPES.LAKE].toFixed(2);
    return `u(🌱) = ${uG}, \\; u(🌲) = ${uT}, \\; u(💧) = ${uL}`;
  }, [unaryWeights]);

  return (
    <div className="App">
      <h1>Dependent Sampling Demo</h1>

      <div className="mode-selector">
        <button
          className={mode === 'independent' ? 'active' : ''}
          onClick={() => setMode('independent')}
        >
          Independent Sampling
        </button>
        <button
          className={mode === 'metropolis' ? 'active' : ''}
          onClick={() => setMode('metropolis')}
        >
          Dependent Sampling
        </button>
      </div>

      <div className="content">
        <div className="controls">
          {mode === 'independent' ? (
            <div className="independent-controls">
              <h3>Independent Sampling</h3>

              <div className="formula-box">
                <p className="formula-description">
                  Each cell is sampled independently from:
                </p>
                <BlockMath math={independentFormula} />
              </div>

              <div className="control-group">
                <label>
                  🌱 Grass: {grassProb.toFixed(2)}
                  <input
                    type="range"
                    min="0.01"
                    max="1"
                    step="0.01"
                    value={grassProb}
                    onChange={(e) => {
                      const newGrass = parseFloat(e.target.value);
                      const remaining = 1 - newGrass;
                      const currentOther = treeProb + lakeProb;
                      if (currentOther > 0) {
                        setGrassProb(newGrass);
                        setTreeProb(treeProb * remaining / currentOther);
                        setLakeProb(lakeProb * remaining / currentOther);
                      } else {
                        setGrassProb(newGrass);
                        setTreeProb(remaining / 2);
                        setLakeProb(remaining / 2);
                      }
                    }}
                  />
                </label>
              </div>
              <div className="control-group">
                <label>
                  🌲 Tree: {treeProb.toFixed(2)}
                  <input
                    type="range"
                    min="0.01"
                    max="1"
                    step="0.01"
                    value={treeProb}
                    onChange={(e) => {
                      const newTree = parseFloat(e.target.value);
                      const remaining = 1 - newTree;
                      const currentOther = grassProb + lakeProb;
                      if (currentOther > 0) {
                        setTreeProb(newTree);
                        setGrassProb(grassProb * remaining / currentOther);
                        setLakeProb(lakeProb * remaining / currentOther);
                      } else {
                        setTreeProb(newTree);
                        setGrassProb(remaining / 2);
                        setLakeProb(remaining / 2);
                      }
                    }}
                  />
                </label>
              </div>
              <div className="control-group">
                <label>
                  💧 Lake: {lakeProb.toFixed(2)}
                  <input
                    type="range"
                    min="0.01"
                    max="1"
                    step="0.01"
                    value={lakeProb}
                    onChange={(e) => {
                      const newLake = parseFloat(e.target.value);
                      const remaining = 1 - newLake;
                      const currentOther = grassProb + treeProb;
                      if (currentOther > 0) {
                        setLakeProb(newLake);
                        setGrassProb(grassProb * remaining / currentOther);
                        setTreeProb(treeProb * remaining / currentOther);
                      } else {
                        setLakeProb(newLake);
                        setGrassProb(remaining / 2);
                        setTreeProb(remaining / 2);
                      }
                    }}
                  />
                </label>
              </div>
              <button onClick={generateIndependentSample} className="generate-btn">
                Generate Sample
              </button>
            </div>
          ) : (
            <div className="metropolis-controls">
              <h3>Dependent Sampling</h3>

              <div className="formula-box">
                <p className="formula-description">
                  Sampling from distribution:
                </p>
                <BlockMath math={metropolisFormula} />
                <BlockMath math={unaryWeightsLatex} />
                <BlockMath math={metropolisWeights} />
              </div>

          <div className="weight-section">
            <h4>Unary Weights (Type Preferences)</h4>
            <div className="control-group">
              <label>
                u(🌱): {unaryWeights[CELL_TYPES.GRASS].toFixed(2)}
                <input
                  type="range"
                  min="0.1"
                  max="10"
                  step="0.1"
                  value={unaryWeights[CELL_TYPES.GRASS]}
                  onChange={(e) => setUnaryWeights({
                    ...unaryWeights,
                    [CELL_TYPES.GRASS]: parseFloat(e.target.value)
                  })}
                />
              </label>
            </div>
            <div className="control-group">
              <label>
                u(🌲): {unaryWeights[CELL_TYPES.TREE].toFixed(2)}
                <input
                  type="range"
                  min="0.1"
                  max="10"
                  step="0.1"
                  value={unaryWeights[CELL_TYPES.TREE]}
                  onChange={(e) => setUnaryWeights({
                    ...unaryWeights,
                    [CELL_TYPES.TREE]: parseFloat(e.target.value)
                  })}
                />
              </label>
            </div>
            <div className="control-group">
              <label>
                u(💧): {unaryWeights[CELL_TYPES.LAKE].toFixed(2)}
                <input
                  type="range"
                  min="0.1"
                  max="10"
                  step="0.1"
                  value={unaryWeights[CELL_TYPES.LAKE]}
                  onChange={(e) => setUnaryWeights({
                    ...unaryWeights,
                    [CELL_TYPES.LAKE]: parseFloat(e.target.value)
                  })}
                />
              </label>
            </div>
          </div>

              <div className="weight-section">
                <h4>Pairwise Weights</h4>
                <div className="control-group">
                  <label>
                    🌱-🌱: {pairWeights['grass-grass'].toFixed(2)}
                    <input
                      type="range"
                      min="0"
                      max="10"
                      step="0.1"
                      value={pairWeights['grass-grass']}
                      onChange={(e) => setPairWeights({
                        ...pairWeights,
                        'grass-grass': parseFloat(e.target.value)
                      })}
                    />
                  </label>
                </div>
                <div className="control-group">
                  <label>
                    🌲-🌲: {pairWeights['tree-tree'].toFixed(2)}
                    <input
                      type="range"
                      min="0"
                      max="10"
                      step="0.1"
                      value={pairWeights['tree-tree']}
                      onChange={(e) => setPairWeights({
                        ...pairWeights,
                        'tree-tree': parseFloat(e.target.value)
                      })}
                    />
                  </label>
                </div>
                <div className="control-group">
                  <label>
                    💧-💧: {pairWeights['lake-lake'].toFixed(2)}
                    <input
                      type="range"
                      min="0"
                      max="10"
                      step="0.1"
                      value={pairWeights['lake-lake']}
                      onChange={(e) => setPairWeights({
                        ...pairWeights,
                        'lake-lake': parseFloat(e.target.value)
                      })}
                    />
                  </label>
                </div>
                <div className="control-group">
                  <label>
                    🌱-🌲: {pairWeights['grass-tree'].toFixed(2)}
                    <input
                      type="range"
                      min="0"
                      max="10"
                      step="0.1"
                      value={pairWeights['grass-tree']}
                      onChange={(e) => setPairWeights({
                        ...pairWeights,
                        'grass-tree': parseFloat(e.target.value)
                      })}
                    />
                  </label>
                </div>
                <div className="control-group">
                  <label>
                    🌱-💧: {pairWeights['grass-lake'].toFixed(2)}
                    <input
                      type="range"
                      min="0"
                      max="10"
                      step="0.1"
                      value={pairWeights['grass-lake']}
                      onChange={(e) => setPairWeights({
                        ...pairWeights,
                        'grass-lake': parseFloat(e.target.value)
                      })}
                    />
                  </label>
                </div>
                <div className="control-group">
                  <label>
                    🌲-💧: {pairWeights['lake-tree'].toFixed(2)}
                    <input
                      type="range"
                      min="0"
                      max="10"
                      step="0.1"
                      value={pairWeights['lake-tree']}
                      onChange={(e) => setPairWeights({
                        ...pairWeights,
                        'lake-tree': parseFloat(e.target.value)
                      })}
                    />
                  </label>
                </div>
              <div className="control-group">
                <label>
                  β (interaction strength): {beta.toFixed(2)}
                  <input
                    type="range"
                    min="0"
                    max="1"
                    step="0.01"
                    value={beta}
                    onChange={(e) => setBeta(parseFloat(e.target.value))}
                    disabled={isRunning || continuousRunning}
                  />
                </label>
              </div>
              </div>

              <div className="action-buttons">
                <button onClick={runSingleStep} disabled={isRunning || continuousRunning}>
                  Single Step
                </button>
                <div className="run-steps-container">
                  <button onClick={runManySteps} disabled={isRunning || continuousRunning}>
                    Run {numSteps.toLocaleString()} Steps
                  </button>
                  <select
                    value={numSteps}
                    onChange={(e) => setNumSteps(parseInt(e.target.value))}
                    disabled={isRunning || continuousRunning}
                  >
                    <option value={1000}>1k</option>
                    <option value={10000}>10k</option>
                    <option value={100000}>100k</option>
                    <option value={1000000}>1M</option>
                  </select>
                </div>
              <button
                onClick={toggleContinuousRun}
                disabled={isRunning}
                className={continuousRunning ? 'running' : ''}
              >
                {continuousRunning ? 'Stop' : 'Run'}
              </button>
                <button onClick={resetGrid} disabled={isRunning || continuousRunning}>
                  Reset Grid
                </button>
              </div>

              <div className="step-counter">
                <div>Steps: {stepCount}</div>
                {acceptanceHistory.length > 0 && (
                  <div>
                    Acceptance Rate: {((acceptanceHistory.reduce((a, b) => a + b, 0) / acceptanceHistory.length) * 100).toFixed(1)}%
                    {acceptanceHistory.length < 1000 && ` (last ${acceptanceHistory.length})`}
                  </div>
                )}
              </div>
            </div>
          )}
        </div>

        <div className="grid-container">
          <div className="grid" style={{
            gridTemplateColumns: `repeat(${GRID_SIZE}, 30px)`,
            gridTemplateRows: `repeat(${GRID_SIZE}, 30px)`
          }}>
            {grid.map((row, i) =>
              row.map((cell, j) => {
                const isHighlighted = highlightedCell &&
                  highlightedCell.i === i && highlightedCell.j === j;
                const displayType = isHighlighted ? highlightedCell.newType : cell;

                // Check if this cell is in the flashing cells list
                const flashingCell = flashingCells.find(fc => fc.i === i && fc.j === j);

                let className = 'cell';
                if (isHighlighted) {
                  className += ' highlighted';
                }
                if (acceptanceFlash && isHighlighted) {
                  className += acceptanceFlash === 'accept' ? ' flash-accept' : ' flash-reject';
                }
                if (flashingCell) {
                  className += flashingCell.type === 'accept' ? ' continuous-flash-accept' : ' continuous-flash-reject';
                }

                return (
                  <div
                    key={`${i}-${j}`}
                    className={className}
                  >
                    {CELL_EMOJIS[displayType]}
                  </div>
                );
              })
            )}
          </div>

          <div className="legend">
            <div className="legend-item">
              <span>🌱 Grass</span>
            </div>
            <div className="legend-item">
              <span>🌲 Tree</span>
            </div>
            <div className="legend-item">
              <span>💧 Lake</span>
            </div>
          </div>

          {stepDetails && (
            <div className="step-details">
              <h3>Step Details</h3>
              <div className="neighborhood-grids">
                <div className="neighborhood-section">
                  <h4>Before</h4>
                  <div className="mini-grid">
                    {[-1, 0, 1].map(di => (
                      <div key={di} className="mini-row">
                        {[-1, 0, 1].map(dj => {
                          const ni = stepDetails.i + di;
                          const nj = stepDetails.j + dj;
                          const isCenter = di === 0 && dj === 0;
                          const isValid = ni >= 0 && ni < GRID_SIZE && nj >= 0 && nj < GRID_SIZE;
                          const cellType = isValid ? grid[ni][nj] : null;

                          return (
                            <div key={dj} className={`mini-cell ${isCenter ? 'center-cell' : ''}`}>
                              {isValid ? CELL_EMOJIS[isCenter ? stepDetails.oldType : cellType] : ''}
                            </div>
                          );
                        })}
                      </div>
                    ))}
                  </div>
                </div>

                <div className="arrow">→</div>

                <div className="neighborhood-section">
                  <h4>After (Proposed)</h4>
                  <div className="mini-grid">
                    {[-1, 0, 1].map(di => (
                      <div key={di} className="mini-row">
                        {[-1, 0, 1].map(dj => {
                          const ni = stepDetails.i + di;
                          const nj = stepDetails.j + dj;
                          const isCenter = di === 0 && dj === 0;
                          const isValid = ni >= 0 && ni < GRID_SIZE && nj >= 0 && nj < GRID_SIZE;
                          const cellType = isValid ? grid[ni][nj] : null;

                          return (
                            <div key={dj} className={`mini-cell ${isCenter ? 'center-cell proposed' : ''}`}>
                              {isValid ? CELL_EMOJIS[isCenter ? stepDetails.newType : cellType] : ''}
                            </div>
                          );
                        })}
                      </div>
                    ))}
                  </div>
                </div>
              </div>

              <div className="weight-ratio">
                <h4>Acceptance Ratio</h4>
                <div className="ratio-formula">
                  <BlockMath math={stepDetails.latexCombined} />
                </div>
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

export default App;
