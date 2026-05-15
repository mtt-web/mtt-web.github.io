class RandomWalk extends MarkovChain {
    constructor(p = 0.5, startPos = 4) {
        const q = 1 - p;
        const numStates = 11;

        // Create states 0 to 10
        const states = Array.from({length: numStates}, (_, i) => i);
        const stateNames = states.map(i => i.toString());

        // Create transition matrix
        const transitionMatrix = [];
        for (let i = 0; i < numStates; i++) {
            const row = new Array(numStates).fill(0);

            if (i === 0) {
                // Left boundary - absorbing
                row[0] = 1.0;
            } else if (i === numStates - 1) {
                // Right boundary - absorbing
                row[numStates - 1] = 1.0;
            } else {
                // Interior states - can move left or right
                row[i - 1] = q;  // Move left
                row[i + 1] = p;  // Move right
            }

            transitionMatrix.push(row);
        }

        // Start at configurable position
        const initialDistribution = new Array(numStates).fill(0);
        const clampedStart = Math.max(0, Math.min(numStates - 1, Math.round(startPos)));
        initialDistribution[clampedStart] = 1.0;

        super({
            name: "Random Walk",
            description: `1D random walk with absorbing boundaries (p=${p.toFixed(2)} right, q=${q.toFixed(2)} left)`,
            states: states,
            stateNames: stateNames,
            transitionMatrix: transitionMatrix,
            initialDistribution: initialDistribution
        });

        this.p = p;
        this.q = q;
        this.startPos = clampedStart;

        // Absorption tracking - TWO absorbing states (0 and 10)
        this.absorbingStates = [0, numStates - 1]; // States 0 and 10
        this.dotArrivalSteps = new Array(this.numDots).fill(NaN);
        this._absorbed = new Array(this.numDots).fill(false);
        this.absorbedCount = 0;
        this.onArrival = (dotIndex, state) => {
            if (this.absorbingStates.includes(state) && !this._absorbed[dotIndex]) {
                this._absorbed[dotIndex] = true;
                this.absorbedCount++;
                const dot = this.dots[dotIndex];
                const transitions = dot.history ? dot.history.length - 1 : 0;
                this.dotArrivalSteps[dotIndex] = transitions;
            }
        };
    }

    getNodePositions(centerX, centerY, radius, canvasWidth = 800) {
        // Override to draw nodes in a horizontal line instead of circle
        // Node radius is 30, so spacing is 4 * radius = 120
        const nodeRadius = 30;
        const spacing = 4 * nodeRadius;
        const totalWidth = spacing * (this.states.length - 1);
        const startX = centerX - totalWidth / 2;

        return this.states.map((_, i) => ({
            x: startX + i * spacing,
            y: centerY
        }));
    }

    updateProbability(newP) {
        this.p = newP;
        this.q = 1 - newP;
        this.description = `1D random walk with absorbing boundaries (p=${newP.toFixed(2)} right, q=${this.q.toFixed(2)} left)`;

        // Update transition matrix for 11 states
        const numStates = 11;
        this.transitionMatrix = [];

        for (let i = 0; i < numStates; i++) {
            const row = new Array(numStates).fill(0);

            if (i === 0) {
                // Left boundary - absorbing
                row[0] = 1.0;
            } else if (i === numStates - 1) {
                // Right boundary - absorbing
                row[numStates - 1] = 1.0;
            } else {
                // Interior states - can move left or right
                row[i - 1] = this.q;  // Move left
                row[i + 1] = this.p;  // Move right
            }

            this.transitionMatrix.push(row);
        }
    }

    getTheoreticalSteadyState() {
        // For random walk with absorbing boundaries starting at position this.startPos
        const startPos = this.startPos;
        const maxPos = 10;
        const steadyState = new Array(11).fill(0);

        if (this.p === 0.5) {
            // Symmetric case: probability of absorption at 0 vs 10 depends on starting position
            const prob0 = 1 - startPos / maxPos; // 6/10 = 0.6
            const prob10 = startPos / maxPos;    // 4/10 = 0.4
            steadyState[0] = prob0;
            steadyState[10] = prob10;
        } else {
            // Asymmetric case
            const r = this.q / this.p;

            let prob0, prob10;
            if (r === 1) {
                prob0 = 1 - startPos / maxPos;
            } else {
                prob0 = (Math.pow(r, startPos) - Math.pow(r, maxPos)) / (1 - Math.pow(r, maxPos));
            }
            prob10 = 1 - prob0;

            steadyState[0] = prob0;
            steadyState[10] = prob10;
        }

        return steadyState;
    }

    getCustomControls() {
        return [
            {
                type: 'probability',
                label: 'Right probability (p)',
                value: this.p,
                min: 0.1,
                max: 0.9,
                step: 0.1,
                onChange: (value) => this.updateProbability(value)
            },
            {
                type: 'slider',
                label: 'Start position',
                value: this.startPos,
                min: 0,
                max: 10,
                step: 1,
                onChange: (value) => this.updateStartPosition(Math.round(value))
            }
        ];
    }

    isRunComplete() {
        return this.absorbedCount === this.numDots;
    }

    getHistogramData() {
        return this.dotArrivalSteps ? this.dotArrivalSteps.filter(v => Number.isFinite(v)) : [];
    }

    updateStartPosition(newStart) {
        const numStates = 11;
        const clamped = Math.max(0, Math.min(numStates - 1, Math.round(newStart)));
        this.startPos = clamped;
        // Rebuild initial distribution and reset dots so change takes effect
        this.initialDistribution = new Array(numStates).fill(0);
        this.initialDistribution[clamped] = 1.0;
        this.reset();
    }

    reset() {
        super.reset();
        this.dotArrivalSteps = new Array(this.numDots).fill(NaN);
        this._absorbed = new Array(this.numDots).fill(false);
        this.absorbedCount = 0;
    }

    setNumDots(num) {
        super.setNumDots(num);
        this.dotArrivalSteps = new Array(num).fill(NaN);
        this._absorbed = new Array(num).fill(false);
        this.absorbedCount = 0;
    }
}

chainModules.push(RandomWalk);