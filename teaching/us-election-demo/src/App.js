import React, { useEffect, useMemo, useRef, useState } from "react";
import Papa from "papaparse";
import { ComposableMap, Geographies, Geography } from "react-simple-maps";
import usStates from "us-atlas/states-10m.json";
import { BarChart, Bar, XAxis, YAxis, Tooltip, ResponsiveContainer } from "recharts";
import 'katex/dist/katex.min.css';
import { BlockMath, InlineMath } from 'react-katex';

/***********************
 * SIMPLE METROPOLIS DEMO
 * - No national shift parameter (no μ₀)
 * - Latents are state margins μ_i (ppt)
 * - Prior: Laplace on swing from 2020 (μ_i - prev_i ~ Laplace(s*, b))
 * - Likelihoods: Gaussian for state polls and national polls
 * - National margin = weighted average of state margins: μ_nat = Σ w_i μ_i
 * - Weights default to Electoral Vote (quick proxy); change WEIGHTS if desired
 ***********************/

/********** constants (hard-coded for clarity) **********/
const SIGMA_BIAS = 2.0;     // extra poll noise (ppt)
const NAT_DEFAULT_N = 1000; // pretend national polls have n=1000 when unknown
const LAPLACE_B = 3.0;      // Laplace scale b (ppt)
const LAPLACE_MEAN = 0.0;   // mean swing s*
const PROPOSAL_SD = 0.5;    // MH proposal sd for each μ_i (ppt)

/********** utilities **********/
const clamp = (x, lo, hi) => Math.max(lo, Math.min(hi, x));
function gaussian01(){ let u=0,v=0; while(!u) u=Math.random(); while(!v) v=Math.random(); return Math.sqrt(-2*Math.log(u))*Math.cos(2*Math.PI*v); }
function marginToColor(m){ // blue ↔ red gradient within ±12ppt
  const x = clamp(m, -12, 12); const t=(x+12)/24; const c0=[0xd7,0x30,0x27], c1=[0x45,0x75,0xb4];
  const c=[0,1,2].map(i=>Math.round(c0[i]*(1-t)+c1[i]*t)); return `rgb(${c[0]},${c[1]},${c[2]})`;
}
function normalSeFromN(n){ return 100/Math.sqrt(Math.max(1,n)); } // ppt approximation
function sigmaFromN(n){ const s = normalSeFromN(n); return Math.sqrt(s*s + SIGMA_BIAS*SIGMA_BIAS); }
function logNormPdf(x, m, sd){ const z=(x-m)/sd; return -0.5*(z*z) - Math.log(sd) - 0.5*Math.log(2*Math.PI); }
function logLaplacePdf(x, m, b){ return -Math.log(2*b) - Math.abs(x-m)/b; }

