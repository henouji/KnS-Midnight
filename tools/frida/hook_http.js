// Frida hooks for the Java HTTP layer + the native packet builder, to capture the
// cleartext request (incl. the `aPi` command string) and the expected `Result` shape.
//
//   frida -U -f <pkg> -l tools/frida/hook_http.js --no-pause
//
// Java side: com.com2us.wrapper.network.CWrapperHttp / CHttpManager.
// Native side: C2S_Network_EncodePacket (pre-encryption body) and
// C2S_Network_DecodePacket (parsed response) in libgame.so.
'use strict';

const LIB = 'libgame.so';

function hookNativePackets() {
  ['C2S_Network_EncodePacket', 'C2S_Network_DecodePacket'].forEach(function (name) {
    let addr;
    try { addr = Module.getExportByName(LIB, name); } catch (e) { console.log('[!] ' + name + ' unresolved'); return; }
    Interceptor.attach(addr, {
      onEnter(args) {
        this.name = name;
        // Heuristic: first few string args often include url/aPi/params. Dump r0..r3
        // as C-strings when they look printable.
        for (let i = 0; i < 4; i++) {
          try {
            const s = args[i].readUtf8String();
            if (s && s.length > 1 && s.length < 512) console.log('[' + name + '] arg' + i + ' = ' + s);
          } catch (e) {}
        }
      },
    });
    console.log('[+] hooked ' + name + ' @ ' + addr);
  });
}

function hookJava() {
  if (!Java.available) { console.log('[!] Java not available'); return; }
  Java.perform(function () {
    try {
      const Http = Java.use('com.com2us.wrapper.network.CWrapperHttp');
      // Log every declared method invocation signature once, then hook common ones.
      Http.class.getDeclaredMethods().forEach(function (m) { console.log('[CWrapperHttp] ' + m.toString()); });
    } catch (e) { console.log('[!] CWrapperHttp hook: ' + e); }

    // URLConnection sink: catches the final outbound URL + body regardless of wrapper.
    try {
      const URL = Java.use('java.net.URL');
      URL.openConnection.overload().implementation = function () {
        console.log('[URL.openConnection] ' + this.toString());
        return this.openConnection();
      };
    } catch (e) { console.log('[!] URL hook: ' + e); }
  });
}

setTimeout(function () {
  console.log('[*] base = ' + Module.findBaseAddress(LIB));
  hookNativePackets();
  hookJava();
}, 1000);
