class MarkovChain {
    constructor(config) {
        this.name = config.name;
        this.description = config.description;
        this.states = config.states;
        this.transitionMatrix = config.transitionMatrix;
        this.stateNames = config.stateNames || this.states.map((_, i) => `S${i}`);
        this.stateColors = config.stateColors || this.generateDefaultColors();
        this.initialDistribution = config.initialDistribution || this.getUniformDistribution();

        this.numDots = 100;
        this.dots = [];
        this.stateCount = new Array(this.states.length).fill(0);
        this.stepCount = 0;
        this.transitionCount = {};
        this.animationSpeed = 1.0; // Speed multiplier for animations

        // New: step-animation coupling
        this.stepInProgress = false;
        this.stepElapsed = 0;
        this.stepDuration = 500; // Will be set by setAnimationSpeed
        this.onStepComplete = null; // Callback when step animation finishes

        this.initializeDots();
    }

    generateDefaultColors() {
        const defaultColors = ['#E0E0E0', '#E0E0E0', '#E0E0E0', '#E0E0E0', '#E0E0E0', '#E0E0E0', '#E0E0E0', '#E0E0E0'];
        return this.states.map((_, i) => defaultColors[i % defaultColors.length]);
    }

    getUniformDistribution() {
        const prob = 1 / this.states.length;
        return new Array(this.states.length).fill(prob);
    }

    initializeDots() {
        this.dots = [];
        this.stateCount = new Array(this.states.length).fill(0);
        this.stepCount = 0;
        this.transitionCount = {};

        for (let i = 0; i < this.numDots; i++) {
            const initialState = this.chooseInitialState();
            const angle = Math.random() * Math.PI * 2;
            const radius = Math.random() * 25;
            this.dots.push({
                currentState: initialState,
                nextState: initialState,
                history: [initialState],
                x: radius * Math.cos(angle),
                y: radius * Math.sin(angle),
                targetX: null,
                targetY: null,
                animationProgress: 1,
                isMoving: false
            });
            this.stateCount[initialState]++;
        }
    }

    chooseInitialState() {
        const rand = Math.random();
        let cumSum = 0;
        for (let i = 0; i < this.states.length; i++) {
            cumSum += this.initialDistribution[i];
            if (rand < cumSum) {
                return i;
            }
        }
        return this.states.length - 1;
    }

    step() {
        // New implementation: prepareStep()
        // Decide transitions but don't commit yet
        if (this.stepInProgress) {
            console.warn('step() called while step already in progress - ignoring');
            return;
        }

        const newTransitions = {};

        for (let dot of this.dots) {
            const currentState = dot.currentState;
            const rand = Math.random();
            let cumSum = 0;
            let nextState = currentState;

            for (let i = 0; i < this.states.length; i++) {
                cumSum += this.transitionMatrix[currentState][i];
                if (rand < cumSum) {
                    nextState = i;
                    break;
                }
            }

            const transKey = `${currentState}->${nextState}`;
            newTransitions[transKey] = (newTransitions[transKey] || 0) + 1;

            // Store the transition decision
            dot.nextState = nextState;
            dot.animationProgress = 0;
            dot.isMoving = true;

            // Calculate target position in the new state
            const angle = Math.random() * Math.PI * 2;
            const radius = Math.random() * 25;
            dot.targetX = radius * Math.cos(angle);
            dot.targetY = radius * Math.sin(angle);

            // Add to history immediately (decision is made)
            dot.history.push(nextState);
        }

        this.transitionCount = newTransitions;

        // Mark step as in progress
        this.stepInProgress = true;
        this.stepElapsed = 0;

        // DO NOT increment stepCount here - only when animation completes
    }

    finishStep() {
        // Commit the step: update all dot states and increment counter
        for (let dot of this.dots) {
            if (dot.nextState !== dot.currentState) {
                // Update state counts
                this.stateCount[dot.currentState]--;
                this.stateCount[dot.nextState]++;
            }

            // Commit state transition
            dot.currentState = dot.nextState;
            dot.x = dot.targetX;
            dot.y = dot.targetY;
            dot.isMoving = false;

            // Call arrival callback if it exists
            if (typeof this.onArrival === 'function') {
                try {
                    this.onArrival(this.dots.indexOf(dot), dot.currentState);
                } catch (e) {
                    /* ignore */
                }
            }
        }

        // NOW increment step count
        this.stepCount++;
        this.stepInProgress = false;

        // Notify that step is complete
        if (typeof this.onStepComplete === 'function') {
            this.onStepComplete();
        }
    }

