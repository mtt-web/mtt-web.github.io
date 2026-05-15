class GamblersRuin extends MarkovChain {
    constructor() {
        const wealth = 6;
        const p = 0.4;
        const q = 1 - p;

        const states = [];
        const stateNames = [];
        const transitionMatrix = [];

        for (let i = 0; i <= wealth; i++) {
            states.push(i);
            stateNames.push(`$${i}`);
            const row = new Array(wealth + 1).fill(0);

            if (i === 0) {
                row[0] = 1;
            } else if (i === wealth) {
                row[wealth] = 1;
            } else {
                row[i - 1] = q;
                row[i + 1] = p;
            }

            transitionMatrix.push(row);
        }

        super({
            name: "Gambler's Ruin",
            description: "A gambler with limited wealth playing an unfair game (p=0.4, q=0.6)",
            states: states,
            stateNames: stateNames,
            transitionMatrix: transitionMatrix,
            initialDistribution: states.map((_, i) => i === Math.floor(wealth/2) ? 1 : 0)
        });

        this.wealth = wealth;
        this.p = p;
    }

    getTheoreticalSteadyState() {
        const r = (1 - this.p) / this.p;
        const startingWealth = Math.floor(this.wealth / 2);

        let ruinProb;
        if (this.p === 0.5) {
            ruinProb = 1 - startingWealth / this.wealth;
        } else {
            ruinProb = (Math.pow(r, startingWealth) - Math.pow(r, this.wealth)) /
                       (1 - Math.pow(r, this.wealth));
        }

        const steadyState = new Array(this.wealth + 1).fill(0);
        steadyState[0] = ruinProb;
        steadyState[this.wealth] = 1 - ruinProb;

        return steadyState;
    }
}

chainModules.push(GamblersRuin);