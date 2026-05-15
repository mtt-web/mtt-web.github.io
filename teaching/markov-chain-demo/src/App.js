import React, { useMemo, useState } from "react";
import "./App.css";

// HMM Road Map-Matching Demo (fully client-side, no deps)
// - Manhattan grid of intersections (cells). Roads along 4-neighbor graph.
// - Simulate a discrete-time path that moves on roads, with waits at intersections,
//   and finally "parks" off-road in a garage (observation off the grid).
// - Generate noisy observations (Gaussian in R^2).
// - Decode with two models:
//   (A) position-only HMM (states = cells),
//   (B) position+velocity HMM (states = (cell, dir) with dir in {Stay,N,E,S,W}).
// - Visualize true, observed, and decoded paths; tweak noise and grid size.

// -------------------- Utilities --------------------
function lcg(seed) {
  // Simple deterministic RNG for reproducibility across runs
  let s = seed >>> 0;
  return () => ((s = (s * 1664525 + 1013904223) >>> 0) / 0xffffffff);
}

function gaussian01(rand) {
  // Box-Muller
  let u = 0, v = 0;
  while (u === 0) u = rand();
  while (v === 0) v = rand();
  return Math.sqrt(-2.0 * Math.log(u)) * Math.cos(2.0 * Math.PI * v);
}

// -------------------- Grid construction --------------------
function buildGrid(grid) {
  const cells = [];
  const id = (i, j) => i + j * grid;
  const center = (i, j) => ({ x: i, y: j });
  for (let j = 0; j < grid; j++) {
    for (let i = 0; i < grid; i++) cells.push(center(i, j));
  }
  const edges = [];
  for (let j = 0; j < grid; j++) {
    for (let i = 0; i < grid; i++) {
      const a = id(i, j);
      if (i + 1 < grid) edges.push([a, id(i + 1, j)]);
      if (j + 1 < grid) edges.push([a, id(i, j + 1)]);
    }
  }
  // adjacency
  const adj = Array.from({ length: grid * grid }, () => []);
  edges.forEach(([a, b]) => { adj[a].push(b); adj[b].push(a); });
  return { cells, edges, adj, id };
}

// -------------------- Path simulation --------------------
function simulate(params) {
  const { grid, steps, noiseSigma, seed, waitProb, turnProb, parkLen } = params;
  const { cells, id } = buildGrid(grid);
  const rng = lcg(seed);

  // start near (1,1)
  let i = 1, j = 1;
  let dir = 2; // 0=Stay,1=N,2=E,3=S,4=W; start east
  const dirVec = {
    0: { x: 0, y: 0 },
    1: { x: 0, y: -1 },
    2: { x: 1, y: 0 },
    3: { x: 0, y: 1 },
    4: { x: -1, y: 0 },
  };

  const inBounds = (ii, jj) => (ii >= 0 && jj >= 0 && ii < grid && jj < grid);
  const moveForward = () => {
    const v = dirVec[dir];
    const ni = i + v.x, nj = j + v.y;
    if (dir === 0) return false;
    if (inBounds(ni, nj)) { i = ni; j = nj; return true; }
    return false;
  };

  const chooseTurn = () => {
    // choose a new direction among feasible ones (incl. stay with small prob)
    const options = [];
    const feas = (d) => {
      const v = dirVec[d];
      const ni = i + v.x, nj = j + v.y;
      return inBounds(ni, nj);
    };
    if (feas(1)) options.push(1);
    if (feas(2)) options.push(2);
    if (feas(3)) options.push(3);
    if (feas(4)) options.push(4);
    // Always allow staying
    options.push(0);
    // remove current dir to force a turn when requested
    const filtered = options.filter(d => d !== dir);
    const pick = filtered[Math.floor(rng() * filtered.length)] ?? 0;
    dir = pick;
  };

  const pathCells = [];
  const truePos = [];

  for (let t = 0; t < steps - parkLen; t++) {
    // decide to wait
    if (rng() < waitProb) {
      dir = 0; // stay
    } else {
      // maybe keep direction, else turn
      if (dir === 0 || rng() < turnProb || !moveForward()) {
        chooseTurn();
        moveForward(); // attempt after turning; ok if stay chosen
      }
    }
    pathCells.push(id(i, j));
    truePos.push({ x: i, y: j }); // on-road center
  }

  // parking in a garage off-road next to current cell
  const last = { x: i, y: j };
  const garage = { x: last.x + 0.45, y: last.y + 0.45 }; // diagonally off-road
  for (let t = 0; t < parkLen; t++) {
    pathCells.push(id(i, j)); // still consider the on-road cell as ground truth cell
    truePos.push(garage); // but the true *position* wanders to the garage
  }

  // observations: truePos + Gaussian noise
  const obs = truePos.map(p => ({
    x: p.x + gaussian01(rng) * noiseSigma,
    y: p.y + gaussian01(rng) * noiseSigma
  }));

  return { grid, cells, trueCells: pathCells, truePos, obs };
}

