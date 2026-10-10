// Resolve what a data slot in .rdata points to (import name or pointer)
import fs from 'fs';
const path = process.argv[2];
const rvaArg = parseInt(process.argv[3], 16);
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
  sections.push({
    name: buf.toString('ascii', s, s + 8).replace(/\0.*$/, ''),
    vaddr: buf.readUInt32LE(s + 12),
    vsize: buf.readUInt32LE(s + 8),
    raddr: buf.readUInt32LE(s + 20),
    rsize: buf.readUInt32LE(s + 16),
  });
}
function r2o(rva) {
  for (const s of sections)
    if (rva >= s.vaddr && rva < s.vaddr + Math.max(s.vsize, s.rsize))
      return s.raddr + (rva - s.vaddr);
  return null;
}
function readCStr(off, wide = false) {
  let e = off;
  if (wide) { while (!(buf[e] === 0 && buf[e+1] === 0)) e += 2; return buf.toString('utf16le', off, e); }
  while (buf[e] !== 0) e++;
  return buf.toString('ascii', off, e);
}
// build import map: thunkRVA -> (dll, name)
const imports = new Map();
let d = r2o(importDirRva);
while (d) {
  const origThunkRva = buf.readUInt32LE(d);
  const nameRva = buf.readUInt32LE(d + 12);
  if (!origThunkRva || !nameRva) break;
  const dll = readCStr(r2o(nameRva));
  let t = origThunkRva;
  while (true) {
    const thunkRva = t;
    const val = buf.readBigUInt64LE(r2o(t));
    if (val === 0n) break;
    if (val & (1n << 63n)) {
      imports.set(thunkRva, `${dll}!ordinal_${val & 0xffffn}`);
    } else {
      const hn = r2o(Number(val));
      imports.set(thunkRva, `${dll}!${readCStr(hn + 2)}`);
    }
    t += 8;
  }
  d += 20;
}
const slot = imports.get(rvaArg);
console.log(`RVA 0x${rvaArg.toString(16)}: ${slot || '(not an import thunk, raw data: ' +
  (r2o(rvaArg) ? buf.readBigUInt64LE(r2o(rvaArg)).toString(16) : 'unmapped') + ')'}`);