/********** metadata **********/
const STATE_ORDER = ["AL","AK","AZ","AR","CA","CO","CT","DE","FL","GA","HI","ID","IL","IN","IA","KS","KY","LA","ME","MD","MA","MI","MN","MS","MO","MT","NE","NV","NH","NJ","NM","NY","NC","ND","OH","OK","OR","PA","RI","SC","SD","TN","TX","UT","VT","VA","WA","WV","WI","WY"];
const STATE_NAME = { AL:"Alabama",AK:"Alaska",AZ:"Arizona",AR:"Arkansas",CA:"California",CO:"Colorado",CT:"Connecticut",DE:"Delaware",FL:"Florida",GA:"Georgia",HI:"Hawaii",ID:"Idaho",IL:"Illinois",IN:"Indiana",IA:"Iowa",KS:"Kansas",KY:"Kentucky",LA:"Louisiana",ME:"Maine",MD:"Maryland",MA:"Massachusetts",MI:"Michigan",MN:"Minnesota",MS:"Mississippi",MO:"Missouri",MT:"Montana",NE:"Nebraska",NV:"Nevada",NH:"New Hampshire",NJ:"New Jersey",NM:"New Mexico",NY:"New York",NC:"North Carolina",ND:"North Dakota",OH:"Ohio",OK:"Oklahoma",OR:"Oregon",PA:"Pennsylvania",RI:"Rhode Island",SC:"South Carolina",SD:"South Dakota",TN:"Tennessee",TX:"Texas",UT:"Utah",VT:"Vermont",VA:"Virginia",WA:"Washington",WV:"West Virginia",WI:"Wisconsin",WY:"Wyoming" };
const EV_2016 = { AL:9,AK:3,AZ:11,AR:6,CA:55,CO:9,CT:7,DE:3,FL:29,GA:16,HI:4,ID:4,IL:20,IN:11,IA:6,KS:6,KY:8,LA:8,ME:4,MD:10,MA:11,MI:16,MN:10,MS:6,MO:10,MT:3,NE:5,NV:6,NH:4,NJ:14,NM:5,NY:29,NC:15,ND:3,OH:18,OK:7,OR:7,PA:20,RI:4,SC:9,SD:3,TN:11,TX:38,UT:6,VT:3,VA:13,WA:12,WV:5,WI:10,WY:3 };
const FIPS_TO_CODE = {"01":"AL","02":"AK","04":"AZ","05":"AR","06":"CA","08":"CO","09":"CT","10":"DE","12":"FL","13":"GA","15":"HI","16":"ID","17":"IL","18":"IN","19":"IA","20":"KS","21":"KY","22":"LA","23":"ME","24":"MD","25":"MA","26":"MI","27":"MN","28":"MS","29":"MO","30":"MT","31":"NE","32":"NV","33":"NH","34":"NJ","35":"NM","36":"NY","37":"NC","38":"ND","39":"OH","40":"OK","41":"OR","42":"PA","44":"RI","45":"SC","46":"SD","47":"TN","48":"TX","49":"UT","50":"VT","51":"VA","53":"WA","54":"WV","55":"WI","56":"WY"};

// Default 2020 margins (ppt, Dem-Rep). Realistic values (subset shown earlier); full list included here.
const DEFAULT_2020 = {"AL":-25.46,"AK":-10.06,"AZ":0.31,"AR":-27.63,"CA":29.15,"CO":13.49,"CT":20.25,"DE":19.00,"FL":-3.36,"GA":0.23,"HI":29.46,"ID":-30.69,"IL":16.98,"IN":-16.06,"IA":-8.20,"KS":-14.65,"KY":-25.91,"LA":-18.63,"ME":9.07,"MD":33.21,"MA":33.46,"MI":2.78,"MN":7.12,"MS":-16.55,"MO":-15.39,"MT":-16.36,"NE":-19.13,"NV":2.39,"NH":7.35,"NJ":15.94,"NM":10.79,"NY":23.12,"NC":-1.34,"ND":-33.37,"OH":-8.03,"OK":-33.08,"OR":16.13,"PA":1.16,"RI":20.80,"SC":-11.68,"SD":-26.23,"TN":-23.16,"TX":-5.58,"UT":-20.49,"VT":35.41,"VA":10.11,"WA":19.20,"WV":-38.93,"WI":0.63,"WY":-43.38};

// Default 2024 state polls (subset; you can upload your broader CSV to replace)
const DEFAULT_STATE_POLLS = [
  ["AZ",1468,47.0,49.0],["AZ",875,47.0,52.0],["AZ",1090,47.0,49.0],
  ["PA",2103,51.0,46.0],["PA",1840,49.0,50.0],["PA",1089,47.0,48.0],
  ["NV",1125,48.0,48.0],["NV",707,47.0,50.0],["NV",792,49.0,50.0],
  ["MI",1668,48.0,48.0],["MI",1113,48.0,50.0],["MI",450,49.0,47.0],
  ["WI",1549,47.0,47.0],["WI",869,48.0,49.0],
  ["NC",1600,48.0,49.0],["NC",1219,48.0,50.0],
  ["GA",1659,47.0,49.0],["GA",1112,48.0,50.0],
];

