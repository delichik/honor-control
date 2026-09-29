// Robust context disassembler: disassemble [rva-win, rva+len] with SKIPDATA, full output
import fs from 'fs';
import { createRequire } from 'module';
const require = createRequire(import.meta.url);
const { Capstone, Const } = require('capstone-wasm');

const dll = process.argv[2];
const center = parseInt(process.argv[3], 16);
const before = parseInt(process.argv[4] || '0xa0', 16);
const after = parseInt(process.argv[5] || '0x20', 16);
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
function r2o(rva) {
  for (const s of sections) if (rva >= s.vaddr && rva < s.vaddr + Math.max(s.vsize, s.rsize)) return s.raddr + (rva - s.vaddr);
  return null;
}
function asciiAt(rva, max = 100) {
  const off = r2o(rva);
  if (off == null) return null;
  let e = off;
  while (e < off + max && e < buf.length && buf[e] >= 0x20 && buf[e] < 0x7f) e++;
  return e - off >= 4 ? buf.toString('ascii', off, e) : null;
}
await require('capstone-wasm').loadCapstone();
const cs = new Capstone(Const.CS_ARCH_X86, Const.CS_MODE_64);
cs.setOption(Const.CS_OPT_SKIPDATA, Const.CS_OPT_ON);
const start = center - before;
const off = r2o(start);
let insns = [];
try {
  insns = cs.disasm(buf.subarray(off, off + before + after), { address: text.vaddr + start });
} catch (e) {
  console.error('disasm failed, falling back to byte dump');
  process.exit(1);
}
for (const ins of insns) {
  let extra = '';
  const m = ins.opStr.match(/\[rip \+ (0x[0-9a-f]+)\]/);
  if (m) {
    const t = Number(ins.address) + ins.size + parseInt(m[1], 16);
    const s = asciiAt(t);
    if (s) extra = `   ; "${s}"`;
  }
  const mark = Number(ins.address) === center ? '>>' : '  ';
  console.log(mark, '0x' + ins.address.toString(16), ins.mnemonic, ins.opStr + extra);
}
