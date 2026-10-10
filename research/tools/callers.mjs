// Build reverse call graph: for a target RVA, find all direct call rel32 sites, recursively up N levels
import fs from 'fs';
const dll = process.argv[2];
const targets = process.argv.slice(3).map(x => parseInt(x, 16));
const depth = parseInt(process.env.DEPTH || '2', 10);
const buf = fs.readFileSync(dll);
const peOff = buf.readUInt32LE(0x3c);
const coff = peOff + 4;
const numSections = buf.readUInt16LE(coff + 2);
const optSize = buf.readUInt16LE(coff + 16);
const opt = coff + 20;
const secOff = opt + optSize;
const sections = [];
let text;
for (let i = 0; i < numSections; i++) {
  const s = secOff + i * 40;
  const sec = { name: buf.toString('ascii', s, s + 8).replace(/\0.*$/, ''), vaddr: buf.readUInt32LE(s + 12), vsize: buf.readUInt32LE(s + 8), raddr: buf.readUInt32LE(s + 20), rsize: buf.readUInt32LE(s + 16) };
  sections.push(sec);
  if (sec.name === '.text') text = sec;
}
function r2o(rva) { for (const s of sections) if (rva >= s.vaddr && rva < s.vaddr + Math.max(s.vsize, s.rsize)) return s.raddr + (rva - s.vaddr); return null; }
// collect all call sites once
const calls = []; // {site, target}
for (let off = text.raddr; off < text.raddr + text.rsize - 5; off++) {
  if (buf[off] === 0xe8) {
    const rel = buf.readInt32LE(off + 1);
    const site = (off - text.raddr) + text.vaddr;
    calls.push({ site, target: site + 5 + rel });
  }
}
function callersOf(t) { return calls.filter(c => c.target === t).map(c => c.site); }
function walk(t, level, prefix) {
  const cs = callersOf(t);
  for (const c of cs) {
    console.log(`${prefix}called from 0x${c.toString(16)}`);
    if (level < depth) walk(c, level + 1, prefix + '  ');
  }
}
for (const t of targets) {
  console.log(`=== target 0x${t.toString(16)} ===`);
  walk(t, 1, '  ');
}
