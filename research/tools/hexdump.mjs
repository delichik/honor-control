import fs from 'fs';
const path = process.argv[2];
const rvaHex = process.argv[3];
const len = parseInt(process.argv[4] || '96', 10);
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
const rva = parseInt(rvaHex, 16);
const sec = sections.find(s => rva >= s.vaddr && rva < s.vaddr + Math.max(s.vsize, s.rsize));
const off = sec.raddr + (rva - sec.vaddr);
console.log(`RVA 0x${rva.toString(16)} -> section ${sec.name}, file offset 0x${off.toString(16)}`);
for (let i = 0; i < len; i += 16) {
  const line = [];
  const ascii = [];
  for (let j = 0; j < 16 && i + j < len; j++) {
    const b = buf[off + i + j];
    line.push(b.toString(16).padStart(2, '0'));
    ascii.push(b >= 0x20 && b < 0x7f ? String.fromCharCode(b) : '.');
  }
  console.log(`0x${(rva + i).toString(16).padStart(6, '0')}  ${line.join(' ').padEnd(47)}  ${ascii.join('')}`);
}