    animate(deltaTime = 16) {
        // New implementation: drives the current step animation
        if (!this.stepInProgress) return;

        this.stepElapsed += deltaTime;
        const progress = Math.min(this.stepElapsed / this.stepDuration, 1.0);

        // Animate all dots together
        for (let i = 0; i < this.dots.length; i++) {
            const dot = this.dots[i];
            if (!dot.isMoving) continue;

            dot.animationProgress = progress;

            if (dot.nextState !== dot.currentState) {
                // Moving between states - interpolate between state centers
                // The actual interpolation happens in draw() using animationProgress
                // Here we just update the progress value
            } else {
                // Moving within same state - interpolate position
                const eased = this.easeInOutQuad(progress);
                dot.x = dot.x + (dot.targetX - dot.x) * eased;
                dot.y = dot.y + (dot.targetY - dot.y) * eased;
            }
        }

        // When animation reaches 100%, commit the step
        if (progress >= 1.0 && this.stepInProgress) {
            this.finishStep();
        }
    }

    draw(ctx, width, height, highlightOptions) {
        // Clearing happens in the render loop to respect transforms

        const centerX = width / 2;
        const centerY = height / 2;
        let radius = Math.min(width, height) * 0.3;

        // Get initial positions
        let positions = this.getNodePositions ? this.getNodePositions(centerX, centerY, radius, width) : this.getDefaultNodePositions(centerX, centerY, radius);

        // No automatic rescaling; rely on UI zoom

        // Expose positions for hit-testing
        this._lastPositions = positions;

        const hoveredIndex = highlightOptions && Number.isInteger(highlightOptions.hoveredIndex)
            ? highlightOptions.hoveredIndex
            : null;

        this.drawTransitions(ctx, positions, hoveredIndex);
        this.drawSelfLoops(ctx, positions, centerX, centerY, hoveredIndex); // Draw self-loops with other edges
        this.drawNodes(ctx, positions);
        this.drawDots(ctx, positions);

        // Optional node highlighting (visual rings) without redrawing arcs
        if (hoveredIndex !== null && hoveredIndex >= 0 && hoveredIndex < this.states.length) {
            this.drawNodeHighlightRings(ctx, positions, hoveredIndex);
        }
    }

    getDefaultNodePositions(centerX, centerY, radius) {
        return this.states.map((_, i) => {
            const angle = (i * 2 * Math.PI) / this.states.length - Math.PI / 2;
            return {
                x: centerX + radius * Math.cos(angle),
                y: centerY + radius * Math.sin(angle)
            };
        });
    }

