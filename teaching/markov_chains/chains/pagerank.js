class PageRank extends MarkovChain {
    constructor() {
        // 15 nodes representing web pages:
        // 0: Hub (important page with many incoming links)
        // 1-5: Quality pages that link to Hub (partial interconnections)
        // 4: Fake important page (has incoming links from spam 5-12)
        // 5-12: Low-quality spam pages that only link to Fake page
        // 13-14: Additional quality pages

        const dampingFactor = 1.0;
        const numPages = 15;
        const teleportProb = (1 - dampingFactor) / numPages; // Uniform teleportation

        // Define the link structure
        const links = {
            0: [1, 2, 3, 4, 13, 14], // Hub links to quality pages and Fake★
            1: [0, 2],               // Quality1 links back to Hub and to Quality2 (no longer to Fake★)
            2: [0, 1, 3],         // Quality2 links to Hub, Quality1, Quality3
            3: [0, 2],            // Quality3 links to Hub and Quality2
            4: [1],               // Fake★ only links to Quality1 (minimal outlinks)
            5: [4],               // Spam1 → Fake★
            6: [4],               // Spam2 → Fake★
            7: [4],               // Spam3 → Fake★
            8: [4],               // Spam4 → Fake★
            9: [4],               // Spam5 → Fake★
            10: [4],              // Spam6 → Fake★
            11: [4],              // Spam7 → Fake★
            12: [4],              // Spam8 → Fake★
            13: [0, 2],           // Quality4 links to Hub and Quality2
            14: [0, 3]            // Quality5 links to Hub and Quality3
        };

        // Create transition matrix
        const transitionMatrix = PageRank.buildMatrixFromAlpha(numPages, links, dampingFactor);

        super({
            name: "PageRank",
            description: `PageRank algorithm (damping=${dampingFactor}). Shows how link structure affects page importance.`,
            states: Array.from({length: numPages}, (_, i) => i),
            stateNames: ['Hub', 'Quality1', 'Quality2', 'Quality3', 'Fake★', 'Spam1', 'Spam2', 'Spam3', 'Spam4', 'Spam5', 'Spam6', 'Spam7', 'Spam8', 'Quality4', 'Quality5'],
            transitionMatrix: transitionMatrix,
            initialDistribution: new Array(numPages).fill(1/numPages)  // Uniform start
        });

        this.dampingFactor = dampingFactor;
        this.links = links;
        this.numPages = numPages;
    }

    static buildMatrixFromAlpha(numPages, links, alpha) {
        const teleportProb = (1 - alpha) / numPages;
        const M = [];
        for (let i = 0; i < numPages; i++) {
            const row = new Array(numPages).fill(teleportProb);
            const outLinks = links[i] || [];
            if (outLinks.length > 0) {
                const linkProb = alpha / outLinks.length;
                for (const target of outLinks) {
                    row[target] += linkProb;
                }
            } else if (alpha > 0) {
                // Dangling node: distribute alpha uniformly
                const uniform = alpha / numPages;
                for (let j = 0; j < numPages; j++) row[j] += uniform;
            }
            M.push(row);
        }
        return M;
    }

    getNodePositions(centerX, centerY, radius) {
        // Custom layout to show the network structure clearly
        // Move non-spam (including hub/quality) left, and fake+spam further right
        const leftShift = -30;
        const rightShift = +60;
        const baseRightX = centerX + 340 + rightShift; // spam columns base x

        const positions = [
            { x: centerX + leftShift, y: centerY - 80 },      // 0: Hub
            { x: centerX - 180 + leftShift, y: centerY - 10 },// 1: Quality1
            { x: centerX - 30 + leftShift, y: centerY + 80 }, // 2: Quality2
            { x: centerX + 120 + leftShift, y: centerY - 10 },// 3: Quality3
            { x: centerX + 260 + rightShift, y: centerY - 80 },// 4: Fake★ (shifted right)
            // Spam rectangle: two columns x = baseRightX, baseRightX + 60; rows y = -120,-60,20,100
            { x: baseRightX, y: centerY - 120 },        // 5: Spam1
            { x: baseRightX, y: centerY - 60 },         // 6: Spam2
            { x: baseRightX, y: centerY + 20 },         // 7: Spam3
            { x: baseRightX, y: centerY + 100 },        // 8: Spam4
            { x: baseRightX + 60, y: centerY - 120 },   // 9: Spam5
            { x: baseRightX + 60, y: centerY - 60 },    // 10: Spam6
            { x: baseRightX + 60, y: centerY + 20 },    // 11: Spam7
            { x: baseRightX + 60, y: centerY + 100 },   // 12: Spam8
            { x: centerX - 240 + leftShift, y: centerY + 30 }, // 13: Quality4
            { x: centerX + 200 + leftShift, y: centerY + 60 }  // 14: Quality5
        ];
        return positions;
    }

    getTheoreticalSteadyState() {
        // Approximate PageRank values (rough, illustrative only)
        return [0.24, 0.11, 0.11, 0.09, 0.08,
                0.035, 0.035, 0.034, 0.034, 0.034, 0.034, 0.034, 0.034,
                0.042, 0.042];
    }

    getCustomControls() {
        return [
            {
                type: 'slider',
                label: 'Damping α',
                value: this.dampingFactor,
                min: 0.0,
                max: 1.0,
                step: 0.01,
                onChange: (value) => this.updateAlpha(parseFloat(value))
            }
        ];
    }

    updateAlpha(alpha) {
        const clamped = Math.max(0, Math.min(1, alpha));
        this.dampingFactor = clamped;
        this.transitionMatrix = PageRank.buildMatrixFromAlpha(this.numPages, this.links, clamped);
        this.description = `PageRank algorithm (damping=${clamped.toFixed(2)}). Shows how link structure affects page importance.`;
    }

    getRenderConfig() {
        return {
            canvasHeight: 450,
            showStats: true,
            showTransitionMatrix: false,  // Too cluttered to show
            showEdgeLabels: false         // Clean look without probability labels
        };
    }

    getEdgeRenderMode() {
        return 'curved_all'; // Use curved edges for cleaner visualization
    }

    // Override to show custom information about the network structure
    getCustomInfo() {
        return {
            title: "Network Analysis",
            content: [
                "• Hub: Receives links from quality pages → High PageRank",
                "• Quality pages: Well-connected to each other → Medium PageRank",
                "• Fake★: Gets links from spam pages, but they're not important → Low PageRank",
                "• Spam pages: Only link to Fake page → Very low PageRank",
                "",
                "This shows why link quality matters more than quantity!"
            ]
        };
    }
}

chainModules.push(PageRank);