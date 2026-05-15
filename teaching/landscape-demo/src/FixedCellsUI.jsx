import React from 'react';

export default function FixedCellsUI({ fixedCells, setFixedCells, addFixedCell }) {
  const [i, setI] = React.useState('');
  const [j, setJ] = React.useState('');
  const [h, setH] = React.useState('');

  const add = () => {
    const ii = parseInt(i, 10);
    const jj = parseInt(j, 10);
    const hh = parseInt(h, 10);
    if (Number.isNaN(ii) || Number.isNaN(jj) || Number.isNaN(hh)) return;
    if (ii < 0 || ii >= 15 || jj < 0 || jj >= 15 || hh < 0 || hh > 9) return;
    if (addFixedCell) {
      addFixedCell(ii, jj, hh);
    } else {
      setFixedCells(prev => {
        const others = prev.filter(c => !(c.i === ii && c.j === jj));
        return [...others, { i: ii, j: jj, h: hh }];
      });
    }
    setI(''); setJ(''); setH('');
  };

  const remove = (ii, jj) => {
    setFixedCells(prev => prev.filter(c => !(c.i === ii && c.j === jj)));
  };

  return (
    <div style={{ marginBottom: 10 }}>
      <div style={{ fontWeight: 600, marginBottom: 6 }}>Fix cell height</div>
      <div style={{ display: 'flex', gap: 6, alignItems: 'center', marginBottom: 6 }}>
        <input placeholder="i (0-14)" value={i} onChange={e=>setI(e.target.value)} style={{ width: 70 }} />
        <input placeholder="j (0-14)" value={j} onChange={e=>setJ(e.target.value)} style={{ width: 70 }} />
        <input placeholder="h (0-9)" value={h} onChange={e=>setH(e.target.value)} style={{ width: 70 }} />
        <button onClick={add}>Add Fixed Height</button>
      </div>
      {fixedCells.length > 0 && (
        <div style={{ fontSize: 13, color: '#444' }}>
          Fixed: {fixedCells.map(c => `(${c.i},${c.j})→${c.h}`).join(', ')}
          <div style={{ marginTop: 6 }}>
            {fixedCells.map(c => (
              <button key={`${c.i}-${c.j}`} onClick={()=>remove(c.i,c.j)} style={{ marginRight: 6 }}>Remove ({c.i},{c.j})</button>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}


