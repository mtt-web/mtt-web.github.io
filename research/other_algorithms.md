---
layout: single # splash, single
title: Other algorithms
permalink: "/research/other_algorithms/"
katex: true
---

I enjoy working on all kinds of algorithms. 

## Parallel shortest paths

Dijkstra's algorithm is a very efficient solution for the problem of finding the shortest path in a graph. But this algorithm is very sequential, so is there also an efficient parallel algorithm for this problem? For a long time, there was no known shortest-path algorithm that would keep reasonable (near-linear) total work and had nontrivial (truly sublinear) depth. In a [paper](https://arxiv.org/abs/2210.16351) with [Christoph Grunau](https://people.inf.ethz.ch/cgrunau/), [Bernhard Haeupler](https://people.inf.ethz.ch/haeuplb/), [Anders Martinsson](https://as.inf.ethz.ch/people/members/maanders/index.html), [Goran Zuzic](https://goranzuzic.github.io/), we showed that there is really an algorithm with near-linear work and about $$\sqrt{n}$$ depth. 

![smoothing](/assets/images/smoothing.png "Parallel shortest paths")

In other [two](https://arxiv.org/abs/2204.05874) [papers](https://arxiv.org/abs/2204.08254) with [Christoph Grunau](https://people.inf.ethz.ch/cgrunau/), [Bernhard Haeupler](https://people.inf.ethz.ch/haeuplb/), [Michael Elkin](https://www.cs.bgu.ac.il/~elkinm/), [Jason Li](https://q3r.github.io/), and [Goran Zuzic](https://goranzuzic.github.io/), we first showed that on undirected graphs and if you are OK with computing just 1.01-approximate shortest paths, we can get the ultimate solution: a deterministic near-linear-time and polylogarithmic-depth algorithm. Then, we showed how this can be used to solve a bunch of other problems with the same near-optimal guarantees. The upshot is that a lot of graph problems can be solved with deterministic algorithms that are near-optimal in terms of both their overall running time and parallelizability. 

![clustering](/assets/images/parallel_clustering.png "Parallel algorithm for clustering")

## Parallel derandomization

Here's a big open computer science question: if there is a fast randomized algorithm for some problem, is there also a fast deterministic algorithm for it? Though we don't know the answer, we have a bunch of quite general techniques, like the method of conditional expectations, to turn randomized algorithms into deterministic ones. In a [paper](https://arxiv.org/abs/2311.13764) with [Mohsen Ghaffari](https://people.csail.mit.edu/ghaffari/) and [Christoph Grunau](https://people.inf.ethz.ch/cgrunau/), we showed a quite general way of implementing this method in parallel; this makes it a bit more plausible that one can not only efficiently derandomize algorithms, but also keep their depth. 

## Consistent k-center

Let's say that I want to compute some clustering on a set of points $$X$$; to be concrete, let's say I want to solve the k-center problem. The twist is that every day, $$X$$ gets updated -- either one new point arrives or one point from $$X$$ gets removed. 

The field of dynamic algorithms is about how to quickly update my solution every day. 
But there's an even more basic question to ask: after each update, is it possible to change only a few of my $$k$$ clusters and preserve that each day, my solution approximates the current optimum? This property is called consistency and in a [paper](https://arxiv.org/abs/2307.13747) with [Christoph Grunau](https://people.inf.ethz.ch/cgrunau/), [Bernhard Haeupler](https://people.inf.ethz.ch/haeuplb/), [Rajesh Jayaram](https://rajeshjayaram.com/), and [Jakub Lacki](https://www.mimuw.edu.pl/~jlacki/) we showed that it is achievable for the k-center problem. 

![kcenter](/assets/images/kcenter.png "Hierarchical algorithm for consistent k-center")