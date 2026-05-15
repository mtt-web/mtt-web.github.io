# HMM Map-Matching Demo - Technical Notes

## Purpose

This demo was created to illustrate Hidden Markov Models in the context of GPS trajectory map-matching for the Probability 2 course.

## Key Concepts Demonstrated

1. **Discretization**: Continuous space → discrete states (road network cells)
2. **Hidden States**: True position on road vs noisy GPS observations
3. **Transition Model**: Movement physics (sticky motion with direction)
4. **Emission Model**: Gaussian observation noise
5. **Inference**: Viterbi algorithm for most likely path

## Two HMM Variants

### Position-Only HMM
- **States**: Grid cells (N×N states)
- **Transitions**: Stay (15%) or move to 4-neighbors uniformly
- **Insight**: Simple but can "flicker" between nearby cells

### Position+Velocity HMM
- **States**: (cell, direction) pairs (N×N×5 states)
- **Transitions**: Sticky motion - prefer continuing in same direction
- **Insight**: More stable, resists rapid direction changes

## Implementation Notes

- Built with React + vanilla JavaScript (no external libraries for HMM)
- Uses deterministic LCG RNG for reproducible demos
- Viterbi in log-space to avoid numerical underflow
- Precomputed incoming arcs for efficiency
- SVG rendering for crisp visualization

## Educational Value

Students can:
- See HMM decoding in action with real-time visualization
- Compare different state space designs (with/without velocity)
- Understand tradeoffs: more states → better physics but slower
- Experiment with noise levels and transition probabilities
- Observe the "parking" edge case (off-road observations)

## Future Enhancements (Optional)

- Add "soft off-road" states with emission penalty
- Multi-step directions (faster car, 2 cells per second)
- Export trajectory data as JSON/CSV
- Step-by-step animation mode
- Load real GPS traces from file
