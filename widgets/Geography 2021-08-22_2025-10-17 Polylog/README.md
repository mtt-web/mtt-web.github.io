Geography data (YouTube Studio export)

Files present in this folder:

- Table data.csv: Per-country breakdown with columns: Geography, Views, Watch time (hours), Average view duration.
- Chart data.csv: Time series by date; not used by the scatter plot.
- Totals.csv: Daily totals; not used by the scatter plot.

The page blog/analytics/geography.html reads Table data.csv and joins to blog/analytics/country_population.json to plot Views vs Population on log-log axes. Geography is typically an ISO-3166 alpha-2 code (e.g., US, GB), sometimes followed by a dash and name; a mapping is embedded in the page to the population JSON country keys.