    drawTransitions(ctx, positions, hoveredIndex = null) {
        // Draw transitions between different states
        for (let i = 0; i < this.states.length; i++) {
            for (let j = i + 1; j < this.states.length; j++) {
                const forwardAllowed = this.shouldDrawEdge(i, j);
                const backwardAllowed = this.shouldDrawEdge(j, i);
                const hasForward = forwardAllowed && this.transitionMatrix[i][j] > 0;
                const hasBackward = backwardAllowed && this.transitionMatrix[j][i] > 0;

                if (!hasForward && !hasBackward) continue;

                const from = positions[i];
                const to = positions[j];

                if (hasForward && hasBackward) {
                    // Draw curved arrows for bidirectional transitions
                    const dx = to.x - from.x;
                    const dy = to.y - from.y;
                    const perpX = -dy * 0.15;
                    const perpY = dx * 0.15;

                    // Forward arrow (curved)
                    const forwardHighlighted = (hoveredIndex === i);
                    this._applyEdgeStroke(ctx, forwardHighlighted);
                    const cpF = { x: from.x + dx * 0.5 + perpX, y: from.y + dy * 0.5 + perpY };
                    ctx.beginPath();
                    ctx.moveTo(from.x, from.y);
                    ctx.quadraticCurveTo(cpF.x, cpF.y, to.x, to.y);
                    ctx.stroke();

                    const { r, pad } = this._getNodeRadiusWithPad();
                    let { tip, angle } = this._placeArrowOnQuadratic(from, cpF, to, to, r, pad);
                    ctx.save();
                    ctx.translate(tip.x, tip.y);
                    ctx.rotate(angle);
                    ctx.beginPath();
                    ctx.moveTo(0, 0);
                    ctx.lineTo(-8, -4);
                    ctx.lineTo(-8, 4);
                    ctx.closePath();
                    this._applyArrowFill(ctx, forwardHighlighted);
                    ctx.fill();
                    ctx.restore();

                    // Backward arrow (curved)
                    const backwardHighlighted = hoveredIndex === j;
                    this._applyEdgeStroke(ctx, backwardHighlighted);
                    const cpB = { x: from.x + dx * 0.5 - perpX, y: from.y + dy * 0.5 - perpY };
                    ctx.beginPath();
                    ctx.moveTo(to.x, to.y);
                    ctx.quadraticCurveTo(cpB.x, cpB.y, from.x, from.y);
                    ctx.stroke();

                    ({ tip, angle } = this._placeArrowOnQuadratic(to, cpB, from, from, r, pad));
                    ctx.save();
                    ctx.translate(tip.x, tip.y);
                    ctx.rotate(angle);
                    ctx.beginPath();
                    ctx.moveTo(0, 0);
                    ctx.lineTo(-8, -4);
                    ctx.lineTo(-8, 4);
                    ctx.closePath();
                    this._applyArrowFill(ctx, backwardHighlighted);
                    ctx.fill();
                    ctx.restore();

                    // Labels
                    if (this.getRenderConfig().showEdgeLabels && this.shouldDisplayEdgeLabel(i, j)) {
                        const fontPx = this._getEdgeLabelFontPx();
                        const midPoint = this._getMidpoint(from, to);
                        const labelPosF = this._getCurvedLabelPosition(cpF, midPoint);
                        const textF = this._formatEdgeProbability(this.transitionMatrix[i][j]);
                        this._drawEdgeLabel(ctx, textF, labelPosF.x, labelPosF.y, forwardHighlighted, fontPx);
                    }
                    if (this.getRenderConfig().showEdgeLabels && this.shouldDisplayEdgeLabel(j, i)) {
                        const fontPx = this._getEdgeLabelFontPx();
                        const midPoint = this._getMidpoint(from, to);
                        const labelPosB = this._getCurvedLabelPosition(cpB, midPoint);
                        const textB = this._formatEdgeProbability(this.transitionMatrix[j][i]);
                        this._drawEdgeLabel(ctx, textB, labelPosB.x, labelPosB.y, backwardHighlighted, fontPx);
                    }

                } else if (hasForward) {
                    // Single direction arrow or curved if chain requests
                    const mode = this.getEdgeRenderMode();
                    if (mode === 'curved_all') {
                        const dx = to.x - from.x;
                        const dy = to.y - from.y;
                        const perpX = -dy * 0.15;
                        const perpY = dx * 0.15;
                        const highlighted = hoveredIndex === i;
                        this._applyEdgeStroke(ctx, highlighted);
                        const { r, pad } = this._getNodeRadiusWithPad();
                        const cpF = { x: from.x + dx * 0.5 + perpX, y: from.y + dy * 0.5 + perpY };
                    ctx.beginPath();
                    ctx.moveTo(from.x, from.y);
                        ctx.quadraticCurveTo(cpF.x, cpF.y, to.x, to.y);
                    ctx.stroke();

                        const { tip, angle } = this._placeArrowOnQuadratic(from, cpF, to, to, r, pad);
                        ctx.save();
                        ctx.translate(tip.x, tip.y);
                        ctx.rotate(angle);
                        ctx.beginPath();
                        ctx.moveTo(0, 0);
                        ctx.lineTo(-8, -4);
                        ctx.lineTo(-8, 4);
                        ctx.closePath();
                        this._applyArrowFill(ctx, highlighted);
                        ctx.fill();
                        ctx.restore();

                        // label near control point
                        if (this.getRenderConfig().showEdgeLabels && this.shouldDisplayEdgeLabel(i, j)) {
                            const fontPx = this._getEdgeLabelFontPx();
                            const labelPos = this._getCurvedLabelPosition(cpF, this._getMidpoint(from, to));
                            const text = this._formatEdgeProbability(this.transitionMatrix[i][j]);
                            this._drawEdgeLabel(ctx, text, labelPos.x, labelPos.y, highlighted, fontPx);
                        }
                    } else {
                        const highlighted = hoveredIndex === i;
                        this._applyEdgeStroke(ctx, highlighted);

                        const { r, pad } = this._getNodeRadiusWithPad();
                        const seg = this._shrinkSegmentToNodes(from, to, r, r, pad);

                        ctx.beginPath();
                        ctx.moveTo(seg.a.x, seg.a.y);
                        ctx.lineTo(seg.b.x, seg.b.y);
                        ctx.stroke();

                        const { tip, angle } = this._placeArrowOnStraight(seg.a, seg.b, 0, 0);
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

                        if (this.getRenderConfig().showEdgeLabels && this.shouldDisplayEdgeLabel(i, j)) {
                            const fontPx = this._getEdgeLabelFontPx();
                            const labelPos = this._getStraightLabelPosition(seg);
                            const text = this._formatEdgeProbability(this.transitionMatrix[i][j]);
                            this._drawEdgeLabel(ctx, text, labelPos.x, labelPos.y, highlighted, fontPx);
                        }
                    }
                } else if (hasBackward) {
                    // Single direction arrow in the reverse direction (j -> i) or curved if chain requests
                    const mode = this.getEdgeRenderMode();
                    if (mode === 'curved_all') {
                        const dx = to.x - from.x;
                        const dy = to.y - from.y;
                        const perpX = -dy * 0.15;
                        const perpY = dx * 0.15;
                        const highlighted = hoveredIndex === j;
                        this._applyEdgeStroke(ctx, highlighted);
                        const { r, pad } = this._getNodeRadiusWithPad();
                        const cpB = { x: from.x + dx * 0.5 - perpX, y: from.y + dy * 0.5 - perpY };
                        ctx.beginPath();
                        ctx.moveTo(to.x, to.y);
                        ctx.quadraticCurveTo(cpB.x, cpB.y, from.x, from.y);
                        ctx.stroke();

                        const { tip, angle } = this._placeArrowOnQuadratic(to, cpB, from, from, r, pad);
                        ctx.save();
                        ctx.translate(tip.x, tip.y);
                        ctx.rotate(angle);
                        ctx.beginPath();
                        ctx.moveTo(0, 0);
                        ctx.lineTo(-8, -4);
                        ctx.lineTo(-8, 4);
                        ctx.closePath();
                        this._applyArrowFill(ctx, highlighted);
                        ctx.fill();
                        ctx.restore();

                        if (this.getRenderConfig().showEdgeLabels && this.shouldDisplayEdgeLabel(j, i)) {
                            const fontPx = this._getEdgeLabelFontPx();
                            const labelPos = this._getCurvedLabelPosition(cpB, this._getMidpoint(from, to));
                            const text = this._formatEdgeProbability(this.transitionMatrix[j][i]);
                            this._drawEdgeLabel(ctx, text, labelPos.x, labelPos.y, highlighted, fontPx);
                        }
                    } else {
                        const highlighted = hoveredIndex === j;
                        this._applyEdgeStroke(ctx, highlighted);

                        const { r, pad } = this._getNodeRadiusWithPad();
                        const seg = this._shrinkSegmentToNodes(to, from, r, r, pad);

                        ctx.beginPath();
                        ctx.moveTo(seg.a.x, seg.a.y);
                        ctx.lineTo(seg.b.x, seg.b.y);
                        ctx.stroke();

                        const { tip, angle } = this._placeArrowOnStraight(seg.a, seg.b, 0, 0);
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

                        if (this.getRenderConfig().showEdgeLabels && this.shouldDisplayEdgeLabel(j, i)) {
                            const fontPx = this._getEdgeLabelFontPx();
                            const labelPos = this._getStraightLabelPosition(seg);
                            const text = this._formatEdgeProbability(this.transitionMatrix[j][i]);
                            this._drawEdgeLabel(ctx, text, labelPos.x, labelPos.y, highlighted, fontPx);
                        }
                    }
                }
            }
        }
    }

