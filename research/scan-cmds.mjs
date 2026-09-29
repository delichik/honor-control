// For each call site RVA, disassemble the preceding window and report command-word setup
import fs from 'fs';
import { createRequire } from 'module';
const require = createRequire(import.meta.url);
const { Capstone, Const } = require('capstone-wasm');

const dll = process.argv[2];
const sites = process.argv.slice(3).map(x => parseInt(x, 16));
const buf = fs.readFileSync(dll);
const peOff = buf.readUInt32LE(0x3c);
const coff = peOff + 4;
const numSections = buf.readUInt16LE(coff + 2);
const optSize = buf.readUInt16LE(coff + 16);
const opt = coff + 20;
const secOff = opt + optSize;
let text;
const sections = [];
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
await require('capstone-wasm').loadCapstone();
const cs = new Capstone(Const.CS_ARCH_X86, Const.CS_MODE_64);
cs.setOption(Const.CS_OPT_SKIPDATA, Const.CS_OPT_ON);

for (const site of sites) {
  const winStart = site - 0x110;
  const off = r2o(winStart);
  const code = buf.subarray(off, off + 0x118);
  let insns = [];
  try { insns = cs.disasm(code, { address: text.vaddr + winStart }); } catch (e) { console.log(`call 0x${site.toString(16)}: DISASM FAIL`); continue; }
  const cmds = [];
  let inLen = null, outPtr = null;
  for (const ins of insns) {
    const t = `${ins.mnemonic} ${ins.opStr}`;
    const m = t.match(/mov word ptr \[rsp \+ (0x[0-9a-f]+)\], (0x[0-9a-f]+)/);
    if (m) cmds.push(`[${m[1]}]=${m[2]}`);
    const mL = t.match(/mov (r8d|ecx|edx), (0x[0-9a-f]+)/);
    if (mL && parseInt(mL[2], 16) > 3 && parseInt(mL[2], 16) < 0x200) inLen = `${mL[1]}=${mL[2]}`;
    const mOut = t.match(/mov dword ptr \[rsp \+ (0x[0-9a-f]+)\], (0x[0-9a-f]+)/);
    if (mOut) cmds.push(`dword[${mOut[1]}]=${mOut[2]}`);
  }
  console.log(`call 0x${site.toString(16)}:  ${cmds.join('  ')}`);
}
