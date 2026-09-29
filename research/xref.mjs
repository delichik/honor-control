// Full-disassembly xref: find all references (rip-relative mem ops, direct calls/jmps) to given RVAs,
// including thunk chains (jmp [rip+disp] stubs that forward to the target).
import fs from 'fs';
import { createRequire } from 'module';
const require = createRequire(import.meta.url);
const { Capstone, Const } = require('capstone-wasm');

const path = process.argv[2];
const targetRva = parseInt(process.argv[3], 16);
const ctxBefore = parseInt(process.argv[4] || '160', 10);
const ctxAfter = parseInt(process.argv[5] || '40', 10);

const buf = fs.readFileSync(path);
const peOff = buf.readUInt32LE(0x3c);
const coff = peOff + 4;
const numSections = buf.readUInt16LE(coff + 2);
const optSize = buf.readUInt16LE(coff + 16);
const opt = coff + 20;
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
const text = sections.find(s => s.name === '.text');
const code = buf.subarray(text.raddr, text.raddr + text.rsize);

await (require('capstone-wasm').loadCapstone());
const cs = new Capstone(Const.CS_ARCH_X86, Const.CS_MODE_64);
const insns = cs.disasm(code, { address: text.vaddr });

// Pass 1: find thunk stubs: jmp [rip+d] or mov r,[rip+d] where resolved == targetRva
const thunks = new Map(); // thunkRva -> true (code stubs that reference target)
const refs = []; // {rva, insn}
function resolveRip(ins) {
  const m = ins.opStr.match(/\[rip \+ (0x[0-9a-f]+)\]|\[rip - (0x[0-9a-f]+)\]/);
  if (!m) return null;
  const disp = m[1] ? parseInt(m[1], 16) : -parseInt(m[2], 16);
  return ins.address + BigInt(ins.size) + BigInt(disp);
}
for (const ins of insns) {
  const r = resolveRip(ins);
  if (r !== null && r === BigInt(targetRva)) refs.push({ rva: Number(ins.address), ins, kind: 'direct-ref' });
  // direct call/jmp rel32 to target
  if ((ins.mnemonic === 'call' || ins.mnemonic === 'jmp') && !ins.opStr.includes('rip') && ins.opStr.startsWith('0x')) {
    const t = parseInt(ins.opStr, 16);
    if (t === targetRva) refs.push({ rva: Number(ins.address), ins, kind: 'direct-' + ins.mnemonic });
  }
}
// Pass 2: stubs that jmp/call into a found thunk (one level indirection): treat jmp [rip] stubs themselves as targets
// find code locations that direct-jmp to any rip-relative ref site of kind direct-ref with mnemonic jmp (classic thunk)
const thunkSites = refs.filter(r => r.ins.mnemonic === 'jmp' && r.kind === 'direct-ref').map(r => r.rva);
for (const ins of insns) {
  if ((ins.mnemonic === 'call' || ins.mnemonic === 'jmp') && !ins.opStr.includes('rip') && ins.opStr.startsWith('0x')) {
    const t = parseInt(ins.opStr, 16);
    if (thunkSites.includes(t)) refs.push({ rva: Number(ins.address), ins, kind: 'via-thunk@' + t.toString(16) });
  }
}
console.log(`refs to 0x${targetRva.toString(16)}: ${refs.length}`);
const seen = new Set();
for (const r of refs) {
  if (seen.has(r.rva)) continue;
  seen.add(r.rva);
  console.log(`\n===== ref @ rva 0x${r.rva.toString(16)} (${r.kind}) =====`);
  // context disasm
  const start = Math.max(0, r.rva - ctxBefore - text.vaddr);
  const ctxIns = cs.disasm(code.subarray(start, start + ctxBefore + ctxAfter + 16), { address: text.vaddr + start });
  for (const ci of ctxIns) {
    const mark = Number(ci.address) === r.rva ? '>>' : '  ';
    console.log(mark, '0x' + ci.address.toString(16), ci.mnemonic, ci.opStr);
  }
}
