import React from 'react';

/**
 * STEMPool - Visualizes the pool of STEM-loving people split 80-20 men/women
 * Shows fractions p_men and p_women who watch YouTube STEM videos
 */
export default function STEMPool() {
  const totalWidth = 800;
  const poolHeight = 150;
  const chartTop = 20;
  const chartHeight = 240;
  const maleWidth = totalWidth * 0.8;
  const femaleWidth = totalWidth * 0.2;
  const pMen = 0.6; // illustrative share among men
  const pWomen = 0.4; // illustrative share among women
  const innerPadding = 6;
  const maleInnerX = innerPadding;
  const maleInnerWidth = maleWidth - innerPadding * 2;
  const maleInnerHeight = Math.round((poolHeight * 2) / 3);
  const maleInnerY = chartTop + poolHeight - maleInnerHeight - innerPadding;
  const femaleInnerX = maleWidth + innerPadding;
  const femaleInnerWidth = femaleWidth - innerPadding * 2;
  const femaleInnerHeight = Math.round(poolHeight / 3);
  const femaleInnerY = chartTop + poolHeight - femaleInnerHeight - innerPadding;

  return (
    <div
      className="stem-pool"
      style={{
        margin: '1.25rem 0',
        padding: 0,
        display: 'flow-root',
        fontFamily: 'system-ui, -apple-system, sans-serif',
      }}
    >
      <svg
        viewBox={`0 0 ${totalWidth} ${chartHeight}`}
        preserveAspectRatio="xMidYMid meet"
        style={{
          width: '100%',
          maxWidth: '100%',
          height: 'auto',
          display: 'block',
        }}
        role="img"
        aria-labelledby="stem-pool-title"
      >
        <title id="stem-pool-title">STEM pool by gender</title>
        <defs>
          <linearGradient id="maleGradient" x1="0" x2="0" y1="0" y2="1">
            <stop offset="0%" stopColor="#6fb5ff" stopOpacity="0.9" />
            <stop offset="100%" stopColor="#4a90e2" stopOpacity="0.9" />
          </linearGradient>
          <linearGradient id="femaleGradient" x1="0" x2="0" y1="0" y2="1">
            <stop offset="0%" stopColor="#ff6fa3" stopOpacity="0.9" />
            <stop offset="100%" stopColor="#e91e63" stopOpacity="0.9" />
          </linearGradient>
        </defs>
        {/* Men section */}
        <rect
          x="0"
          y={chartTop}
          width={maleWidth}
          height={poolHeight}
          fill="url(#maleGradient)"
          opacity={0.25}
          stroke="#4a90e2"
          strokeWidth="2"
          rx="12"
          ry="12"
        />
        <text
          x={maleWidth / 2}
          y={chartTop - 10}
          textAnchor="middle"
          fontSize="18"
          fontWeight="600"
          fill="#1976d2"
        >
          Men (80%)
        </text>

        {/* Highlight p_men portion */}
        <rect
          x={maleInnerX}
          y={maleInnerY}
          width={maleInnerWidth}
          height={maleInnerHeight}
          fill="#4a90e2"
          opacity={0.8}
          rx="10"
          ry="10"
        />
        <text
          x={maleInnerX + maleInnerWidth / 2}
          y={maleInnerY + maleInnerHeight / 2}
          textAnchor="middle"
          dominantBaseline="middle"
          fontSize="15"
          fontStyle="italic"
          fill="#1976d2"
        >
          {"p"}
          <tspan baselineShift="-30%" fontSize="11">men</tspan>
          {" × Men"}
        </text>

        {/* Women section */}
        <rect
          x={maleWidth}
          y={chartTop}
          width={femaleWidth}
          height={poolHeight}
          fill="url(#femaleGradient)"
          opacity={0.25}
          stroke="#e91e63"
          strokeWidth="2"
          rx="12"
          ry="12"
        />
        <text
          x={maleWidth + femaleWidth / 2}
          y={chartTop - 10}
          textAnchor="middle"
          fontSize="18"
          fontWeight="600"
          fill="#c2185b"
        >
          Women (20%)
        </text>

        {/* Highlight p_women portion */}
        <rect
          x={femaleInnerX}
          y={femaleInnerY}
          width={femaleInnerWidth}
          height={femaleInnerHeight}
          fill="#e91e63"
          opacity={0.85}
          rx="10"
          ry="10"
        />
        <text
          x={femaleInnerX + femaleInnerWidth / 2}
          y={femaleInnerY + femaleInnerHeight / 2}
          textAnchor="middle"
          dominantBaseline="middle"
          fontSize="14"
          fontStyle="italic"
          fill="#c2185b"
        >
          {"p"}
          <tspan baselineShift="-30%" fontSize="10.5">women</tspan>
          {" × Women"}
        </text>
      </svg>
    </div>
  );
}
