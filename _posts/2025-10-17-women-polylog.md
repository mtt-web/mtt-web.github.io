---
layout: single
title: Polylog gender studies
date: 2025-10-17 10:00:00 +0100
categories: blog polylog youtube
tags: [polylog-meta]
katex: true
header:
  teaser: /assets/images/thumbs/polylog_gender_thumb.png
---

{% include footnotes.html %}

Polylog is a mid-sized algorithms channel. Riddle: What share of viewers is male vs female? Make a guess before you go on.

<div id="gender-guess-root" data-answer="97.5"></div>
<script type="module" src="/widgets/dist/gender-guess.js"></script>

<div style="margin: 10rem 0; text-align: center; font-size: 1.2rem; color: #666; font-weight: 600; text-transform: uppercase; letter-spacing: 0.1em;">
⚠️ Spoiler Below ⚠️
</div>

Yeah. When I started working on Polylog, I had high hopes about videos being a nice way of democratizing computer science (CS) education. This could mean accessibility to people from third-world countries. But also to women: In my experience, many students first encounter math or computer science at math competitions, but these are tailored to boys. _Beat others at math_ simply isn't the best motivation for everybody. Maybe videos can do better! 

It seems that I have been mistaken, at least in the gender aspect. 

This post is not about eating humble pie or lamenting the state of the world. Self-reflection is in order, but first, I would like to understand where this huge discrepancy comes from. Can we break it down into several understandable factors?

Below is the best answer I got. 

First things first: I’ll take YouTube’s gender field of 2.5% female at face value as a correct number.<footnote>Is it clear this number is correct? It is possible that women are more likely not to disclose or may misreport their gender. It is also possible that YouTube's algorithm tries to infer the gender of users who don't disclose it and does a poor job. And because the share of women viewers is so low (and the share of trans people in tech relatively high), the share of nonbinary/trans viewers could change the interpretation of the number substantially. I do not consider any of this here.</footnote>
Also, 97.5% corresponds to odds of 39:1. It turns out that $\log_2(39)$ is about $5.3$, so I think of our task as explaining around $5$ bits of discrepancy with respect to the $1:1$ baseline in the population.


## Biased baseline

Gender bias in computer science is hard to miss. Let's try to estimate some baseline share of women in computer science and STEM in high-income countries. A few data points:

- in 2022, the share of women STEM researchers has been around 30% in the US and 40% worldwide.<footnote><a href="https://www.insidehighered.com/news/diversity/sex-gender/2024/06/11/women-make-global-gains-researchers-gaps-persist#:~:text=%E2%80%9CWe%20still%20have%20these%20perceptions,a%20quarter%20of%20mathematics%20researchers">Source</a></footnote>.
- in 2018, around 20% of AI researchers were women.<footnote><a href="https://www.insidehighered.com/news/diversity/sex-gender/2024/06/11/women-make-global-gains-researchers-gaps-persist#:~:text=%E2%80%9CWe%20still%20have%20these%20perceptions,a%20quarter%20of%20mathematics%20researchers">Source</a></footnote>
- in the late 2010s, around 20% of CS undergrads/PhD students/CS faculty were female.<footnote><a href="https://womenshistorymonth.cc.gatech.edu/changing-the-landscape/#:~:text=Nationally%2C%2018%20percent%20of%20undergraduate,students%20are%20female">Source</a></footnote>

I take it that the baseline ratio is ~80% male among CS-interested people. That's ~4:1 which means that we already found ≈2 bits in our 5.3-bit target.
 



## YouTube-general bias

It may be that men consume YouTube (or the Internet in general) more than women. I found this:

- In Britain, women are slightly more likely to visit YouTube; however, male visitors spend longer per day (men: 54 minutes, women: 40 minutes).<footnote><a href="https://www.ofcom.org.uk/siteassets/resources/documents/research-and-data/online-research/online-nation/2024/online-nation-2024-report.pdf?v=386238">Source</a></footnote>

