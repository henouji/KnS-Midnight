// Frida hooks for KnS native crypto (libgame.so). Emits plaintext<->ciphertext<->key
// triples so server/c2s_crypto.py can be validated and the open items in
// docs/crypto-findings.md can be closed.
//
//   frida -U -f <pkg> -l tools/frida/hook_crypto.js --no-pause
//   (pkg = com.com2us.imhero.normal.freefull.google.global.android.common)
//
// Writes JSONL to stdout; tee it:  frida ... | tee work/crypto-captures.jsonl
// Each line: {op, key, input_hex, output_hex}  (consumed by `c2s_crypto.py validate`).
'use strict';

const LIB = 'libgame.so';

// Exported symbol -> {args we care about}. Addresses in docs/crypto-findings.md are
// fallbacks if a name ever fails to resolve (Module.getExportByName uses .dynsym).
const TARGETS = [
  'C2S_Encryption_Encode',   // (out, mode, in, len)
  'C2S_Encryption_Decode',
  'rijndaelSetupEncrypt',    // (rk, key, bits)  <-- reveals the real AES key
  'rijndaelSetupDecrypt',
  'GetDecryptionKey',
  'CreateEncryptionKey',
  'EncryptData',
  'DecryptData',
];

function hexdump_ptr(p, n) {
  if (p.isNull() || n <= 0 || n > 0x4000) return '';
  try { return Memory.readByteArray(p, n); } catch (e) { return ''; }
}
function toHex(buf) {
  if (!buf) return '';
  const u = new Uint8Array(buf);
  let s = '';
  for (let i = 0; i < u.length; i++) s += ('0' + u[i].toString(16)).slice(-2);
  return s;
}
function emit(obj) { console.log(JSON.stringify(obj)); }

function base() { return Module.findBaseAddress(LIB); }

function hookByName(name) {
  let addr;
  try { addr = Module.getExportByName(LIB, name); }
  catch (e) { console.log('[!] cannot resolve ' + name); return; }

  Interceptor.attach(addr, {
    onEnter(args) {
      this.name = name;
      if (name === 'rijndaelSetupEncrypt' || name === 'rijndaelSetupDecrypt') {
        // (rk*, key*, int bits) -> dump the key; bits/8 bytes
        const bits = args[2].toInt32();
        emit({ note: 'AES_KEY', fn: name, bits: bits, key: toHex(hexdump_ptr(args[1], bits / 8)) });
      } else if (name === 'C2S_Encryption_Encode') {
        this.mode = args[1].toInt32();
        this.inp = args[2]; this.len = args[3].toInt32(); this.out = args[0];
        emit({ note: 'ENC_IN', mode: this.mode, len: this.len, input_hex: toHex(hexdump_ptr(this.inp, this.len)) });
      } else if (name === 'C2S_Encryption_Decode') {
        this.inp = args[2]; this.len = args[3].toInt32(); this.out = args[0];
        emit({ note: 'DEC_IN', len: this.len, input_hex: toHex(hexdump_ptr(this.inp, this.len)) });
      } else {
        this.a0 = args[0]; this.a1 = args[1]; this.a2 = args[2]; this.a3 = args[3];
      }
    },
    onLeave(retval) {
      if (this.name === 'C2S_Encryption_Encode') {
        emit({ note: 'ENC_OUT', mode: this.mode, ret: retval.toInt32(), output_hex: toHex(hexdump_ptr(this.out, this.len + 32)) });
      } else if (this.name === 'C2S_Encryption_Decode') {
        emit({ note: 'DEC_OUT', ret: retval.toInt32(), output_hex: toHex(hexdump_ptr(this.out, this.len)) });
      }
    },
  });
  console.log('[+] hooked ' + name + ' @ ' + addr);
}

function main() {
  const b = base();
  console.log('[*] ' + LIB + ' base = ' + b);
  TARGETS.forEach(hookByName);
  console.log('[*] hooks installed; interact with the game to capture.');
}

setTimeout(main, 1000); // let the lib load