    drawSelfLoops(ctx, positions, centerX, centerY, hoveredIndex = null) {
        // Draw self-loops on top of everything else
        for (let i = 0; i < this.states.length; i++) {
            if (this.transitionMatrix[i][i] > 0) {
                this.drawSelfLoop(ctx, positions[i], i, this.transitionMatrix[i][i], centerX, centerY, hoveredIndex);
            }
        }
    }

    drawDots(ctx, positions) {
        ctx.fillStyle = '#FF0000';
        for (let dot of this.dots) {
            let dotX, dotY;

            if (dot.isMoving && dot.nextState !== dot.currentState) {
                // Interpolate position between current and next state
                const currentPos = positions[dot.currentState];
                const nextPos = positions[dot.nextState];
                const progress = this.easeInOutQuad(dot.animationProgress);

                const stateX = currentPos.x + (nextPos.x - currentPos.x) * progress;
                const stateY = currentPos.y + (nextPos.y - currentPos.y) * progress;

                dotX = stateX + dot.x + (dot.targetX - dot.x) * progress;
                dotY = stateY + dot.y + (dot.targetY - dot.y) * progress;
            } else {
                // Dot is in a single state
                const state = positions[dot.currentState];
                dotX = state.x + dot.x;
                dotY = state.y + dot.y;
            }

            ctx.beginPath();
            ctx.arc(dotX, dotY, 5, 0, 2 * Math.PI);
            ctx.fill();
        }
    }

    drawNodes(ctx, positions) {
        const nodeRadius = this._getUniformNodeRadius();
        const fontSize = this.getNodeFontSize ? this.getNodeFontSize() : 16;
        for (let i = 0; i < this.states.length; i++) {
            const pos = positions[i];

            ctx.beginPath();
            ctx.arc(pos.x, pos.y, nodeRadius, 0, 2 * Math.PI);
            ctx.fillStyle = '#E0E0E0';
            ctx.fill();
            ctx.strokeStyle = '#999';
            ctx.lineWidth = 2;
            ctx.stroke();

            ctx.fillStyle = '#333';
            ctx.font = `bold ${fontSize}px Arial`;
            ctx.textAlign = 'center';
            ctx.textBaseline = 'middle';
            ctx.fillText(this.stateNames[i], pos.x, pos.y);
        }
    }

    getStateProbabilities() {
        return this.stateCount.map(count => count / this.numDots);
    }

    isAnimating() {
        return this.dots.some(dot => dot.isMoving);
    }

    reset() {
        this.initializeDots();
    }

    setNumDots(num) {
        this.numDots = num;
        this.initializeDots();
    }

    setAnimationSpeed(stepIntervalMs) {
        // Set the duration for each step animation
        this.stepDuration = stepIntervalMs;
        this.animationSpeed = 500 / stepIntervalMs; // Keep for compatibility
    }

