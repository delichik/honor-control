// Disassemble a function from a PE at a given RVA
const fs = require('fs');
const Capstone = require('capstone-wasm');

function loadPe(path) {
  const buf = fs.readFileSync(path);
  const peOff = buf.readUInt32LE(0x3c);
  const coff = peOff + 4;
  const numSections = buf.readUInt16LE(coff + 2);
  const optSize = buf.readUInt16LE(coff + 16);
  const opt = coff + 20;
  const imageBase = buf.readBigUInt64LE(opt + 24);
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
  return { buf, imageBase, sections, rvaToOff(rva) {
    for (const s of sections)
      if (rva >= s.vaddr && rva < s.vaddr + Math.max(s.vsize, s.rsize))
        return s.raddr + (rva - s.vaddr);
    return null;
  } };
}

const [path, rvaHex, maxBytesArg] = process.argv.slice(2);
const pe = loadPe(path);
const rva = parseInt(rvaHex, 16);
const maxBytes = parseInt(maxBytesArg || '400', 10);
const off = pe.rvaToOff(rva);
if (off == null) { console.error('rva not mapped'); process.exit(1); }
const code = pe.buf.subarray(off, off + maxBytes);

const cs = new Capstone(Capstone.ARCH_X86, Capstone.MODE_64);
cs.option(Capstone.OPT_SKIPDATA, 1);
const insns = cs.disasm(code, pe.imageBase + BigInt(rva));
for (const ins of insns) {
  console.log(
    '0x' + ins.address.toString(16).padStart(8, '0'),
    ins.mnemonic + ' ' + (ins.op_str || '')
  );
}
