class CounterDemo extends MarkovChain {
    constructor() {
        const numStates = 256;
        const states = Array.from({ length: numStates }, (_, i) => i);
        const names = states.map(i => String(i));
        const transitionMatrix = CounterDemo._buildNaive(numStates);
        super({
            name: "Counter (256 nodes)",
            description: "Switch between a naive counter and a Morris-style counter.",
            states,
            stateNames: names,
            transitionMatrix,
            initialDistribution: (() => {
                const dist = new Array(numStates).fill(0);
                dist[0] = 1.0;
                return dist;
            })()
        });
        this.mode = 0; // 0 = naive, 1 = morris
        this.alpha = 1.1; // growth base for morris mode
        this.morrisValues = CounterDemo._computeMorrisValues(numStates, this.alpha);
        this._applyStateNames();
    }

    static _buildNaive(n) {
        const M = Array.from({ length: n }, () => new Array(n).fill(0));
        for (let i = 0; i < n - 1; i++) {
            M[i][i + 1] = 1.0;
        }
        M[n - 1][n - 1] = 1.0;
        return M;
    }

    static _computeMorrisValues(n, alpha) {
        const values = new Array(n);
        for (let i = 0; i < n; i++) {
            values[i] = Math.max(i, Math.floor(Math.pow(alpha, i)));
        }
        return values;
    }

    static _buildMorris(values) {
        const n = values.length;
        const M = Array.from({ length: n }, () => new Array(n).fill(0));
        for (let i = 0; i < n - 1; i++) {
            const delta = Math.max(1, values[i + 1] - values[i]);
            const p = 1 / delta;
            M[i][i + 1] = p;
            M[i][i] = 1 - p;
        }
        M[n - 1][n - 1] = 1.0;
        return M;
    }

    _rebuildMatrix() {
        const n = this.states.length;
        if (this.mode === 0) {
            this.transitionMatrix = CounterDemo._buildNaive(n);
            this._applyStateNames();
        } else {
            this.morrisValues = CounterDemo._computeMorrisValues(n, this.alpha);
            this.transitionMatrix = CounterDemo._buildMorris(this.morrisValues);
            this._applyStateNames();
        }
    }

    _applyStateNames() {
        if (this.mode === 1 && this.morrisValues) {
            this.stateNames = this.morrisValues.map(v => String(v));
        } else {
            this.stateNames = this.states.map((_, i) => String(i));
        }
    }

    getCustomControls() {
        // Use a 0/1 range slider to switch modes; 0 = naive, 1 = morris
        return [
            {
                label: 'Mode (0=Naive, 1=Morris)',
                min: 0,
                max: 1,
                step: 1,
                value: this.mode,
                onChange: (v) => { this.mode = v; this._rebuildMatrix(); }
            }
        ];
    }

    getRenderConfig() {
        return {
            canvasHeight: 600,
            showStats: true,
            showTransitionMatrix: false,
            showEdgeLabels: this.mode === 1,
            selfLoopOrientation: 'up'
        };
    }

    getNodeRadius() {
        return 12;
    }

    getNodeFontSize() {
        return 12;
    }

    getEdgeRenderMode() {
        return 'mixed';
    }

    getNodePositions(centerX, centerY, radius, width) {
        // Arrange nodes on a 16x16 grid centered in the canvas
        const cols = 16, rows = 16;
        const spacing = this.getNodeSpacing ? this.getNodeSpacing() : 120;
        const totalW = (cols - 1) * spacing;
        const totalH = (rows - 1) * spacing;
        const originX = centerX - totalW / 2;
        const originY = centerY - totalH / 2;
        const positions = [];
        for (let r = 0; r < rows; r++) {
            for (let c = 0; c < cols; c++) {
                positions.push({
                    x: originX + c * spacing,
                    y: originY + r * spacing
                });
            }
        }
        return positions;
    }

    shouldDisplayEdgeLabel(fromIndex, toIndex) {
        if (this.mode === 1) {
            return toIndex === fromIndex + 1;
        }
        return super.shouldDisplayEdgeLabel(fromIndex, toIndex);
    }

    shouldDisplaySelfLoopLabel() {
        return this.mode !== 1;
    }

    draw(ctx, width, height, highlightOptions) {
        super.draw(ctx, width, height, highlightOptions);

        const positions = this._lastPositions || [];
        const hoveredIndex = highlightOptions && Number.isInteger(highlightOptions.hoveredIndex)
            ? highlightOptions.hoveredIndex
            : null;
        const cols = 16;

        for (let i = 0; i < this.states.length - 1; i++) {
            const j = i + 1;
            if (this._isWrapEdge(i, j, cols) && this.transitionMatrix[i][j] > 0) {
                this._drawWrapEdge(ctx, positions, i, j, hoveredIndex, this.transitionMatrix[i][j]);
            }
        }
    }

    _isWrapEdge(fromIndex, toIndex, cols) {
        return (fromIndex % cols === cols - 1) && toIndex === fromIndex + 1;
    }

    _drawWrapEdge(ctx, positions, fromIndex, toIndex, hoveredIndex, probability) {
        const from = positions[fromIndex];
        const to = positions[toIndex];
        if (!from || !to) return;

        const highlighted = hoveredIndex === fromIndex;
        this._applyEdgeStroke(ctx, highlighted);

        const { r, pad } = this._getNodeRadiusWithPad();
        const spacing = this.getNodeSpacing ? this.getNodeSpacing() : 120;
        const verticalDrop = spacing / 4;

        const start = { x: from.x, y: from.y + r + pad };
        const corner1 = { x: from.x, y: from.y + r + pad + verticalDrop };
        const corner2 = { x: to.x, y: corner1.y };
        const end = { x: to.x, y: to.y - (r + pad) };

        ctx.beginPath();
        ctx.moveTo(start.x, start.y);
        ctx.lineTo(corner1.x, corner1.y);
        ctx.lineTo(corner2.x, corner2.y);
        ctx.lineTo(end.x, end.y);
        ctx.stroke();

        const { tip, angle } = this._placeArrowOnStraight(corner2, to, r, pad);
        ctx.save();
        ctx.translate(tip.x, tip.y);
        ctx.rotate(angle);
        ctx.beginPath();
        ctx.moveTo(0, 0);
        ctx.lineTo(-10, -5);
        ctx.lineTo(-10, 5);
        ctx.closePath();
        this._applyArrowFill(ctx, highlighted);
        ctx.fill();
        ctx.restore();

        if (this.getRenderConfig().showEdgeLabels && this.shouldDisplayEdgeLabel(fromIndex, toIndex)) {
            const fontPx = this._getEdgeLabelFontPx();
            const text = this._formatEdgeProbability(probability);
            const labelPos = {
                x: (corner1.x + corner2.x) / 2,
                y: corner1.y - this._getLabelOffsetAmount()
            };
            this._drawEdgeLabel(ctx, text, labelPos.x, labelPos.y, highlighted, fontPx);
        }
    }

    shouldDrawEdge(fromIndex, toIndex) {
        const cols = 16;
        if (this._isWrapEdge(fromIndex, toIndex, cols)) {
            return false;
        }
        return super.shouldDrawEdge(fromIndex, toIndex);
    }
}

chainModules.push(CounterDemo);

