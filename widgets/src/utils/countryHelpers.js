// Lightweight helpers for country name normalization, ISO codes, and flags.

let cache = null;

async function load() {
  if (cache) return cache;
  const [nameMap, flagMap] = await Promise.all([
    fetch('./country_name_map.json').then(r => r.json()),
    fetch('./country_flags.json').then(r => r.json()),
  ]);
  // Build reverse index if not provided
  const iso2ToName = nameMap.iso2ToName || {};
  const aliasesToName = nameMap.aliasesToName || {};
  let nameToIso = nameMap.nameToIso;
  if (!nameToIso) {
    nameToIso = {};
    for (const [iso, nm] of Object.entries(iso2ToName)) nameToIso[nm] = iso;
    for (const [alias, canon] of Object.entries(aliasesToName)) {
      if (nameToIso[canon]) nameToIso[alias] = nameToIso[canon];
    }
  }
  cache = { iso2ToName, nameToIso, aliasesToName, flagMap };
  return cache;
}

function isoToEmoji(iso2) {
  if (!iso2 || iso2.length !== 2) return '🏳️';
  const up = iso2.toUpperCase();
  const codePoints = [up.charCodeAt(0), up.charCodeAt(1)].map(c => 127397 + c);
  return String.fromCodePoint(codePoints[0], codePoints[1]);
}

export async function normalizeCountry(iso2, name) {
  const { iso2ToName, aliasesToName } = await load();
  if (iso2 && iso2ToName[iso2]) return iso2ToName[iso2];
  if (name && aliasesToName[name]) return aliasesToName[name];
  return name;
}

export async function countryToISO(name) {
  const { nameToIso, aliasesToName } = await load();
  if (nameToIso[name]) return nameToIso[name];
  if (aliasesToName[name] && nameToIso[aliasesToName[name]]) return nameToIso[aliasesToName[name]];
  return null;
}

export async function getFlagByISO(iso2) {
  const { flagMap } = await load();
  return flagMap[iso2] || isoToEmoji(iso2);
}

export async function getFlagByName(name) {
  const iso = await countryToISO(name);
  return iso ? getFlagByISO(iso) : '🏳️';
}