// -------------------- HMMs --------------------
function logEmission(pos, obs, sigma) {
  const dx = pos.x - obs.x, dy = pos.y - obs.y;
  const r2 = dx * dx + dy * dy;
  const logNorm = -Math.log(2 * Math.PI * sigma * sigma);
  return logNorm - 0.5 * r2 / (sigma * sigma);
}

function viterbiPositionOnly(sim, sigma) {
  // States = grid cells; transitions: stay or 4-neighbors uniformly
  const N = sim.grid * sim.grid;
  const id = (i, j) => i + j * sim.grid;
  const inB = (i, j) => i >= 0 && j >= 0 && i < sim.grid && j < sim.grid;

  const neighbors = Array.from({ length: N }, () => []);
  for (let j = 0; j < sim.grid; j++) {
    for (let i = 0; i < sim.grid; i++) {
      const a = id(i, j);
      if (inB(i + 1, j)) neighbors[a].push(id(i + 1, j));
      if (inB(i - 1, j)) neighbors[a].push(id(i - 1, j));
      if (inB(i, j + 1)) neighbors[a].push(id(i, j + 1));
      if (inB(i, j - 1)) neighbors[a].push(id(i, j - 1));
    }
  }

  const T = sim.obs.length;
  const stayProb = 0.15;
  const V = Array.from({ length: T }, () => new Float64Array(N).fill(-1e300));
  const B = Array.from({ length: T }, () => new Int32Array(N).fill(-1));

  // prior ~ proximity to first obs
  const obs0 = sim.obs[0];
  const pri = new Float64Array(N);
  for (let s = 0; s < N; s++) pri[s] = logEmission(sim.cells[s], obs0, sigma);

  for (let s = 0; s < N; s++) V[0][s] = pri[s];

  for (let t = 1; t < T; t++) {
    for (let s2 = 0; s2 < N; s2++) {
      let best = -1e300, arg = -1;
      const neigh = neighbors[s2];
      // incoming from self (stay)
      const pStay = Math.log(stayProb);
      if (V[t - 1][s2] + pStay > best) { best = V[t - 1][s2] + pStay; arg = s2; }
      // incoming from neighbors (move)
      const pMoveEach = Math.log((1 - stayProb) / Math.max(1, neigh.length));
      for (const s1 of neigh) {
        const sc = V[t - 1][s1] + pMoveEach;
        if (sc > best) { best = sc; arg = s1; }
      }
      V[t][s2] = best + logEmission(sim.cells[s2], sim.obs[t], sigma);
      B[t][s2] = arg;
    }
  }

  // backtrack
  let last = 0;
  for (let s = 1; s < N; s++) if (V[T - 1][s] > V[T - 1][last]) last = s;
  const path = new Array(T);
  path[T - 1] = last;
  for (let t = T - 1; t > 0; t--) path[t - 1] = (last = B[t][last]);
  return path; // sequence of cell indices
}