    updateTransitionMatrix(newMatrix) {
        // Validate that each row sums to 1 (within tolerance)
        for (let i = 0; i < newMatrix.length; i++) {
            const rowSum = newMatrix[i].reduce((sum, val) => sum + val, 0);
            if (Math.abs(rowSum - 1.0) > 0.001) {
                throw new Error(`Row ${i} does not sum to 1.0 (sum = ${rowSum.toFixed(3)})`);
            }
        }

        this.transitionMatrix = newMatrix.map(row => [...row]); // Deep copy
        return true;
    }

    drawSelfLoop(ctx, position, stateIndex, probability, centerX, centerY, hoveredIndex = null) {
        // Orientation: 'radial' (default) or 'up' (always above node)
        const cfg = this.getRenderConfig ? this.getRenderConfig() : null;
        const orientation = (cfg && cfg.selfLoopOrientation) || 'radial';

        // Compute outward and perpendicular directions
        let ux, uy;
        if (orientation === 'up') {
            ux = 0; uy = -1;
        } else {
            ux = position.x - centerX;
            uy = position.y - centerY;
            const len = Math.hypot(ux, uy);
            if (len < 1e-6) {
                // Fallback if node is at center
                ux = 0; uy = -1;
            } else {
                ux /= len; uy /= len;
            }
        }
        const vx = -uy; // rotate (ux,uy) by +90° to get perpendicular
        const vy = ux;

        // Helper to map local loop coordinates to world using basis (v, -u)
        const mapLocal = (dx, dy) => ({
            x: position.x + dx * vx - dy * ux,
            y: position.y + dx * vy - dy * uy
        });

        const { r, pad } = this._getNodeRadiusWithPad();
        const loopScale = cfg && Number.isFinite(cfg.selfLoopScale) ? cfg.selfLoopScale : 1.2;

        // Local control points (scaled with r so loop stays outside node)
        const p0 = mapLocal(-20 * loopScale, -10 * loopScale);
        const cp = mapLocal(0, -(r + 35 * loopScale));
        const p2 = mapLocal(20 * loopScale, -10 * loopScale);

        // Draw loop curve with same style as normal arrows
        const highlighted = hoveredIndex === stateIndex;
        this._applyEdgeStroke(ctx, highlighted);
        ctx.beginPath();
        ctx.moveTo(p0.x, p0.y);
        ctx.quadraticCurveTo(cp.x, cp.y, p2.x, p2.y);
        ctx.stroke();

        const { tip, angle } = this._placeArrowOnQuadratic(p0, cp, p2, position, r, pad);
        ctx.save();
        ctx.translate(tip.x, tip.y);
        ctx.rotate(angle);
        ctx.beginPath();
        ctx.moveTo(0, 0);
        ctx.lineTo(-8, -4);
        ctx.lineTo(-8, 4);
        ctx.closePath();
        this._applyArrowFill(ctx, highlighted);
        ctx.fill();
        ctx.restore();

        if (this.getRenderConfig().showEdgeLabels && this.shouldDisplaySelfLoopLabel(stateIndex)) {
            const fontPx = this._getEdgeLabelFontPx();
            const labelPos = this._getLoopLabelPosition(p0, cp, p2);
            const text = this._formatEdgeProbability(probability);
            this._drawEdgeLabel(ctx, text, labelPos.x, labelPos.y, highlighted, fontPx);
        }
    }