- 54% of YouTube’s audience is male and 46% female in 2025.<footnote><a href="https://datareportal.com/essential-youtube-stats">DataReportal</a>, <a href="https://www.globalmediainsight.com/blog/youtube-users-statistics/#:~:text=Considering%20user%20gender%2C%20YouTube%20is,popular%20among%20men%20than%20women">Global Media Insight</a></footnote> But only 49% were male in the US in 2023.<footnote><a href="https://blog.hubspot.com/marketing/youtube-demographics#:~:text=,34.%20%28Statista">HubSpot/Statista</a></footnote>

I take it that this can perhaps explain a small part of our problem, but it's mostly negligible. 


## Maybe women have a life?

Imagine the pool of _STEM-loving people_, split 80-20 in favor of men. This doesn't mean that the share of _STEM-loving people watching YouTube_ will have the same split. In fact, I will now make a guess that it's more like 90-10 or 95-5. 

Here are two theories why. 

First, even if men and women want to spend comparable time on YouTube in general, maybe this is not true for men in STEM versus women in STEM. Thinking of my women friends in STEM, I entertain the hypothesis that they have social lives and hobbies. Also, some have kids. Or perhaps the fact that most large STEM YouTubers are men plays a role. 

The second suspect is the YouTube algorithm. While it's not public, we can at least reason about the incentives behind it. Imagine the following simple toy model. 

Think of a simple game where you keep flipping a biased coin that lands heads 80% of the time. Before each flip, you can predict the outcome and get a dollar if you predict correctly. To win the most money, the right strategy is to predict heads _every_ time, not 80% of the time. 

Similarly, imagine that you know that most women like videos about cooking, beauty, and animals, while most men like videos about cars, soccer, and computers. With what probability should you suggest a cooking video to a male viewer? Unless there are some other constraints (like aiming for diversity, etc.), the probability is zero. Every pair of a man shown cooking and a woman shown cars is an imperfect situation: swapping them earns money in expectation. 

### Getting an estimate

Whatever the reason, how do we estimate the proportion of women on STEM Youtube? In theory, we could pool the data from a few large channels like Veritasium or 3blue1brown and estimate it from their viewership. But I could not find data for these large channels. My guess is it could be about 85-95% male, but who knows. 

A cheap way of estimating this fraction is to look at the fraction of female/male science hosts and estimate that the fraction of viewers is roughly the same. I found an estimate that 8% of STEM hosts are female.<footnote>[source](https://pubmed.ncbi.nlm.nih.gov/29974815/#:~:text=video%20content,per%20view%2C%20and%20significantly%20higher)</footnote> But literally all large STEM YouTubers I can think of are men. This leads me to my guess that the split is around 90-10 to 95-5, with large uncertainty. 

If this is a reasonable guess, we explained around one or two more bits in the $5.3$-bit mystery.


## Birds of a feather

Here's the male/female ratio for some large YouTube channels where I could find it. Let's try to use it to make some sense of what's going on. <footnote>Sources: <a href="https://www.parrotanalytics.com/insights/amazons-mrbeast-series-is-primed-for-success/#:~:text=Specifically%20the%20demographics%20of%20MrBeast%E2%80%99s,be%20a%20challenge%20but%20also">MrBeast</a>, <a href="https://www.frontiersin.org/journals/communication/articles/10.3389/fcomm.2021.610920/full">It's Okay to Be Smart, Physics Girl, and Hydraulic Press</a>, <a href="https://www.edisonresearch.com/who-joe-rogan-listeners-are-likely-to-support-in-the-election/#:~:text=Known%20for%20its%20influence%20and,align%20with%20Democrats">Joe Rogan</a>, <a href="https://www.mediamonitors.com/audience-demographic-variations-specific-to-genre/?utm_source=chatgpt.com">Crime Junkie</a>. Generally, the sources were found by GPT and I did not cross-check them.
</footnote>

<div id="gender-table-root" data-channels='[
  {"name":"Crime Junkie","host":"Female","topic":"Crime","malePercent":15},
  {"name":"MrBeast","host":"Male","topic":"Entertainment","malePercent":74},
  {"name":"Its Okay to Be Smart","host":"Male","topic":"Science","malePercent":75},
  {"name":"Physics Girl","host":"Female","topic":"Science","malePercent":80},
  {"name":"Joe Rogan","host":"Male","topic":"Podcast","malePercent":80},
  {"name":"Hydraulic Press","host":"Balanced","topic":"I think you can guess it","malePercent":94}
]'></div>
<script type="module" src="/widgets/dist/gender-table.js"></script>