function viterbiPosVel(sim, sigma) {
  // States = (cell, dir) with dir in {0:Stay,1:N,2:E,3:S,4:W}
  const G = sim.grid;
  const Ncells = G * G;
  const K = 5;
  const S = Ncells * K;
  const id = (i, j) => i + j * G;
  const ij = (n) => [n % G, Math.floor(n / G)];
  const inB = (i, j) => i >= 0 && j >= 0 && i < G && j < G;

  const neighIdx = (c, d) => {
    const [i, j] = ij(c);
    if (d === 1 && inB(i, j - 1)) return id(i, j - 1); // N (y-1 for SVG up)
    if (d === 2 && inB(i + 1, j)) return id(i + 1, j); // E
    if (d === 3 && inB(i, j + 1)) return id(i, j + 1); // S
    if (d === 4 && inB(i - 1, j)) return id(i - 1, j); // W
    if (d === 0) return c; // stay
    return -1;
  };

  // Transition params (sticky motion)
  const pKeep = 0.82, pTurn = 0.12, pStay = 0.06;

  // Precompute incoming arcs for efficiency
  const incoming = Array.from({ length: S }, () => []);
  const inLogP = Array.from({ length: S }, () => []);

  for (let c = 0; c < Ncells; c++) {
    for (let d = 0; d < K; d++) {
      const s1 = c * K + d;
      // candidates from s1 -> s2
      const cand = [];
      // keep
      if (d !== 0) {
        const c2 = neighIdx(c, d);
        if (c2 >= 0) cand.push({ d2: d, c2, w: pKeep });
      }
      // turns (to other feasible directions)
      const feasibleDirs = [1, 2, 3, 4].filter(dd => dd !== d && neighIdx(c, dd) >= 0);
      const share = feasibleDirs.length > 0 ? pTurn / feasibleDirs.length : 0;
      for (const dd of feasibleDirs) {
        cand.push({ d2: dd, c2: neighIdx(c, dd), w: share });
      }
      // stay
      cand.push({ d2: 0, c2: c, w: pStay });

      // Normalize & register incoming to s2
      const Z = cand.reduce((a, b) => a + b.w, 0) || 1;
      for (const { d2, c2, w } of cand) {
        const s2 = c2 * K + d2;
        incoming[s2].push(s1);
        inLogP[s2].push(Math.log(w / Z));
      }
    }
  }

  // Emission log-likelihood per (cell) — velocity-independent
  const T = sim.obs.length;
  const Em = Array.from({ length: T }, () => new Float64Array(Ncells));
  for (let t = 0; t < T; t++) {
    for (let c = 0; c < Ncells; c++) {
      Em[t][c] = logEmission(sim.cells[c], sim.obs[t], sigma);
    }
  }

  // Viterbi DP over S states
  const V = Array.from({ length: T }, () => new Float64Array(S).fill(-1e300));
  const B = Array.from({ length: T }, () => new Int32Array(S).fill(-1));

  // Prior: proximity to first obs, spread over directions (stay favored)
  for (let c = 0; c < Ncells; c++) {
    const base = Em[0][c];
    V[0][c * K + 0] = base + Math.log(0.4);
    for (let d = 1; d < K; d++) V[0][c * K + d] = base + Math.log(0.6 / 4);
  }

  for (let t = 1; t < T; t++) {
    for (let s2 = 0; s2 < S; s2++) {
      let best = -1e300, arg = -1;
      const inc = incoming[s2];
      const w = inLogP[s2];
      for (let k = 0; k < inc.length; k++) {
        const s1 = inc[k];
        const sc = V[t - 1][s1] + w[k];
        if (sc > best) { best = sc; arg = s1; }
      }
      const c2 = Math.floor(s2 / K);
      V[t][s2] = best + Em[t][c2];
      B[t][s2] = arg;
    }
  }

  // Backtrack
  let last = 0;
  for (let s = 1; s < S; s++) if (V[T - 1][s] > V[T - 1][last]) last = s;
  const pathS = new Array(T);
  pathS[T - 1] = last;
  for (let t = T - 1; t > 0; t--) pathS[t - 1] = (last = B[t][last]);
  const cellsPath = pathS.map(s => Math.floor(s / K));
  return cellsPath;
}

// -------------------- Visualization --------------------
const cellSize = 28; // px
const pad = 32; // px around the grid

function toPx(p) {
  return { x: pad + p.x * cellSize, y: pad + p.y * cellSize };
}

function polyline(points) {
  return points.map(p => `${p.x},${p.y}`).join(" ");
}