    // Highlight helpers
    drawOutgoingHighlight(ctx, positions, i) {
        ctx.save();
        ctx.strokeStyle = '#ff5722';
        ctx.fillStyle = '#ff5722';
        ctx.lineWidth = 3;

        for (let j = 0; j < this.states.length; j++) {
            if (this.transitionMatrix[i][j] <= 0 || i === j) continue;
            const a = Math.min(i, j);
            const b = Math.max(i, j);
            const A = positions[a];
            const B = positions[b];
            const dx = B.x - A.x;
            const dy = B.y - A.y;
            const perpX = -dy * 0.15;
            const perpY = dx * 0.15;
            const hasForwardAB = this.transitionMatrix[a][b] > 0;
            const hasBackwardBA = this.transitionMatrix[b][a] > 0;

            if (hasForwardAB && hasBackwardBA) {
                // Use curved path consistent with base drawing
                if (i === a) {
                    // i -> j corresponds to forward (A -> B)
                    const cpX = A.x + dx * 0.5 + perpX;
                    const cpY = A.y + dy * 0.5 + perpY;
                    ctx.beginPath();
                    ctx.moveTo(A.x, A.y);
                    ctx.quadraticCurveTo(cpX, cpY, B.x, B.y);
                    ctx.stroke();

                    // Arrowhead along curve
                    const t = 0.85;
                    const qx = (1 - t) * (1 - t) * A.x + 2 * (1 - t) * t * cpX + t * t * B.x;
                    const qy = (1 - t) * (1 - t) * A.y + 2 * (1 - t) * t * cpY + t * t * B.y;
                    const ddx = 2 * (1 - t) * (cpX - A.x) + 2 * t * (B.x - cpX);
                    const ddy = 2 * (1 - t) * (cpY - A.y) + 2 * t * (B.y - cpY);
                    const angle = Math.atan2(ddy, ddx);
                    ctx.save();
                    ctx.translate(qx, qy);
                    ctx.rotate(angle);
                    ctx.beginPath();
                    ctx.moveTo(0, 0);
                    ctx.lineTo(-8, -4);
                    ctx.lineTo(-8, 4);
                    ctx.closePath();
                    ctx.fill();
                    ctx.restore();
                } else {
                    // i -> j corresponds to backward (B -> A)
                    const cpX = A.x + dx * 0.5 - perpX;
                    const cpY = A.y + dy * 0.5 - perpY;
                    ctx.beginPath();
                    ctx.moveTo(B.x, B.y);
                    ctx.quadraticCurveTo(cpX, cpY, A.x, A.y);
                    ctx.stroke();

                    const t = 0.85;
                    const qx = (1 - t) * (1 - t) * B.x + 2 * (1 - t) * t * cpX + t * t * A.x;
                    const qy = (1 - t) * (1 - t) * B.y + 2 * (1 - t) * t * cpY + t * t * A.y;
                    const ddx = 2 * (1 - t) * (cpX - B.x) + 2 * t * (A.x - cpX);
                    const ddy = 2 * (1 - t) * (cpY - B.y) + 2 * t * (A.y - cpY);
                    const angle = Math.atan2(ddy, ddx);
                    ctx.save();
                    ctx.translate(qx, qy);
                    ctx.rotate(angle);
                    ctx.beginPath();
                    ctx.moveTo(0, 0);
                    ctx.lineTo(-8, -4);
                    ctx.lineTo(-8, 4);
                    ctx.closePath();
                    ctx.fill();
                    ctx.restore();
                }
            } else {
                // Single straight path i -> j
                const from = positions[i];
                const to = positions[j];
                ctx.beginPath();
                ctx.moveTo(from.x, from.y);
                ctx.lineTo(to.x, to.y);
                ctx.stroke();

                const angle = Math.atan2(to.y - from.y, to.x - from.x);
                const arrowX = to.x - 35 * Math.cos(angle);
                const arrowY = to.y - 35 * Math.sin(angle);
                ctx.save();
                ctx.translate(arrowX, arrowY);
                ctx.rotate(angle);
                ctx.beginPath();
                ctx.moveTo(0, 0);
                ctx.lineTo(-10, -5);
                ctx.lineTo(-10, 5);
                ctx.closePath();
                ctx.fill();
                ctx.restore();
            }
        }
        ctx.restore();
    }

    drawNodeHighlightRings(ctx, positions, i) {
        ctx.save();
        ctx.strokeStyle = '#ff9800';
        ctx.lineWidth = 3;
        // hovered node
        ctx.beginPath();
        ctx.arc(positions[i].x, positions[i].y, 34, 0, 2 * Math.PI);
        ctx.stroke();
        ctx.restore();
    }

    drawSelfLoopHighlight(ctx, position, stateIndex, centerX, centerY) {
        // Recompute the same loop geometry as drawSelfLoop, but just the stroke in highlight color
        let ux = position.x - centerX;
        let uy = position.y - centerY;
        const len = Math.hypot(ux, uy);
        if (len < 1e-6) { ux = 0; uy = -1; } else { ux /= len; uy /= len; }
        const vx = -uy; const vy = ux;
        const mapLocal = (dx, dy) => ({ x: position.x + dx * vx - dy * ux, y: position.y + dx * vy - dy * uy });
        const p0 = mapLocal(-20, -10);
        const cp = mapLocal(0, -70);
        const p2 = mapLocal(20, -10);

        ctx.save();
        ctx.strokeStyle = '#ff5722';
        ctx.lineWidth = 3;
        ctx.beginPath();
        ctx.moveTo(p0.x, p0.y);
        ctx.quadraticCurveTo(cp.x, cp.y, p2.x, p2.y);
        ctx.stroke();
        ctx.restore();
    }

    easeInOutQuad(t) {
        return t < 0.5 ? 2 * t * t : -1 + (4 - 2 * t) * t;
    }

    getTheoreticalSteadyState() {
        return null;
    }

    // Compute distribution evolution over n steps
    getDistributionEvolution(steps = 10) {
        const n = this.states.length;
        const evolution = [];

        // Start with initial distribution
        let currentDist = [...this.initialDistribution];
        evolution.push([...currentDist]);

        // Compute distribution at each step
        for (let step = 1; step <= steps; step++) {
            const nextDist = new Array(n).fill(0);

            // Matrix multiplication: nextDist = currentDist * transitionMatrix
            for (let i = 0; i < n; i++) {
                for (let j = 0; j < n; j++) {
                    nextDist[j] += currentDist[i] * this.transitionMatrix[i][j];
                }
            }

            evolution.push([...nextDist]);
            currentDist = nextDist;
        }

        return evolution;
    }

