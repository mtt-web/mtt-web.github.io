---
layout: single # splash, single
title: Instance optimality
permalink: "/research/instance_optimality/"
katex: true
---

What does it mean that an algorithm is the best one for a given task? The usual approach is to compute the time complexity of the algorithm as a function of $$n$$, always considering *the worst possible* $$n$$-bit input. Instance optimality is about getting an algorithm that is single-handedly the best one for *all* inputs at once. 


<!--
## Formally 

Very formally speaking, it means that our algorithm $$A$$ satisfies this:

$$
\exists C: \forall A': \forall n: \max_{\text{$n$-bit input $I$}} \text{time}(A, I) \le C \cdot \max_{\text{$n$-bit input $I$}} \text{time}(A', I)
$$

where $$\text{time}(A, I)$$ is the time that an algorithm $$A$$ spends on an instance $$I$$. 
For example, when we say that mergesort is the best possible algorithm for sorting (in the comparison model), this is what we have in mind. 

But we could be more adventurous. In fact, why shouldn't we try to construct algorithms that are the best possible on *every single input*? Such algorithms are called instance-optimal and formally, this is what instance optimality means: 

$$
\exists C: \forall A': \forall I: \text{time}(A, I) \le C \cdot \text{time}(A', I)
$$

This definition seems extremely silly: how can we compare ourselves with algorithm *print(42)* on inputs where the output is 42? To fix that, we require in the definition of instance-optimality that $$A'$$ is correct on all inputs, not just $$I$$. But even then, how can we beat this algorithm (that hardcodes the correct answer 42 on my_favorite_input)?

```
if(input==my_favorite_input)
    print(42);
else 
    some_correct_algorithm(input);
```

In most cases, this algorithm indeed kills any hope for instance optimality, but it turns out that in many interesting scenario, checking whether *input == my_favorite_input* is too costly and we can thus keep the hope for achieving instance optimality or some variant of it. Here are some examples:
-->

## Universally-optimal Dijkstra

Dijkstra's algorithm is one of the oldest and most important algorithms. We use it to solve the shortest path problem for which it is [not necessarily the best algorithm](https://arxiv.org/abs/2307.04139), but we know that it is the best possible algorithm for the problem of ordering the nodes by their distance from the root, provided it uses a [sufficiently efficient heap](https://en.wikipedia.org/wiki/Fibonacci_heap).  

[In a paper](https://arxiv.org/abs/2311.11793) with [Bernhard Haeupler](https://people.inf.ethz.ch/haeuplb/), [Richard Hladík](https://rihl.uralyx.cz/), [Robert Tarjan](https://www.cs.princeton.edu/people/profile/ret), and [Jakub Tětek](https://sites.google.com/view/jakub-tetek/), we showed that if the heap is even more efficient (has the so-called working-set property), Dijkstra's algorithm is even universally-optimal for this problem. 

Universal optimality is a property that lies between instance optimality and worst-case; it basically says that no matter how the input graph looks like, our algorithm adapts to it ideally (but we are worst-case with respect to the weights on it).  

[
![quanta picture](/assets/images/quanta.png "A picture from Quanta magazine")
](https://www.quantamagazine.org/computer-scientists-establish-the-best-way-to-traverse-a-graph-20241025/)
<small>A picture from [Quanta magazine](https://www.quantamagazine.org/computer-scientists-establish-the-best-way-to-traverse-a-graph-20241025/).</small>

## Sampling-based algorithms

Here's a fundamental problem in statistics: There is some unknown distribution $$D$$ supported on $$[0, 1]$$ and we want to estimate its mean -- let's say that our output should be correct up to additive $$\pm \epsilon$$ error with 99% probability. The standard solution is to sample $$O(1/\epsilon^2)$$ samples and return their mean; this is the best possible complexity in the worst-case sense. 
<!--
Here it is:

1. $$T_1 \leftarrow 1/\epsilon, \; x_1, \dots, x_{T_1} \leftarrow$$ sample from input
3. $$\hat{\mu} \leftarrow \frac{1}{T_1} \sum_{i = 1}^{T_1} x_i, \; \hat{\sigma}^2 \leftarrow \frac{1}{T_1-1} \sum_{i = 1}^{T_1} (\hat{\mu} - x_i)^2$$
5. $$T_2 \leftarrow O(1/\epsilon + \hat{\sigma}^2/\epsilon^2)$$
6. Sample additional $$T_2$$ samples and return their mean
-->

However, it turns out there is also an instance-optimal solution! 
Basically, you can first sample just a few samples and guess the variance of the distribution; this allows you to potentially lower the amount of samples you then need for the final estimate. We analyzed the instance-optimal estimation of the mean, median and some other problems in [a paper](https://arxiv.org/abs/2410.14643) with [Shyam Narayanan](https://sites.google.com/view/shyamnarayanan/home), [Jakub Tětek](https://sites.google.com/view/jakub-tetek/), and [Mikkel Thorup](http://hjemmesider.diku.dk/~mthorup/), and later found out that the mean case was already analyzed in [an earlier paper](https://epubs.siam.org/doi/abs/10.1137/S0097539797315306?download=true&journalCode=smjcat). 

## Distributed algorithms

In a series of amazing papers, [Bernhard Haeupler](https://people.inf.ethz.ch/haeuplb/) and many others developed the technology for designing approximately-universally-optimal algorithms for distributed algorithms. In [two](https://arxiv.org/abs/2204.05874) [papers](https://arxiv.org/abs/2204.08254) with [Christoph Grunau](https://people.inf.ethz.ch/cgrunau/), [Bernhard Haeupler](https://people.inf.ethz.ch/haeuplb/), [Michael Elkin](https://www.cs.bgu.ac.il/~elkinm/), [Jason Li](https://q3r.github.io/), and [Goran Zuzic](https://goranzuzic.github.io/), we showed how to design universally-optimal algorithms for a bunch of problems, including computing approximate shortest paths. 

![Distributed algorithm for approximate shortest paths](/assets/images/dist_paths.png "Distributed algorithm for approximate shortest paths")