// Default national polls (margins in ppt; if n missing we will use NAT_DEFAULT_N)
const DEFAULT_NAT_POLLS = [
  { n: 973,  dem: 50.0, rep: 48.0 },
  { n: 1297, dem: 51.0, rep: 47.0 },
  { n: 1000, dem: 49.0, rep: 49.0 },
  { n: 2463, dem: 48.0, rep: 50.0 },
  { n: null, dem: 51.0, rep: 49.0 }, // will fallback to NAT_DEFAULT_N
];

// Will be populated from CSV on load (defaults to equal weights)
let WEIGHTS = Object.fromEntries(STATE_ORDER.map(s=>[s, 1.0/STATE_ORDER.length]));

/********** main component **********/
export default function USElectionMetropolisSimplified(){
  // Data state (can be replaced via CSV uploads)
  const [prevMargins, setPrevMargins] = useState(DEFAULT_2020); // {state: ppt}
  const [statePolls, setStatePolls]   = useState(()=> toStatePollRows(DEFAULT_STATE_POLLS)); // [{state, r, n}]
  const [natPolls, setNatPolls]       = useState(()=> toNatPollRows(DEFAULT_NAT_POLLS));     // [{r, n}]

  // Latents: μ_i for each state (ppt)
  const [mu, setMu] = useState(()=> STATE_ORDER.map(()=> 0)); // start neutral everywhere

  // MH bookkeeping
  const [iter, setIter] = useState(0);
  const [acceptRate, setAcceptRate] = useState(0);
  const acceptRef = useRef(0); const triedRef = useRef(0);
  const [running, setRunning] = useState(false);
  const [evSamples, setEvSamples] = useState([]);
  const [showLatents, setShowLatents] = useState(false);

  // Load CSV files on mount
  useEffect(() => {
    // Load 2020 margins
    fetch('/data/state_margins_2020_all_states.csv')
      .then(res => res.text())
      .then(csv => {
        Papa.parse(csv, { header: true, dynamicTyping: true, complete: res => {
          const map = {};
          for (const row of res.data) {
            if (!row) continue;
            const s = (row.state || "").toString().trim().toUpperCase();
            const m = Number(row.margin_dem_minus_rep_2020_pp);
            if (STATE_ORDER.includes(s) && Number.isFinite(m)) map[s] = m;
          }
          setPrevMargins(map);
        }});
      });

    // Load state polls
    fetch('/data/state_polls_2024_broader.csv')
      .then(res => res.text())
      .then(csv => {
        Papa.parse(csv, { header: true, dynamicTyping: true, complete: res => {
          const rows = [];
          for (const r of res.data) {
            if (!r) continue;
            const s = (r.state || "").toString().trim().toUpperCase();
            if (!STATE_ORDER.includes(s)) continue;
            const n = Number(r.sample_size);
            const margin = Number(r.margin_harris_minus_trump_pp);
            if (Number.isFinite(n) && Number.isFinite(margin)) {
              rows.push({ state: s, r: margin, n: n });
            }
          }
          if (rows.length > 0) setStatePolls(rows);
        }});
      });

    // Load national polls
    fetch('/data/national_polls_2024.csv')
      .then(res => res.text())
      .then(csv => {
        Papa.parse(csv, { header: true, dynamicTyping: true, complete: res => {
          const rows = [];
          for (const r of res.data) {
            if (!r) continue;
            const margin = Number(r.margin_harris_minus_trump_pp);
            if (Number.isFinite(margin)) {
              rows.push({ r: margin, n: null }); // n is null for national polls
            }
          }
          if (rows.length > 0) setNatPolls(rows);
        }});
      });

    // Load population shares (for national poll weights)
    fetch('/data/state_population_share.csv')
      .then(res => res.text())
      .then(csv => {
        Papa.parse(csv, { header: true, dynamicTyping: true, complete: res => {
          const map = {};
          for (const row of res.data) {
            if (!row) continue;
            const s = (row.state || "").toString().trim().toUpperCase();
            const pop = Number(row.population_share_pct);
            if (STATE_ORDER.includes(s) && Number.isFinite(pop)) map[s] = pop;
          }
          // Normalize to sum to 1
          const total = Object.values(map).reduce((acc, v) => acc + v, 0);
          if (total > 0) {
            for (const s in map) map[s] /= total;
            WEIGHTS = map;
          }
        }});
      });
  }, []);

  // Derived quantities
  const muNat = useMemo(()=> sumProduct(mu, STATE_ORDER.map(s=>WEIGHTS[s])), [mu]);
  const marginByState = useMemo(()=> Object.fromEntries(STATE_ORDER.map((s,i)=>[s, mu[i]])), [mu]);
  const evNow = useMemo(()=> currentEV(mu), [mu]);

  // MH loop
  useEffect(()=>{
    if(!running) return; let canceled=false;
    const stepper=()=>{
      if(canceled) return;
      const CHUNK=200; let curMu = mu.slice(); let curLogPost = logPosterior(curMu, prevMargins, statePolls, natPolls);
      for(let t=0;t<CHUNK;t++){
        const prop = curMu.map(m=> m + PROPOSAL_SD*gaussian01());
        const lp = logPosterior(prop, prevMargins, statePolls, natPolls);
        const a = Math.exp(Math.max(-50, Math.min(50, lp - curLogPost)));
        triedRef.current += 1;
        if(Math.random() < a){ curMu = prop; curLogPost = lp; acceptRef.current += 1; }
      }
      setMu(curMu);
      setIter(x=>x+CHUNK);
      setAcceptRate(acceptRef.current/Math.max(1,triedRef.current));
      // record EV
      const ev = currentEV(curMu);
      setEvSamples(arr => [...arr, ev]);
      requestAnimationFrame(stepper);
    };
    requestAnimationFrame(stepper);
    return ()=>{ canceled=true; };
  }, [running, mu, prevMargins, statePolls, natPolls]);

  // Histogram data
  const histData = useMemo(()=>{
    const min=80,max=480,bin=10; const bins=new Map(); for(let x=min;x<=max;x+=bin) bins.set(x,0);
    evSamples.forEach(v=>{ const b=Math.floor(v/bin)*bin; if(bins.has(b)) bins.set(b, bins.get(b)+1); });
    return Array.from(bins.entries()).map(([x,count])=>({x,count}));
  }, [evSamples]);

  const geography = usStates;

  return (
    <div className="w-full min-h-screen bg-white text-gray-900">
      <header className="max-w-7xl mx-auto px-4 pt-6 pb-2">
        <h1 className="text-2xl font-bold">US Election — Metropolis Sampler (Simplified)</h1>
        <p className="text-sm text-gray-600">Latents are state margins (ppt). Prior says states usually stay near 2020. State and national polls are noisy measurements. We sample a map with Metropolis.</p>
      </header>

      <main className="max-w-7xl mx-auto px-4 py-6 space-y-6">
        {/* DATA COLUMNS */}
        <section className="p-4 rounded-2xl shadow bg-gray-50">
          <h2 className="text-lg font-semibold mb-3">Data</h2>
          <div className="grid grid-cols-1 md:grid-cols-3 gap-4 text-sm">
            <div>
              <h3 className="font-semibold mb-2">2020 margins (Dem − Rep, ppt)</h3>
              <div className="max-h-56 overflow-auto bg-white border rounded-lg p-2">
                {STATE_ORDER.map(s=> (
                  <div key={s} className="flex justify-between border-b py-1"><span>{s}</span><span className={prevMargins[s]>=0?"text-blue-600":"text-red-600"}>{(prevMargins[s]??0).toFixed(1)}</span></div>
                ))}
              </div>
            </div>
            <div>
              <h3 className="font-semibold mb-2">State polls</h3>
              <div className="max-h-56 overflow-auto bg-white border rounded-lg p-2">
                {statePolls.map((p, i)=> (
                  <div key={i} className="flex justify-between border-b py-1"><span>{p.state}</span><span className={p.r>=0?"text-blue-600":"text-red-600"}>{p.r>=0?`Dem +${p.r.toFixed(1)}`:`Rep +${Math.abs(p.r).toFixed(1)}`}</span><span className="text-gray-500">n={p.n}</span></div>
                ))}
              </div>
            </div>
            <div>
              <h3 className="font-semibold mb-2">National polls</h3>
              <div className="max-h-56 overflow-auto bg-white border rounded-lg p-2">
                {natPolls.map((p, i)=> (
                  <div key={i} className="flex justify-between border-b py-1">
                    <span>#{i+1}</span>
                    <span className={p.r>=0?"text-blue-600":"text-red-600"}>{p.r>=0?`Dem +${p.r.toFixed(1)}`:`Rep +${Math.abs(p.r).toFixed(1)}`} </span>
                  </div>
                ))}
              </div>
            </div>
          </div>
        </section>

        {/* MODEL EXPLANATION */}
        <section className="p-4 rounded-2xl shadow bg-gray-50 text-sm">
          <h2 className="text-lg font-semibold mb-2">Model</h2>
          <div className="space-y-3">
            <div>
              <b>1. Latent variables:</b> For each state <InlineMath math="i" />, we have margin <InlineMath math="\mu_i" /> (in ppt, Dem − Rep).
            </div>

            <div>
              <b>2. Prior:</b> Each state usually stays near its 2020 result:
              <div className="ml-4 my-1"><BlockMath math={`\\mu_i - m^{2020}_i \\sim \\mathrm{Laplace}(0, ${LAPLACE_B})`} /></div>
              (Laplace distribution: <InlineMath math={`p(x) = \\frac{1}{2b} \\exp\\left(-\\frac{|x|}{b}\\right)`} />, where <InlineMath math={`b = ${LAPLACE_B}`} /> ppt)
            </div>

            <div>
              <b>3. State polls evidence:</b> Poll <InlineMath math="j" /> in state <InlineMath math="i" /> measures <InlineMath math="\mu_i" /> with noise:
              <div className="ml-4 my-1"><BlockMath math={`y_j \\sim \\mathcal{N}\\left(\\mu_i, \\;\\sigma_j\\right), \\quad \\sigma_j = \\sqrt{\\left(\\frac{100}{\\sqrt{n_j}}\\right)^2 + ${SIGMA_BIAS}^2}`} /></div>
              (<InlineMath math="n_j" /> = sample size, {SIGMA_BIAS} ppt = extra poll noise; the 100 converts sampling noise from proportions [0,1] to percentage points [0,100])
            </div>

            <div>
              <b>4. National polls evidence:</b> Poll <InlineMath math="k" /> measures the national popular vote margin <InlineMath math="\mu_0" /> with noise, where:
              <div className="ml-4 my-1"><BlockMath math="\mu_0 = \sum_i w_i \mu_i" /></div>
              where <InlineMath math="w_i" /> = population share of state <InlineMath math="i" /> (normalized to sum to 1). The poll:
              <div className="ml-4 my-1"><BlockMath math={`z_k \\sim \\mathcal{N}\\left(\\mu_0, \\;\\sigma_k\\right), \\quad \\sigma_k = \\sqrt{\\left(\\frac{100}{\\sqrt{n_k}}\\right)^2 + ${SIGMA_BIAS}^2}`} /></div>
              (if <InlineMath math="n_k" /> unknown, use <InlineMath math={`n_k = ${NAT_DEFAULT_N}`}/>)
            </div>

            <div>
              <b>5. Posterior:</b>
              <div className="ml-4 my-1"><BlockMath math="p(\mu_1, \ldots, \mu_{50} \mid y, z) \;\propto\; \prod_i p(\mu_i - m^{2020}_i) \times \prod_j p(y_j \mid \mu_i) \times \prod_k p(z_k \mid \mu_0)" /></div>
            </div>
          </div>
        </section>

        {/* CONTROLS + SCORES */}
        <section className="p-4 rounded-2xl shadow bg-gray-50">
          <div className="flex flex-wrap items-center gap-2">
            <button className={`px-3 py-2 rounded-xl shadow ${running?"bg-red-600 text-white":"bg-blue-600 text-white"}`} onClick={()=>setRunning(r=>!r)}>{running?"Stop":"Start"} Metropolis</button>
            <button className="px-3 py-2 rounded-xl shadow bg-gray-200" onClick={()=>{
              const CHUNK=500; let cur=mu.slice(); let lp=logPosterior(cur, prevMargins, statePolls, natPolls);
              for(let t=0;t<CHUNK;t++){
                const prop = cur.map(m=> m + PROPOSAL_SD*gaussian01());
                const lq = logPosterior(prop, prevMargins, statePolls, natPolls);
                const a = Math.exp(Math.max(-50, Math.min(50, lq-lp))); triedRef.current+=1; if(Math.random()<a){ cur=prop; lp=lq; acceptRef.current+=1; }
              }
              setMu(cur); setIter(x=>x+CHUNK); setAcceptRate(acceptRef.current/Math.max(1,triedRef.current));
              const ev=currentEV(cur); setEvSamples(arr=>[...arr, ev]);
            }}>Step ×500</button>
            <button className="px-3 py-2 rounded-xl shadow bg-gray-200" onClick={()=>{ setMu(STATE_ORDER.map(()=>0)); setIter(0); setEvSamples([]); acceptRef.current=0; triedRef.current=0; setAcceptRate(0); }}>Reset</button>
            <button className="px-3 py-2 rounded-xl shadow bg-gray-200" onClick={()=>setShowLatents(s=>!s)}>{showLatents?"Hide":"Show"} current latents</button>
            <div className="text-sm text-gray-700 ml-2">Iter: <b>{iter.toLocaleString()}</b> · Accept: <b>{(acceptRate*100).toFixed(1)}%</b> · μ₀ (popular vote): <b>{muNat.toFixed(2)} ppt</b> · EV now: <b>{evNow}</b></div>
          </div>
          {showLatents && (
            <div className="mt-4 p-3 bg-white border rounded-lg">
              <h3 className="text-sm font-semibold mb-2">Current latent variables (state margins μ_i, ppt)</h3>
              <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-5 lg:grid-cols-6 gap-2 text-xs">
                {STATE_ORDER.map((s, i) => (
                  <div key={s} className="flex justify-between border-b py-1">
                    <span className="font-medium">{s}</span>
                    <span className={mu[i]>=0?"text-blue-600":"text-red-600"}>{mu[i].toFixed(2)}</span>
                  </div>
                ))}
              </div>
            </div>
          )}
        </section>

        {/* MAP + HISTOGRAM */}
        <section className="p-4 rounded-2xl shadow bg-gray-50">
          <h2 className="text-lg font-semibold mb-3">Map — current state numbers (ppt)</h2>
          <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
            <div className="lg:col-span-2">
              <ComposableMap projection="geoAlbersUsa" width={820} height={500}>
                <Geographies geography={geography}>
                  {({ geographies }) => geographies.map(geo => {
                    const fips=String(geo.id).padStart(2,"0"); const abbr=FIPS_TO_CODE[fips]; if(!abbr) return null;
                    const m = marginByState[abbr] ?? 0; const fill = marginToColor(m);
                    return <Geography key={geo.rsmKey} geography={geo} fill={fill} stroke="#fff" strokeWidth={0.5} />;
                  })}
                </Geographies>
              </ComposableMap>
            </div>
            <div>
              <div className="h-72">
                <ResponsiveContainer width="100%" height="100%">
                  <BarChart data={histData}>
                    <XAxis dataKey="x" label={{ value:"EV (Dem)", position:"insideBottom", offset:-2 }} />
                    <YAxis allowDecimals={false} />
                    <Tooltip />
                    <Bar dataKey="count" />
                  </BarChart>
                </ResponsiveContainer>
              </div>
              <p className="text-xs text-gray-700 mt-2">Electoral College from all draws (winner-take-all; ME/NE not split in this demo).</p>
            </div>
          </div>
        </section>
      </main>
    </div>
  );
}

