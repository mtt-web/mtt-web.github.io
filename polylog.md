---
layout: single # splash, single
title: Polylog
permalink: "/polylog/"
katex: true
header:
    teaser: /assets/images/vv_alps.jpg
---

We create videos about algorithmic topics, focusing on ideas that are not as well-known as they should be, or presenting well-known ideas in at least a slightly different way.
Ideally, our videos could be of interest to a very broad range of people interested in algorithms and computer science. 

Here are a few examples. 

## An algorithm for Rubik's cube

How do you create an algorithm for solving a scrambled Rubik's cube? This is harder than it looks and we introduce the concept of meet-in-the-middle on that problem. 

<iframe width="560" height="315" src="https://www.youtube.com/embed/wL3uWO-KLUE" title="YouTube video player" frameborder="0" allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture" allowfullscreen></iframe>

## A* algorithm

A* algorithm is an important heuristic variant of Dijkstra's algorithm used in practice. In this video, we develop the algorithm from the first principles, hopefully showcasing that it's actually deeper and more beautiful than how it looks at first glance. 

<iframe width="560" height="315" src="https://www.youtube.com/embed/A60q6dcoCjw" title="YouTube video player" frameborder="0" allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share" allowfullscreen></iframe>


## A different way of understanding P vs NP

We usually frame P vs NP as the question "If I can check the solution quickly, can I also solve the problem quickly?" In this video, we explain how you can equivalently view as the question "If I can quickly compute some function, can I quickly invert it?"

The video won the unofficial fourth [Summer of Math Exposition competition](https://www.linkedin.com/in/gaborhollbeck/?locale=de_DE). 

<iframe width="560" height="315" src="https://www.youtube.com/embed/6OPsH8PK7xM?si=oKdJlqE5LeW6F4ZI" title="YouTube video player" frameborder="0" allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share" referrerpolicy="strict-origin-when-cross-origin" allowfullscreen></iframe>

## Team

Check out the pages of the other team members, they all do amazing stuff!

- [Václav Volhejn](https://vvolhejn.com/) (we started the channel together)
- [Ríša Hladík](https://rihl.uralyx.cz/)
- [Tom Sláma](https://slama.dev/)
- [Filip Hlásek](http://filip.hlasek.org/)
- [Gábor Hollbeck](https://www.linkedin.com/in/gaborhollbeck/?locale=de_DE)

## Polyblog

I work mostly on writing scripts for the videos. There's always so much I want to say and can't, and there are always details we have to sweep under the rug. I always write about those [here](https://vasekrozhon.wordpress.com/), mostly just to be able to sleep well at night. But I am also humbled that a fraction of viewers reads it!

I also wrote [one blog post](https://vasekrozhon.wordpress.com/2023/09/24/heuristic-arguments/) there about heuristic arguments, a topic I was thinking about a lot at the time. There's a lot more I would love to write about, but I don't expect myself to manage that much of it in the coming years. 

## Why the name Polylog

There is an excellent reason for the name. We started working on it with [Václav Volhejn](https://vvolhejn.com/) when we both were ETH students. ETH is familiarly known to its students as "poly" (there's Polymensa, Polyterasse, Polyball, ...), so why not Polylog. 

Polylog is also a homonym for [polylogue](https://en.wiktionary.org/wiki/polylogue), which I am certain is deeply related to our channel somehow. 

The sad truth is that we did not know about above reasons at all; we just looked for something short, related to computer science, and Busy beavers [was taken](https://www.youtube.com/channel/UCbt63GNsB5wet6NO3dmhssA). 

In the algorithmic world, "polylog" is a shorthand for polylogarithmic function. That is, it denotes the function $$O(\log^C n)$$ for some constant $$C$$. You see this function all the time in the research papers, because in many contexts, we think of these factors as relatively negligible. It's a bit similar to how we often think of constant factors as negligible and hide them using the $$O$$ notation. In fact, in theoretical papers, you can often encounter $$\tilde{O}$$ notation that hides polylogarithmic factors. 

## Scaling laws

This image plots each country's population against the number of views of our videos from that country. Unless YouTube algorithm is doing something spooky, I think this measures some combination of how much people speak English / have access to Internet / care about computers in each country. It's definitely appealing to think of Canada as scaled up Australia which is itself scaled up New Zealand. 

[
![Scaling laws](/assets/images/polylog_withlines.png "Scaling laws")
](/assets/images/polylog_withlines.png)