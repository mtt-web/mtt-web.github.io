import React from 'react';
import './App.css';
import 'katex/dist/katex.min.css';
import { BlockMath } from 'react-katex';
import Landscape3D from './Landscape3D.jsx';
import FixedCellsUI from './FixedCellsUI.jsx';

const GRID_SIZE = 15;
const HEIGHTS = Array.from({ length: 10 }, (_, i) => i); // 0..9

const DEFAULT_FIXED = [
  { i: 0, j: 0, h: 1 },
  { i: 7, j: 7, h: 5 },
  { i: 0, j: 14, h: 9 },
  { i: 10, j: 0, h: 8 },
  { i: 13, j: 13, h: 1 },
  { i: 14, j: 0, h: 4 }
];

function initializeGridWithFixed(fixedList) {
  const grid = [];
  for (let i = 0; i < GRID_SIZE; i++) {
    const row = [];
    for (let j = 0; j < GRID_SIZE; j++) {
      row.push(0);
    }
    grid.push(row);
  }
  if (Array.isArray(fixedList)) {
    for (const { i, j, h } of fixedList) {
      if (i >= 0 && i < GRID_SIZE && j >= 0 && j < GRID_SIZE) {
        grid[i][j] = h;
      }
    }
  }
  return grid;
}

export default function App() {
  const [grid, setGrid] = React.useState(() => initializeGridWithFixed(DEFAULT_FIXED));
  const [fixedCells, setFixedCells] = React.useState(DEFAULT_FIXED); // [{i,j,h}]
  const [isRunning, setIsRunning] = React.useState(false);
  const [numSteps, setNumSteps] = React.useState(10000);
  const [continuous, setContinuous] = React.useState(false);
  const [stepCount, setStepCount] = React.useState(0);
  const [highlight, setHighlight] = React.useState(null); // {i,j,newH}
  const [flash, setFlash] = React.useState(null); // 'accept'|'reject'|null
  const [beta, setBeta] = React.useState(0.5); // temperature parameter in [0,1]
  const [stepDetails, setStepDetails] = React.useState(null); // details of last single step
  const [stepsPerTick, setStepsPerTick] = React.useState(1); // continuous speed

  // Ensure newly added fixed cells are immediately enforced in the current grid
  const addFixedCell = React.useCallback((ii, jj, hh) => {
    setFixedCells(prev => {
      const others = prev.filter(c => !(c.i === ii && c.j === jj));
      return [...others, { i: ii, j: jj, h: hh }];
    });
    setGrid(prev => {
      if (ii < 0 || ii >= GRID_SIZE || jj < 0 || jj >= GRID_SIZE) return prev;
      const g = prev.map(r => [...r]);
      g[ii][jj] = hh;
      return g;
    });
  }, []);

  // no independent sampling mode

  function localLogProb(gridRef, i, j) {
    // Sum of log weights for edges touching (i,j): log w = -|hi-hj|
    const h = gridRef[i][j];
    let sum = 0;
    const nbrs = [
      [i - 1, j],
      [i + 1, j],
      [i, j - 1],
      [i, j + 1]
    ];
    for (const [ni, nj] of nbrs) {
      if (ni >= 0 && ni < GRID_SIZE && nj >= 0 && nj < GRID_SIZE) {
        const diff = h - gridRef[ni][nj];
        sum += -(diff * diff); // log e^{-(h-h')^2}
      }
    }
    return sum;
  }

  const metropolisStep = React.useCallback(() => {
    const i = Math.floor(Math.random() * GRID_SIZE);
    const j = Math.floor(Math.random() * GRID_SIZE);
    // Skip fixed cells
    if (fixedCells.some(fc => fc.i === i && fc.j === j)) {
      setStepCount(s => s + 1);
      return;
    }
    const oldH = grid[i][j];
    // Proposal: cyclic ±1 (mod 10) → exactly two neighbors per state
    const r = Math.random();
    const newH = r < 0.5 ? (oldH + 1) % 10 : (oldH + 9) % 10;

    const before = localLogProb(grid, i, j);
    const newGrid = grid.map(r => [...r]);
    newGrid[i][j] = newH;
    const after = localLogProb(newGrid, i, j);

    const delta = after - before;
    const acceptProb = Math.min(1, Math.exp(beta * delta));
    const accepted = Math.random() < acceptProb;
    if (accepted) setGrid(newGrid);

    setHighlight({ i, j, newH });
    setStepCount(s => s + 1);
    // Build step details for visualization
    const neighbors = [
      [i - 1, j], [i, j + 1], [i + 1, j], [i, j - 1]
    ];
    const neighborHeights = neighbors.map(([ni, nj]) => (
      ni >= 0 && ni < GRID_SIZE && nj >= 0 && nj < GRID_SIZE ? grid[ni][nj] : null
    ));
    // Save a snapshot of the full 3x3 neighborhood for display
    const gridSnapshot = {};
    for (let di = -1; di <= 1; di++) {
      for (let dj = -1; dj <= 1; dj++) {
        const ni = i + di;
        const nj = j + dj;
        if (ni >= 0 && ni < GRID_SIZE && nj >= 0 && nj < GRID_SIZE) {
          gridSnapshot[`${di},${dj}`] = grid[ni][nj];
        }
      }
    }
    const oldTerms = neighborHeights.map(hn => hn == null ? null : Math.pow(grid[i][j] - hn, 2));
    const newTerms = neighborHeights.map(hn => hn == null ? null : Math.pow(newH - hn, 2));
    const sumDiff = newTerms.reduce((s, t, idx) => s + (t == null ? 0 : (t - oldTerms[idx])), 0);
    const ratio = Math.exp(-beta * sumDiff) * Math.exp(beta * 0); // explicit to mirror derivation
    const accShown = Math.min(1, ratio);
    const betaStr = beta.toFixed(2);
    const termsSymbolic = neighborHeights
      .map(hn => (hn == null ? null : `( ( ${newH} - ${hn} )^2 - ( ${grid[i][j]} - ${hn} )^2 )`))
      .filter(Boolean)
      .join(' + ');
    const latex = `\\begin{aligned}
      &\np(y)/p(x) = \\hat{p}(y)/\\hat{p}(x) \\\\
      &= \\frac{\\prod e^{-\\beta (h'_c - h_n)^2}}{\\prod e^{-\\beta (h_c - h_n)^2}} \\\\
      &= e^{-\\beta \\sum ( ( ${newH} - h_n )^2 - ( ${grid[i][j]} - h_n )^2 )} \\\\
      &= e^{- ${betaStr} \\cdot (${termsSymbolic})} \\\\
      &= ${Number.isFinite(ratio) ? ratio.toFixed(3) : '0.000'} \\\\[6pt]
      \\text{acceptance probability} &= \\min(1, ${Number.isFinite(ratio) ? ratio.toFixed(3) : '0.000'}) = ${accShown.toFixed(3)} ${accepted ? '\\text{ (Accepted ✓)}' : '\\text{ (Rejected ×)}'}
    \\end{aligned}`;
    setStepDetails({ i, j, oldH, newH, accepted, neighbors, neighborHeights, oldTerms, newTerms, ratio, acceptProb: accShown, latex, gridSnapshot });
    setTimeout(() => setFlash(accepted ? 'accept' : 'reject'), 50);
    setTimeout(() => { setHighlight(null); setFlash(null); }, 300);
  }, [grid, fixedCells, beta]);

  const runMany = React.useCallback(() => {
    setIsRunning(true);
    let g = grid.map(r => [...r]);
    for (let s = 0; s < numSteps; s++) {
      const i = Math.floor(Math.random() * GRID_SIZE);
      const j = Math.floor(Math.random() * GRID_SIZE);
      if (fixedCells.some(fc => fc.i === i && fc.j === j)) continue;
      const oldH = g[i][j];
      // Cyclic ±1 (mod 10)
      const r = Math.random();
      const newH = r < 0.5 ? (oldH + 1) % 10 : (oldH + 9) % 10;
      // local delta
      let before = 0, after = 0;
      const nbrs = [
        [i - 1, j], [i + 1, j], [i, j - 1], [i, j + 1]
      ];
      for (const [ni, nj] of nbrs) {
        if (ni >= 0 && ni < GRID_SIZE && nj >= 0 && nj < GRID_SIZE) {
          const dOld = oldH - g[ni][nj];
          const dNew = newH - g[ni][nj];
          before += -(dOld * dOld);
          after += -(dNew * dNew);
        }
      }
      const delta = after - before;
      if (Math.random() < Math.min(1, Math.exp(beta * delta))) {
        g[i][j] = newH;
      }
    }
    setGrid(g);
    setStepCount(s => s + numSteps);
    setIsRunning(false);
  }, [grid, numSteps, fixedCells, beta]);

  React.useEffect(() => {
    if (!continuous) return;
    const id = setInterval(() => {
      for (let k = 0; k < stepsPerTick; k++) metropolisStep();
    }, 100);
    return () => clearInterval(id);
  }, [continuous, metropolisStep, stepsPerTick]);

  // display-only formula string not used

  return (
    <div className="App">
      <h1>Landscape generation via Metropolis algorithm</h1>

      <div className="content">
        <div className="controls">
            <>
              <div className="formula-box">
                Given preset heights at selected positions, we complete the remaining cells to form a smooth landscape using the Metropolis algorithm.
              </div>
              <div className="formula-box">
                <div>Sampling from:</div>
                <BlockMath math={"p(\\text{grid}) \\propto \\prod_{\\text{edge neighbors}} e^{-\\beta (h_i - h_j)^2}"} />
                <div>Unnormalized probability:</div>
                <BlockMath math={"\\hat{p}(\\text{grid}) = \\prod_{\\text{edge neighbors}} e^{-\\beta (h_i - h_j)^2}"} />
              </div>
              <div className="control-group">
                <label>
                  β (inverse temperature): {beta.toFixed(2)}
                  <input
                    type="range"
                    min="0"
                    max="2"
                    step="0.01"
                    value={beta}
                    onChange={(e) => setBeta(parseFloat(e.target.value))}
                    disabled={isRunning || continuous}
                  />
                </label>
              </div>
              
              <FixedCellsUI fixedCells={fixedCells} setFixedCells={setFixedCells} addFixedCell={addFixedCell} />
              <div className="action-buttons">
                <div style={{ fontWeight: 600, marginBottom: 6 }}>Sampling by Metropolis algorithm</div>
                <button onClick={metropolisStep} disabled={isRunning || continuous}>Single Step</button>
                <div className="run-steps-container">
                  <button onClick={runMany} disabled={isRunning || continuous}>Run {numSteps.toLocaleString()} Steps</button>
                  <select value={numSteps} onChange={(e)=>setNumSteps(parseInt(e.target.value))} disabled={isRunning || continuous}>
                    <option value={1000}>1k</option>
                    <option value={10000}>10k</option>
                    <option value={100000}>100k</option>
                    <option value={1000000}>1M</option>
                  </select>
                </div>
                <div className="run-steps-container">
                  <button onClick={()=>setContinuous(c=>!c)} className={continuous?'running':''} disabled={isRunning}>{continuous?'Stop':'Run'}</button>
                  <label style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                    Speed: {stepsPerTick}
                    <input
                      type="range"
                      min="1"
                      max="100"
                      step="1"
                      value={stepsPerTick}
                      onChange={(e) => setStepsPerTick(parseInt(e.target.value, 10))}
                      disabled={isRunning}
                    />
                  </label>
                </div>
                <button onClick={()=>{ setGrid(initializeGridWithFixed(fixedCells)); setStepCount(0); setHighlight(null); setFlash(null); }} disabled={isRunning || continuous}>Reset Grid</button>
              </div>
              
            </>
        </div>

        <div className="grid-container">
          <Landscape3D grid={grid} />
          {stepDetails && (
            <div className="step-details">
              <h3>Step Details</h3>
              <div>Cell: ({stepDetails.i}, {stepDetails.j})</div>
              <div>Proposal: change height from {stepDetails.oldH} to {stepDetails.newH}</div>
              <div className="neighborhood-grids">
                <div className="neighborhood-section">
                  <h4>Before</h4>
                  <div className="mini-grid">
                    {[-1, 0, 1].map(di => (
                      <div key={di} className="mini-row">
                        {[-1, 0, 1].map(dj => {
                          const isCenter = di === 0 && dj === 0;
                          const hVal = isCenter ? stepDetails.oldH : stepDetails.gridSnapshot?.[`${di},${dj}`];
                          return (
                            <div key={dj} className={`mini-cell ${isCenter ? 'center-cell' : ''}`}>
                              {hVal != null ? hVal : ''}
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
                          const isCenter = di === 0 && dj === 0;
                          const hVal = isCenter ? stepDetails.newH : stepDetails.gridSnapshot?.[`${di},${dj}`];
                          return (
                            <div key={dj} className={`mini-cell ${isCenter ? 'center-cell' : ''}`}>
                              {hVal != null ? hVal : ''}
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
                  <BlockMath math={stepDetails.latex} />
                </div>
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}