/********** scoring & posterior **********/
function computeScores(mu, prevMargins, statePolls, natPolls){
  let prior=0, state=0, national=0;
  // Prior: Laplace on swings
  for(let i=0;i<STATE_ORDER.length;i++){
    const s = mu[i] - (prevMargins[STATE_ORDER[i]] ?? 0);
    prior += logLaplacePdf(s, LAPLACE_MEAN, LAPLACE_B);
  }
  // State polls
  for(const p of statePolls){ const sd=sigmaFromN(p.n); state += logNormPdf(p.r, mu[STATE_ORDER.indexOf(p.state)], sd); }
  // National polls
  const wVec = STATE_ORDER.map(s=>WEIGHTS[s]); const muNat = sumProduct(mu, wVec);
  for(const q of natPolls){ const nEff = q.n ?? NAT_DEFAULT_N; const sd = sigmaFromN(nEff); national += logNormPdf(q.r, muNat, sd); }
  return { prior, state, national };
}
function logPosterior(mu, prevMargins, statePolls, natPolls){
  const sc = computeScores(mu, prevMargins, statePolls, natPolls);
  return sc.prior + sc.state + sc.national;
}

/********** helpers **********/
function toStatePollRows(rows){
  // rows: [state, n, demPct, repPct] or objects with same info
  const out=[]; for(const r of rows){
    if(Array.isArray(r)){
      const [s, n, dem, rep] = r; if(!STATE_ORDER.includes(s)) continue;
      const margin = Number(dem) - Number(rep);
      out.push({ state:s, n:Number(n), r:margin });
    } else { // object
      const s = (r.state||"").toString().trim().toUpperCase(); if(!STATE_ORDER.includes(s)) continue;
      const n = Number(r.sample_size||r.n); let m=null;
      if(r.margin_dem_minus_rep_pp!=null) m = Number(r.margin_dem_minus_rep_pp);
      else if(r.dem_pct!=null && r.rep_pct!=null) m = Number(r.dem_pct) - Number(r.rep_pct);
      if(Number.isFinite(n) && Number.isFinite(m)) out.push({ state:s, n, r:m });
    }
  } return out;
}
function toNatPollRows(rows){
  const out=[]; for(const r of rows){ if(Array.isArray(r)){ const [n, dem, rep] = r; out.push({ r: Number(dem)-Number(rep), n: Number.isFinite(Number(n))? Number(n): null }); } else { const n=r.sample_size??r.n; const dem=r.dem_pct, rep=r.rep_pct; if(dem!=null && rep!=null){ out.push({ r:Number(dem)-Number(rep), n: Number.isFinite(Number(n))? Number(n): null }); } else if(r.margin_dem_minus_rep_pp!=null){ out.push({ r:Number(r.margin_dem_minus_rep_pp), n: Number.isFinite(Number(n))? Number(n): null }); } } } return out; }
function sumProduct(a, b){ let s=0; for(let i=0;i<a.length;i++) s += a[i]*(b[i]??0); return s; }
function currentEV(mu){ let ev=0; for(let i=0;i<STATE_ORDER.length;i++){ if(mu[i]>0) ev += EV_2016[STATE_ORDER[i]]; } return ev; }

// Small UI component
function ScoreBox({ label, value }){
  return (
    <div className="bg-white border rounded-xl p-3">
      <div className="text-xs text-gray-500">{label}</div>
      <div className="text-base font-semibold">{value.toFixed(1)}</div>
    </div>
  );
}
