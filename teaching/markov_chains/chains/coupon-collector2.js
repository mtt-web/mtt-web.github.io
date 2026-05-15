class CouponCollector2 extends MarkovChain {
    constructor(numCoupons = 10) {
        const states = Array.from({ length: numCoupons + 1 }, (_, i) => i);
        const stateNames = states.map(i => `${i}`);
        const transitionMatrix = CouponCollector2._buildTransitionMatrix(numCoupons);

        super({
            name: "Coupon Collector",
            description: `Collect ${numCoupons} coupons with equal probability.`,
            states,
            stateNames,
            transitionMatrix,
            initialDistribution: [1, ...Array(numCoupons).fill(0)]
        });

        this.numCoupons = numCoupons;
        this.absorbingState = numCoupons;
        this._initAbsorptionTracking(this.numDots);
    }

    getCustomControls() {
        return {
            label: 'Coupons',
            min: 2,
            max: 150,
            step: 1,
            value: this.numCoupons,
            onChange: (val) => this._setNumCoupons(val)
        };
    }

    _setNumCoupons(n) {
        this.numCoupons = n;
        this.absorbingState = n;
        this.states = Array.from({ length: n + 1 }, (_, i) => i);
        this.stateNames = this.states.map(i => `${i}`);
        this.transitionMatrix = CouponCollector2._buildTransitionMatrix(n);
        this.initialDistribution = [1, ...Array(n).fill(0)];
        this.description = `Collect ${n} coupons with equal probability.`;
        this.reset();
    }

    static _buildTransitionMatrix(numCoupons) {
        return Array.from({ length: numCoupons + 1 }, (_, i) => {
            const row = Array(numCoupons + 1).fill(0);
            if (i === numCoupons) {
                row[i] = 1;
            } else {
                const pNew = (numCoupons - i) / numCoupons;
                row[i] = 1 - pNew;
                row[i + 1] = pNew;
            }
            return row;
        });
    }

    _initAbsorptionTracking(count) {
        this.dotArrivalSteps = new Array(count).fill(NaN);
        this._absorbed = new Array(count).fill(false);
        this.absorbedCount = 0;
        this.onArrival = (dotIndex, state) => {
            if (state === this.absorbingState && !this._absorbed[dotIndex]) {
                this._absorbed[dotIndex] = true;
                this.absorbedCount++;
                const dot = this.dots[dotIndex];
                this.dotArrivalSteps[dotIndex] = dot.history ? dot.history.length - 1 : 0;
            }
        };
    }

    getNodePositions(centerX, centerY) {
        const nodesPerRow = 20;
        const numRows = Math.ceil(this.states.length / nodesPerRow);
        const spacing = this.getNodeSpacing ? this.getNodeSpacing() : 160;
        const rowHeight = 120;

        const totalHeight = (numRows - 1) * rowHeight;
        const startY = centerY - totalHeight / 2;

        return this.states.map((_, i) => {
            const row = Math.floor(i / nodesPerRow);
            const col = i % nodesPerRow;
            const nodesInThisRow = Math.min(nodesPerRow, this.states.length - row * nodesPerRow);
            const rowWidth = (nodesInThisRow - 1) * spacing;
            const startX = centerX - rowWidth / 2;
            return {
                x: startX + col * spacing,
                y: startY + row * rowHeight
            };
        });
    }

    getRenderConfig() {
        const numRows = Math.ceil(this.states.length / 20);
        const canvasHeight = Math.max(420, 150 + numRows * 60);
        return {
            canvasHeight: canvasHeight,
            showStats: true,
            showTransitionMatrix: true,
            showEdgeLabels: true,
            selfLoopOrientation: 'up'
        };
    }

    getEdgeRenderMode() {
        return 'mixed';
    }

    getTheoreticalSteadyState() {
        const steady = Array(this.numCoupons + 1).fill(0);
        steady[this.numCoupons] = 1;
        return steady;
    }

    getHistogramData() {
        return this.dotArrivalSteps.filter(v => Number.isFinite(v));
    }

    reset() {
        super.reset();
        this._initAbsorptionTracking(this.numDots);
    }

    setNumDots(num) {
        super.setNumDots(num);
        this._initAbsorptionTracking(num);
    }
}

chainModules.push(CouponCollector2);

