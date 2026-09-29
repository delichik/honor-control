// Parse PE exports of a DLL (x64)
const fs = require('fs');

function parseExports(path) {
  const buf = fs.readFileSync(path);
  const peOff = buf.readUInt32LE(0x3c);
  const sig = buf.toString('ascii', peOff, peOff + 4);
  if (sig !== 'PE\0\0') throw new Error('not PE');
  const coff = peOff + 4;
  const machine = buf.readUInt16LE(coff);
  const numSections = buf.readUInt16LE(coff + 2);
  const optSize = buf.readUInt16LE(coff + 16);
  const opt = coff + 20;
  const magic = buf.readUInt16LE(opt);
  const isPE32Plus = magic === 0x20b;
  const dataDirOff = opt + (isPE32Plus ? 112 : 96);
  const exportDirRva = buf.readUInt32LE(dataDirOff + 0);

  const secOff = opt + optSize;
  const sections = [];
  for (let i = 0; i < numSections; i++) {
    const s = secOff + i * 40;
    const name = buf.toString('ascii', s, s + 8).replace(/\0.*$/, '');
    const vsize = buf.readUInt32LE(s + 8);
    const vaddr = buf.readUInt32LE(s + 12);
    const rsize = buf.readUInt32LE(s + 16);
    const raddr = buf.readUInt32LE(s + 20);
    sections.push({ name, vaddr, vsize, raddr, rsize });
  }

  function rvaToOff(rva) {
    if (rva == null) return null;
    for (const s of sections) {
      if (rva >= s.vaddr && rva < s.vaddr + Math.max(s.vsize, s.rsize)) {
        return s.raddr + (rva - s.vaddr);
      }
    }
    return null;
  }

  const result = { path, machineHex: machine.toString(16), sections, exports: [] };
  if (!exportDirRva) return result;
  const eo = rvaToOff(exportDirRva);
  const nNames = buf.readUInt32LE(eo + 24);
  const addrNamesRva = buf.readUInt32LE(eo + 32);
  const addrFuncsRva = buf.readUInt32LE(eo + 28);
  const ordBase = buf.readUInt32LE(eo + 16);
  const namesOff = rvaToOff(addrNamesRva);
  const funcsOff = rvaToOff(addrFuncsRva);
  const ordsRva = buf.readUInt32LE(eo + 36);
  const ordsOff = rvaToOff(ordsRva);
  for (let i = 0; i < nNames; i++) {
    const nameRva = buf.readUInt32LE(namesOff + i * 4);
    const ord = buf.readUInt16LE(ordsOff + i * 2);
    const nOff = rvaToOff(nameRva);
    if (nOff == null) continue;
    let e = nOff;
    while (e < buf.length && buf[e] !== 0) e++;
    const name = buf.toString('ascii', nOff, e);
    const funcRva = buf.readUInt32LE(funcsOff + ord * 4);
    result.exports.push({ name, rva: funcRva, ordinal: ordBase + ord });
  }
  return result;
}

const target = process.argv[2];
const filter = process.argv[3] || '';
const info = parseExports(target);
console.log('machine:', info.machineHex);
if (filter) info.exports = info.exports.filter(e => e.name.includes(filter));
for (const ex of info.exports) console.log(ex.rva.toString(16).padStart(8, '0'), ex.name);
console.log('total exports:', parseExports(target).exports.length);
