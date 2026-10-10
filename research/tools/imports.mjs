// List import DLLs and function names of a PE, and find call sites (xrefs) to selected imports
import fs from 'fs';

const path = process.argv[2];
const filter = (process.argv[3] || '').toLowerCase();
const mode = process.argv[4] || 'list'; // 'list' or 'xref'
const buf = fs.readFileSync(path);
const peOff = buf.readUInt32LE(0x3c);
const coff = peOff + 4;
const numSections = buf.readUInt16LE(coff + 2);
const optSize = buf.readUInt16LE(coff + 16);
const opt = coff + 20;
const dataDirOff = opt + 112;
const importDirRva = buf.readUInt32LE(dataDirOff + 8);
const secOff = opt + optSize;
const sections = [];
for (let i = 0; i < numSections; i++) {
  const s = secOff + i * 40;
  sections.push({ name: buf.toString('ascii', s, s + 8).replace(/\0.*$/, ''), vaddr: buf.readUInt32LE(s + 12), vsize: buf.readUInt32LE(s + 8), raddr: buf.readUInt32LE(s + 20), rsize: buf.readUInt32LE(s + 16) });
}
function r2o(rva) {
  for (const s of sections) if (rva >= s.vaddr && rva < s.vaddr + Math.max(s.vsize, s.rsize)) return s.raddr + (rva - s.vaddr);
  return null;
}
function cstr(off) { let e = off; while (buf[e] !== 0) e++; return buf.toString('ascii', off, e); }

// import thunks: map IAT slot RVA -> name (using FirstThunk array)
const bySlot = new Map();
let d = r2o(importDirRva);
while (d) {
  const oft = buf.readUInt32LE(d);
  const nameRva = buf.readUInt32LE(d + 12);
  const ft = buf.readUInt32LE(d + 16);
  if (!ft || !nameRva) break;
  const dll = cstr(r2o(nameRva));
  const useOft = oft || ft;
  let t = r2o(useOft), tRva = useOft, iRva = ft, iOff = r2o(ft);
  while (true) {
    const val = buf.readBigUInt64LE(t);
    if (val === 0n) break;
    let fn;
    if (val & (1n << 63n)) fn = `ord${val & 0xffffn}`;
    else fn = cstr(r2o(Number(val)) + 2);
    bySlot.set(iRva, `${dll}!${fn}`);   // key by IAT (FirstThunk) — what call [rip+X] targets
    t += 8; tRva += 8; iRva += 8; iOff += 8;
  }
  d += 20;
}
if (mode === 'list') {
  const dlls = new Map();
  for (const [slot, name] of bySlot) {
    const dll = name.split('!')[0];
    if (!dlls.has(dll)) dlls.set(dll, []);
    dlls.get(dll).push(name.split('!')[1]);
  }
  for (const [dll, fns] of dlls) {
    console.log(`\n[${dll}] (${fns.length})`);
    if (filter) fns.filter(f => f.toLowerCase().includes(filter)).forEach(f => console.log('  slot_rva? ' + f));
    else fns.slice(0, 400).forEach(f => console.log('  ' + f));
  }
  process.exit(0);
}
// xref mode: scan .text for call [rip+disp32] (ff 15 xx xx xx xx) or jmp (ff 25) targeting slots
const text = sections.find(s => s.name === '.text');
const selected = [];
for (const [slot, name] of bySlot) {
  if (name.toLowerCase().includes(filter)) selected.push({ slot, name });
}
if (!selected.length) { console.error('no import matches filter'); process.exit(1); }
console.log('Tracking imports:');
selected.forEach(s => console.log(`  slot 0x${s.slot.toString(16)} ${s.name}`));
const hits = [];
for (let off = text.raddr; off < text.raddr + text.rsize - 6; off++) {
  if (buf[off] === 0xff && (buf[off + 1] === 0x15 || buf[off + 1] === 0x25)) {
    const disp = buf.readInt32LE(off + 2);
    const insnLen = 6;
    const nextRva = (off - text.raddr) + text.vaddr + insnLen;
    const target = nextRva + disp;
    for (const s of selected) {
      if (target === s.slot) {
        hits.push({ callRva: (off - text.raddr) + text.vaddr, name: s.name, kind: buf[off + 1] === 0x15 ? 'call' : 'jmp' });
      }
    }
  }
}
console.log(`\n${hits.length} xrefs:`);
hits.forEach(h => console.log(`  ${h.kind} @ rva 0x${h.callRva.toString(16)} -> ${h.name}`));