    // Default render configuration; chains can override
    getRenderConfig() {
        return {
            canvasHeight: 400,
            showStats: true,
            showTransitionMatrix: true,
            showEdgeLabels: true,
            edgePadding: 2,
            selfLoopScale: 1.2,
            nodeRadius: 30
        };
    }

    // Optional editors; override in subclasses to expose editable configs
    getEditors() {
        return [];
    }

    // Edge rendering mode: 'mixed' (default) or 'curved_all'
    getEdgeRenderMode() {
        return 'mixed';
    }

    // Node drawing radius in world units (override via render config in future)
    getNodeRadius() {
        return 30;
    }

    // --- Geometry helpers for precise arrow placement ---
    _getNodeRadiusWithPad() {
        const cfg = this.getRenderConfig ? this.getRenderConfig() : null;
        const pad = cfg && Number.isFinite(cfg.edgePadding) ? cfg.edgePadding : 2;
        const r = this._getUniformNodeRadius();
        return { r, pad };
    }

    _getUniformNodeRadius() {
        const cfg = this.getRenderConfig ? this.getRenderConfig() : null;
        if (cfg && Number.isFinite(cfg.nodeRadius)) return cfg.nodeRadius;
        return 30;
    }

    getNodeSpacing() {
        return 4 * this._getUniformNodeRadius();
    }

    _applyEdgeStroke(ctx, highlighted) {
        ctx.strokeStyle = highlighted ? '#ff5722' : '#999';
        ctx.lineWidth = highlighted ? 3 : 2;
    }

    _applyArrowFill(ctx, highlighted) {
        ctx.fillStyle = highlighted ? '#ff5722' : '#666';
    }

    _drawEdgeLabel(ctx, text, x, y, highlighted, fontPx) {
        ctx.fillStyle = highlighted ? '#ff5722' : '#666';
        ctx.font = (highlighted ? 'bold ' : '') + `${fontPx}px Arial`;
        ctx.textAlign = 'center';
        ctx.fillText(text, x, y);
    }

    _getEdgeLabelFontPx() {
        return 12;
    }
    _getLabelOffsetAmount() {
        return 8;
    }

    _formatEdgeProbability(prob) {
        if (prob <= 0) return '0.00';
        const fixed = prob.toFixed(2);
        if (fixed !== '0.00') return fixed;
        const expStr = prob.toExponential(1); // e.g. "2.0e-6"
        const [coeffRaw, exponentRaw] = expStr.split('e');
        const coeff = parseFloat(coeffRaw).toString();
        const exponent = parseInt(exponentRaw, 10);
        const expFormatted = exponent >= 0 ? `${exponent}` : `-${Math.abs(exponent)}`;
        return `${coeff}e^${expFormatted}`;
    }

    shouldDisplayEdgeLabel(fromIndex, toIndex) {
        return true;
    }

    shouldDisplaySelfLoopLabel(stateIndex) {
        return true;
    }

    shouldDrawEdge(fromIndex, toIndex) {
        return true;
    }

    _getMidpoint(p1, p2) {
        return { x: (p1.x + p2.x) / 2, y: (p1.y + p2.y) / 2 };
    }

    _getStraightLabelPosition(seg) {
        const mid = this._getMidpoint(seg.a, seg.b);
        const dy = seg.b.y - seg.a.y;
        let sign;
        if (Math.abs(dy) < 1e-3) {
            sign = -1; // default above for horizontal edges
        } else {
            sign = dy > 0 ? 1 : -1;
        }
        const offset = this._getLabelOffsetAmount() * sign;
        return { x: mid.x, y: mid.y + offset };
    }

    _getCurvedLabelPosition(anchor, reference) {
        const offset = this._getLabelOffsetAmount();
        const dir = { x: anchor.x - reference.x, y: anchor.y - reference.y };
        const len = Math.hypot(dir.x, dir.y);
        if (len < 1e-3) {
            return { x: anchor.x, y: anchor.y - offset };
        }
        return {
            x: anchor.x + (dir.x / len) * offset,
            y: anchor.y + (dir.y / len) * offset
        };
    }

    _getLoopLabelPosition(p0, cp, p2) {
        const mid = this._quadraticPoint(p0, cp, p2, 0.5);
        const outward = { x: mid.x - cp.x, y: mid.y - cp.y };
        let len = Math.hypot(outward.x, outward.y);
        let dir;
        if (len < 1e-3) {
            const tangent = this._quadraticTangent(p0, cp, p2, 0.5);
            const tlen = Math.hypot(tangent.x, tangent.y) || 1;
            dir = { x: -tangent.y / tlen, y: tangent.x / tlen };
        } else {
            dir = { x: outward.x / len, y: outward.y / len };
        }
        const offset = this._getLabelOffsetAmount() * (-2);
        return { x: mid.x + dir.x * offset, y: mid.y + dir.y * offset };
    }

