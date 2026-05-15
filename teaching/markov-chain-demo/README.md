# HMM Map-Matching Demo

An interactive visualization demonstrating Hidden Markov Models (HMMs) for GPS trajectory map-matching.

## About

This demo simulates a car traveling on a Manhattan grid road network with:
- Random movement with configurable wait and turn probabilities
- Gaussian noise added to GPS observations
- A final "parking" phase where the car moves off-road

Two HMM decoders reconstruct the most likely path:
1. **Position-only HMM**: States are grid cells only
2. **Position+Velocity HMM**: States are (cell, direction) pairs with sticky motion

## Features

- Real-time visualization of true path, noisy observations, and decoded paths
- Interactive controls for grid size, noise level, and movement parameters
- Performance metrics comparing both HMM approaches
- SVG-based rendering with clear color-coded paths

## Development

```bash
# Install dependencies
npm install

# Run development server
npm start

# Build for production
npm run build
```

## Access

When deployed, access this demo at:
`/teaching/markov-chain-demo/build/`

## Implementation Details

- **Simulation**: Deterministic RNG for reproducible results
- **HMM Decoding**: Viterbi algorithm in log-space
- **Transitions**: Position-only uses uniform 4-neighbor + stay; Position+velocity uses sticky motion (0.82 keep direction, 0.12 turn, 0.06 stay)
- **Emissions**: Isotropic Gaussian likelihood based on distance to cell center

## Credits

Based on the HMM map-matching concept with discretized road networks and velocity-aware state spaces.
