// Checks every package in the web app's lockfile against the licence allow list
// (standards, domain 5). Reads the lockfile only, so nothing is installed or run.
//
//   node scripts/check-npm-licences.mjs web/package-lock.json

import { readFileSync } from 'node:fs';

// what ships in the page (A32)
const shipped = new Set(['MIT', 'MIT-0', 'Apache-2.0', 'BSD-2-Clause', 'BSD-3-Clause', 'ISC', '0BSD']);

// also allowed in tools that only build or test the page (A32)
const buildOnly = new Set(['MPL-2.0', 'BlueOak-1.0.0', 'CC0-1.0']);

// an SPDX expression of ORs passes if any term does; ANDs need every term
function passes(expression, allowed) {
  const terms = expression.replace(/[()]/g, '').split(/\s+OR\s+/);
  return terms.some((term) => term.split(/\s+AND\s+/).every((licence) => allowed.has(licence)));
}

const lockfile = process.argv[2] ?? 'web/package-lock.json';
const { packages } = JSON.parse(readFileSync(lockfile, 'utf8'));
const refused = [];

for (const [path, pkg] of Object.entries(packages)) {
  if (path === '') continue; // the app itself

  const devOnly = pkg.dev === true || pkg.devOptional === true;
  const allowed = devOnly ? new Set([...shipped, ...buildOnly]) : shipped;
  if (!pkg.license || !passes(pkg.license, allowed)) {
    const name = path.replace(/^.*node_modules\//, '');
    refused.push(`${name}@${pkg.version}: ${pkg.license ?? 'no licence'}${devOnly ? ' (dev)' : ''}`);
  }
}

if (refused.length > 0) {
  console.error(`Licences outside the allow list in ${lockfile}:`);
  refused.forEach((line) => console.error(`  ${line}`));
  process.exit(1);
}

console.log(`${Object.keys(packages).length - 1} packages, every licence allowed.`);