    _shrinkSegmentToNodes(p0, p1, r0, r1, pad) {
        // return endpoints of the edge trimmed so it doesn't enter the disks
        const dx = p1.x - p0.x, dy = p1.y - p0.y;
        const L = Math.hypot(dx, dy) || 1;
        const ux = dx / L, uy = dy / L;
        return {
            a: { x: p0.x + (r0 + pad) * ux, y: p0.y + (r0 + pad) * uy },
            b: { x: p1.x - (r1 + pad) * ux, y: p1.y - (r1 + pad) * uy },
            angle: Math.atan2(uy, ux)
        };
    }

    _placeArrowOnStraight(from, to, rTo, pad) {
        const dx = to.x - from.x, dy = to.y - from.y;
        const L = Math.hypot(dx, dy) || 1;
        const ux = dx / L, uy = dy / L;
        const tip = { x: to.x - (rTo + pad) * ux, y: to.y - (rTo + pad) * uy };
        const angle = Math.atan2(uy, ux);
        return { tip, angle };
    }

    _placeArrowOnQuadratic(p0, cp, p1, target, rTo, pad) {
        // find t where the curve reaches the target circle boundary; robust with fallback
        let t = this._findBezierBoundaryT(p0, cp, p1, target, rTo + pad);
        if (t == null) {
            // Fallback: pick a near-end t and project tip to the target circle along radial direction
            t = 0.97;
            const near = this._quadraticPoint(p0, cp, p1, t);
            const dirx = near.x - target.x;
            const diry = near.y - target.y;
            const L = Math.hypot(dirx, diry) || 1;
            const nx = dirx / L;
            const ny = diry / L;
            const tip = { x: target.x + nx * (rTo + pad), y: target.y + ny * (rTo + pad) };
            const dq = this._quadraticTangent(p0, cp, p1, t);
            const angle = Math.atan2(dq.y, dq.x);
            return { tip, angle };
        } else {
            const q  = this._quadraticPoint(p0, cp, p1, t);
            const dq = this._quadraticTangent(p0, cp, p1, t);
            const angle = Math.atan2(dq.y, dq.x);
            return { tip: q, angle };
        }
    }

    // --- Arrowhead helpers (canvas-only, boundary-aware) ---
    _quadraticPoint(p0, cp, p1, t) {
        const mt = 1 - t;
        return {
            x: mt * mt * p0.x + 2 * mt * t * cp.x + t * t * p1.x,
            y: mt * mt * p0.y + 2 * mt * t * cp.y + t * t * p1.y
        };
    }

    _quadraticTangent(p0, cp, p1, t) {
        return {
            x: 2 * (1 - t) * (cp.x - p0.x) + 2 * t * (p1.x - cp.x),
            y: 2 * (1 - t) * (cp.y - p0.y) + 2 * t * (p1.y - cp.y)
        };
    }

    _findBezierBoundaryT(p0, cp, p1, target, radiusPlusPad) {
        // Try to bracket a sign change for f(t) = dist(bezier(t), center) - (r+pad) near t -> 1, then bisect
        const f = (t) => {
            const q = this._quadraticPoint(p0, cp, p1, t);
            const dx = q.x - target.x;
            const dy = q.y - target.y;
            return Math.hypot(dx, dy) - radiusPlusPad;
        };
        // Descending samples toward 1 to find a bracket
        const samples = [0.6, 0.75, 0.85, 0.92, 0.96, 0.98, 0.99, 0.995];
        let lo = null, hi = null, flo = null, fhi = null;
        // Ensure we look for a sign change (f crosses 0)
        let prevT = samples[0], prevF = f(prevT);
        for (let k = 1; k < samples.length; k++) {
            const t = samples[k];
            const ft = f(t);
            if (prevF === 0) { lo = prevT; hi = t; flo = prevF; fhi = ft; break; }
            if (ft === 0)  { lo = prevT; hi = t; flo = prevF; fhi = ft; break; }
            if (prevF * ft < 0) {
                lo = prevT; hi = t; flo = prevF; fhi = ft; break;
            }
            prevT = t; prevF = ft;
        }
        if (lo == null) {
            // No bracket found: return null to trigger fallback
            return null;
        }
        // Bisection
        for (let iter = 0; iter < 22; iter++) {
            const mid = (lo + hi) * 0.5;
            const fm = f(mid);
            if (Math.abs(fm) < 0.25) return mid;
            if (flo * fm <= 0) {
                hi = mid; fhi = fm;
            } else {
                lo = mid; flo = fm;
            }
        }
        return (lo + hi) * 0.5;
    }

    _computeStraightTip(from, to, targetRadius, padding) {
        const dx = to.x - from.x;
        const dy = to.y - from.y;
        const len = Math.hypot(dx, dy) || 1;
        const ux = dx / len;
        const uy = dy / len;
        const tip = {
            x: to.x - (targetRadius + padding) * ux,
            y: to.y - (targetRadius + padding) * uy
        };
        const angle = Math.atan2(uy, ux);
        return { tip, angle };
    }
}