Here's where I believe we may find one last bit. If you look at the table above, you can see that viewership tends to follow host gender. MrBeast does game shows and Joe Rogan does podcasts; both sound pretty gender‑neutral to me, their audiences skew 3:1 and 4:1 male. On the other hand, Physics Girl has a phenomenal 20% share of women viewers, while our channel has 2.5%—clearly she's doing something different! Also, a woman‑hosted podcast about crime has only 15% male audience, while the general split for this type of content is apparently more balanced, 1:2 male to female.<footnote>[source](https://www.pewresearch.org/short-reads/2023/06/20/true-crime-podcasts-are-popular-in-the-us-particularly-among-women-and-those-with-less-formal-education/?utm_source=chatgpt.com)</footnote>

Correlation between the gender of the host and the audience doesn't seem too surprising to me. What is not clear to me is how the math works out if the base share of both hosts and audience is already so skewed. 

I will need a toy example. Imagine a world with 10 STEM viewers and 10 STEM content creators. In both cases, nine of them are men. At first, we will assume that each creator has the same share of audience. If each viewer watches each creator 10% of time, each creator sees 10% women rate. 

Now let's say that our female content creator is the Physics Girl with 80% male rate. Let's make this happen in our model: we will have to assume that the female viewer actually sends around twice as many views to the Physics Girl than to the rest. This has to be compensated by a smaller male-male bias. Hover over the squares in the widget to see the gender ratio of each creator in this new model with Physics Girl. 

<div id="viewer-creator-root"></div>
<script type="module" src="/widgets/dist/viewer-creator.js"></script>

The female viewer is sending 0.8/9 \approx 9% worth of viewership to the male creators, instead of 10%. That's actually a pretty small difference! The bias that the female viewer can create for the Physics Girl (90%->80%) is just way more impressive than what changes for all the other creators (90%->91%). 

It seems that we can't find that many bits in this effect, even though looking at Joe Rogan and MrBeast data, I thought this was going to be important. 

## Putting it together

My current guess is something like this:

<div id="bias-breakdown-root-2" data-segments='[
  {"label":"STEM baseline","value":2,"description":"20% of CS researchers/students are women"},
  {"label":"YouTube STEM baseline","value":2,"description":"Women in STEM watch less YouTube STEM content"},
  {"label":"Birds of a feather","value":0.3,"description":"Same-gender preference"},
  {"label":"Unique misogyny","value":1,"description":"Channel-specific gender bias?"}
]' data-total="5.3"></div>
<script type="module" src="/widgets/dist/bias-breakdown.js"></script>

There's still one last bit I am missing, and I remain confused. Are we uniquely misogynistic in our channel? Am I underestimating the red bar? <footnote>I found <a href="https://hci.ucsd.edu/papers/millions_views.pdf#:~:text=For%20example%2C%20C27%2C%20a%20female,male%E2%80%9D">a paper</a> that discusses this issue and cites some other content creators opinions on it. Somebody there claims that Veritasium channel is "95, 99% male". I thought it was a hyperbole while reading it, but maybe I am just underestimating how skewed STEM YouTube is by default. </footnote>