function Stats({ label, match, meanErr }) {
  return (
    <div className="stats-box">
      <div className="stats-label">{label}</div>
      <div>Node match: {(100 * match).toFixed(1)}%</div>
      <div>Mean |Δpos|: {meanErr.toFixed(3)} cells</div>
    </div>
  );
}

export default function HMMDemo() {
  const [grid, setGrid] = useState(12);
  const [steps, setSteps] = useState(220);
  const [sigma, setSigma] = useState(0.18);
  const [seed, setSeed] = useState(12345);
  const [waitProb, setWaitProb] = useState(0.2);
  const [turnProb, setTurnProb] = useState(0.25);
  const [parkLen, setParkLen] = useState(10);

  const sim = useMemo(() => {
    const params = { grid, steps, noiseSigma: sigma, seed, waitProb, turnProb, parkLen };
    return simulate(params);
  }, [grid, steps, sigma, seed, waitProb, turnProb, parkLen]);

  const decA = useMemo(() => viterbiPositionOnly(sim, sigma), [sim, sigma]);
  const decB = useMemo(() => viterbiPosVel(sim, sigma), [sim, sigma]);

  // Metrics
  const trueCells = sim.trueCells;
  const matchA = decA.reduce((acc, c, t) => acc + (c === trueCells[t] ? 1 : 0), 0) / decA.length;
  const matchB = decB.reduce((acc, c, t) => acc + (c === trueCells[t] ? 1 : 0), 0) / decB.length;
  const meanErr = (path) => {
    let s = 0;
    for (let t = 0; t < path.length; t++) {
      const pa = sim.cells[path[t]];
      const tr = sim.truePos[t];
      const dx = pa.x - tr.x, dy = pa.y - tr.y;
      s += Math.hypot(dx, dy);
    }
    return s / path.length;
  };
  const errA = meanErr(decA), errB = meanErr(decB);

  // SVG geometry
  const W = pad * 2 + (grid - 1) * cellSize;
  const H = pad * 2 + (grid - 1) * cellSize;

  const obsPts = sim.obs.map(toPx);
  const truePts = sim.truePos.map(toPx);
  const decPtsA = decA.map(i => toPx(sim.cells[i]));
  const decPtsB = decB.map(i => toPx(sim.cells[i]));

  return (
    <div className="app-container">
      <div className="content-grid">
        <div className="controls-section">
          <h1 className="title">HMM Map‑Matching Demo</h1>
          <p className="description">
            Simulated car on a Manhattan grid. We observe noisy positions (orange dots). Two HMMs infer the
            most likely cell sequence: <span className="highlight">position‑only</span> (blue) and
            <span className="highlight"> position+velocity</span> (purple). At the very end, the car parks off‑road in a garage; the observation drifts off the grid, but the HMM keeps a coherent last on‑road cell.
          </p>

          <div className="controls-grid">
            <label className="control-label">
              Grid size
              <input
                type="range"
                min={6}
                max={20}
                value={grid}
                onChange={e => setGrid(parseInt(e.target.value))}
                className="slider"
              />
              <div className="control-value">{grid} × {grid}</div>
            </label>
            <label className="control-label">
              Steps
              <input
                type="range"
                min={80}
                max={400}
                value={steps}
                onChange={e => setSteps(parseInt(e.target.value))}
                className="slider"
              />
              <div className="control-value">{steps} s</div>
            </label>
            <label className="control-label">
              Noise σ
              <input
                type="range"
                min={0.02}
                max={0.35}
                step={0.01}
                value={sigma}
                onChange={e => setSigma(parseFloat(e.target.value))}
                className="slider"
              />
              <div className="control-value">{sigma.toFixed(2)} cells</div>
            </label>
            <label className="control-label">
              Seed
              <input
                type="number"
                value={seed}
                onChange={e => setSeed(parseInt(e.target.value) || 0)}
                className="number-input"
              />
            </label>
            <label className="control-label">
              Wait prob
              <input
                type="range"
                min={0}
                max={0.6}
                step={0.01}
                value={waitProb}
                onChange={e => setWaitProb(parseFloat(e.target.value))}
                className="slider"
              />
              <div className="control-value">{waitProb.toFixed(2)}</div>
            </label>
            <label className="control-label">
              Turn prob
              <input
                type="range"
                min={0}
                max={0.8}
                step={0.01}
                value={turnProb}
                onChange={e => setTurnProb(parseFloat(e.target.value))}
                className="slider"
              />
              <div className="control-value">{turnProb.toFixed(2)}</div>
            </label>
            <label className="control-label">
              Park steps
              <input
                type="range"
                min={0}
                max={30}
                step={1}
                value={parkLen}
                onChange={e => setParkLen(parseInt(e.target.value))}
                className="slider"
              />
              <div className="control-value">{parkLen} s</div>
            </label>
          </div>

          <div className="stats-container">
            <Stats label="Position‑only HMM" match={matchA} meanErr={errA} />
            <Stats label="Pos+Velocity HMM" match={matchB} meanErr={errB} />
          </div>

          <ul className="info-list">
            <li>Transitions (A): stay with p=0.15, else move uniformly to 4‑neighbors.</li>
            <li>Transitions (B): keep direction 0.82, turn 0.12 (split), stay 0.06.</li>
            <li>Emissions: isotropic Gaussian in 2D; σ = slider value.</li>
          </ul>
        </div>

        <div className="viz-section">
          <svg width={W} height={H} className="svg-canvas">
            {/* Roads as thin gray lines */}
            {Array.from({ length: grid }, (_, j) => (
              <line
                key={"h" + j}
                x1={pad}
                y1={pad + j * cellSize}
                x2={pad + (grid - 1) * cellSize}
                y2={pad + j * cellSize}
                stroke="#cbd5e1"
                strokeWidth={1}
              />
            ))}
            {Array.from({ length: grid }, (_, i) => (
              <line
                key={"v" + i}
                x1={pad + i * cellSize}
                y1={pad}
                x2={pad + i * cellSize}
                y2={pad + (grid - 1) * cellSize}
                stroke="#cbd5e1"
                strokeWidth={1}
              />
            ))}

            {/* Observations */}
            <g opacity={0.8}>
              {obsPts.map((p, idx) => (
                <circle
                  key={"o" + idx}
                  cx={p.x}
                  cy={p.y}
                  r={2.2}
                  fill="#f59e0b"
                />
              ))}
            </g>

            {/* True path */}
            <polyline
              points={polyline(truePts)}
              fill="none"
              stroke="#10b981"
              strokeWidth={2}
              strokeOpacity={0.9}
            />

            {/* Decoded A (position-only) */}
            <polyline
              points={polyline(decPtsA)}
              fill="none"
              stroke="#3b82f6"
              strokeWidth={2.5}
              strokeOpacity={0.8}
            />

            {/* Decoded B (pos+vel) */}
            <polyline
              points={polyline(decPtsB)}
              fill="none"
              stroke="#8b5cf6"
              strokeWidth={2}
              strokeOpacity={0.9}
            />

            {/* Legend */}
            <g>
              <rect
                x={pad}
                y={8}
                width={280}
                height={60}
                rx={10}
                ry={10}
                fill="#ffffffcc"
              />
              <g transform={`translate(${pad + 12}, ${20})`}>
                <line x1={0} y1={6} x2={18} y2={6} stroke="#10b981" strokeWidth={2} />
                <text x={26} y={10} fontSize="12" fill="#0f172a">True path</text>
              </g>
              <g transform={`translate(${pad + 12}, ${38})`}>
                <line x1={0} y1={6} x2={18} y2={6} stroke="#3b82f6" strokeWidth={2.5} />
                <text x={26} y={10} fontSize="12" fill="#0f172a">Decoded (pos‑only)</text>
              </g>
              <g transform={`translate(${pad + 12}, ${56})`}>
                <line x1={0} y1={6} x2={18} y2={6} stroke="#8b5cf6" strokeWidth={2} />
                <text x={26} y={10} fontSize="12" fill="#0f172a">Decoded (pos+vel)</text>
              </g>
            </g>
          </svg>
        </div>
      </div>

      <div className="footer-tip">
        Tip: increase Noise σ and compare how the velocity-aware model resists zig‑zagging and keeps a coherent route. Set Park steps ≥ 5 to see the garage effect.
      </div>
    </div>
  );
}
