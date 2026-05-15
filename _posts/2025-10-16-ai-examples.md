---
layout: single
title: Open problems solved by AI
date: 2025-10-16 10:00:00 +0100
categories: blog math ai
tags: [AI, math]
katex: true
header:
  teaser: /assets/images/thumbs/feghalli_thumb.png
---

{% include footnotes.html %}

What are the current math capabilities of AI? A huge question; I want to contribute with the following experiment. I will take a clearly defined open problem in mathematics<footnote>also open to me, i.e., I don't know the solution</footnote> and give it to an AI model. The only input to the model is the problem itself, possibly with some generic prompting (“You are a research mathematician…”). Can AI already solve such open problems?

It can; kind of. I tried this for about 50 problems with GPT-5 and obtained a few results I consider nontrivial. I will share two examples in combinatorics; aside from the problem statement, the following solutions are 100% AI, including writeup.

It should be said that the following two questions can barely be called "open problems". The questions are not well-known and not even conjectured in a peer-reviewed article. I learned of one problem from a somewhat obscure booklet of open problems and the other from a research pitch by a fellow mathematician. Yet these are questions that someone working in the relevant area wondered about and did not immediately have a solution for. <footnote>Of course, I'm certain they could solve them, if they spent more time. </footnote>


### Context
I'm not aware of others showcasing examples of this kind. There are benchmarks like [FrontierMath](https://epoch.ai/frontiermath) and [Humanity's last exam](https://agi.safe.ai/). These are extremely useful, but different in that the problems under question have a known solution, so it's a bit harder to gauge what, e.g., "50% of problems solved" means. 

Many mathematicians also use AI in their work, but it is typically hard to disentangle what part of the work is done by human and what part by the AI.<footnote>See <a href="https://x.com/g_leech_/status/1974165458283860198">discussion here</a> or <a href="https://mathstodon.xyz/@tao/115306424727150237">here</a>.</footnote> So this is again different from these strict requirements. 

## Tightness of a recent paper on 2D tiling

There was a [cool recent paper](https://arxiv.org/pdf/2408.02151) about tiling the plane $\mathbb{R}^2$ with polygonal tiles. One of the results in that paper is that every tiling of the plane with rational polygonal tiles is _weakly periodic_: it can be partitioned into finitely many pieces, each of them singly-periodic.

Can this theorem be improved? Can we prove that every polygonal tiling is simply _periodic_? It turns out that the answer is no. AI can find a specific example of a polygonal tiling that is weakly periodic but not periodic.

[Here's the example](/assets/documents/tiling_solution.pdf) (fully AI written).<footnote>The question asked to the model is the one formulated in the first section of the paper and asks for an example in a slightly different space than $\mathbb{R}^2$, but it extends to $\mathbb{R}^2$ via arguments in the original paper.</footnote>

## A counterexample to a problem about functions

Here's a fact about functions that has applications in graph theory.<footnote>First proven <a href="https://onlinelibrary.wiley.com/doi/abs/10.1002/jgt.10146">here</a>.</footnote>

Consider two functions $f$ and $g$ from a set $E$ into a set $F$ such that $f(x) \neq g(x)$ for every $x \in E$. Suppose that there exists a positive integer $n$ such that for any element $z \in F$, either $\lvert f^{-1}(z) \rvert \le n$ or $\lvert g^{-1}(z) \rvert \le n$. Then $E$ can be partitioned into $2n + 1$ subsets $E_1, E_2, \dots, E_{2n+1}$ such that $f(E_i) \cap g(E_i) = \emptyset$ for each $1 \le i \le 2n + 1$.

Can we generalize this theorem to more functions? It is unclear how many sets would be needed in the partition if we generalize from $2$ functions to $k$ functions.

A particularly bold question was asked at KAMAK, the yearly retreat of combinatorics researchers from Charles University (first problem in [this booklet](https://kam.mff.cuni.cz/~kamak/static/problems/2020.pdf)). Maybe $2n+1$ sets is _still_ enough?

This turns out to be too optimistic. Given this problem, AI came up with a straightforward construction showing that if you generalize the problem to $k$ functions, you need to partition into at least $kn$ subsets.

[Here's the short proof](/assets/documents/feghali_solution.pdf) (fully AI-written).

## Beginnings

Neither of the solved problems is that interesting; if I solved a problem like this, I would not bother publishing, I would just write to the authors of the original paper and hope that something more interesting comes out of it. 

But look: beginnings always look like this. Take [the first program that beat a chess grandmaster](https://en.wikipedia.org/wiki/HiTech). A legitimate reaction to this event was that the grandmaster was "badly off form." Yet, Kasparov has been beaten less than 10 years later. Similarly, I expect that in the coming months we will see examples that look more and more legitimate and be surpassed within 10 years (and maybe much sooner). 
