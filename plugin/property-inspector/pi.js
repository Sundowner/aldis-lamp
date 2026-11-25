/* Aldis Lamp — Property Inspector logic.
 * Copyright (C) 2026 Sundowner
 * SPDX-License-Identifier: GPL-3.0-only
 *
 * This program is free software: you can redistribute it and/or modify it under
 * the terms of the GNU General Public License version 3 as published by the
 * Free Software Foundation. It comes with no warranty at all; see the LICENSE
 * file in this repository or <https://www.gnu.org/licenses/>.
 *
 * Forked from the Key Inject PI (same hard-won protocol lessons):
 *   - Registers with the Stream Deck app directly over the documented
 *     "manual" WebSocket protocol (window.connectElgatoStreamDeckSocket).
 *   - PI commands (setSettings/getSettings) must carry the PI's OWN uuid
 *     as "context"; the action context is rejected by the app.
 *   - The PI may NOT send setTitle; the bridge relays it instead.
 *   - The injected actionInfo can be stale on reopen, so on socket open we
 *     explicitly request the current settings with getSettings.
 *   - localStorage in the PI CEF view is ephemeral: not used at all.
 *
 * Physical deck buttons are executed by the bridge itself; this page is the
 * settings editor, a test pad (dot/dash/on/off/stop) and the keybind
 * detector, over ws://127.0.0.1:12346.
 */
(function () {
  "use strict";

  var BRIDGE_URL = "ws://127.0.0.1:12346";

  function parseMaybe(v) {
    if (typeof v === "string") { try { return JSON.parse(v); } catch (e) { return null; } }
    return v || null;
  }

  // The app sometimes hands the settings over double-nested as
  // { "settings": { "settings": { ... } } }. Unwrap before reading, otherwise
  // every field looks empty and the next save writes that emptiness back over
  // a perfectly good button.
  function unwrap(s) {
    var n = 0;
    while (s && s.settings && !s.mode && n < 4) { s = s.settings; n++; }
    return s;
  }

  function hasContent(s) {
    return !!(s && (s.mode || s.text || s.lampKey || s.Title ||
                    s.cooldownMs || s.wpm !== undefined));
  }

  // ------------------------------------------------------------------ //
  // Connection to the Stream Deck app (settings live there)
  // ------------------------------------------------------------------ //
  var app = {
    sock: null, up: false, port: 0,
    uuid: "", registerEvent: "",
    action: "", context: "", device: "", controller: "Keypad"
  };

  window.connectElgatoStreamDeckSocket = function (port, pluginUuid, registerEvent, info, actionInfo) {
    app.port = port;
    app.uuid = String(pluginUuid || "");
    app.registerEvent = String(registerEvent || "registerPropertyInspector");

    var ai = parseMaybe(actionInfo);
    if (ai) {
      app.action = ai.action || app.action;
      app.context = ai.context || app.context;
      app.device = typeof ai.device === "string" ? ai.device : String(ai.device || "");
      app.controller = ai.controller || app.controller;
      var s = unwrap((ai.payload && ai.payload.settings) || ai.settings || null);
      if (hasContent(s)) applySettings(s, "actionInfo");
    }
    openAppSocket();
  };

  function openAppSocket() {
    if (!app.port) return;
    var sock = new WebSocket("ws://127.0.0.1:" + app.port);
    app.sock = sock;
    sock.onopen = function () {
      app.up = true;
      sock.send(JSON.stringify({ event: app.registerEvent, uuid: app.uuid }));
      note("app socket up ctx=" + app.context);
      // The injected actionInfo can be stale/empty on reopen; always ask the
      // app for the button's current settings.
      sock.send(JSON.stringify({
        event: "getSettings",
        action: app.action,
        context: app.uuid,   // NB: the PI's own uuid, not the action context
        device: app.device,
        payload: {}
      }));
    };
    sock.onmessage = function (ev) {
      var m = parseMaybe(ev.data);
      if (!m) return;
      var e = m.event || "";
      if (e) note("app event: " + e);
      if (e === "willAppear" || e === "didReceiveSettings") {
        var s = unwrap((m.payload && m.payload.settings) || null);
        if (hasContent(s)) applySettings(s, e);
      }
    };
    sock.onclose = function () {
      app.up = false;
      app.sock = null;
      setTimeout(openAppSocket, 800);
    };
    sock.onerror = function () { };
  }

  function appSend(obj) {
    if (app.up && app.sock && app.sock.readyState === 1) {
      app.sock.send(JSON.stringify(obj));
      return true;
    }
    return false;
  }

  // Persist settings to Stream Deck (per button).
  function persist() {
    currentSettings = {
      mode: modeEl.value,
      text: textEl.value.trim(),
      wpm: parseInt(wpmEl.value, 10) || 8,
      lampKey: lampKeyEl.value.trim() || "l",
      cooldownMs: cooldownMs(),
      wpmInTitle: wpmTitleEl.checked,
      Title: titleEl.value.trim()
    };
    var ok = appSend({
      event: "setSettings",
      action: app.action,
      context: app.uuid,   // the PI's own uuid (see header note)
      device: app.device,
      payload: { settings: currentSettings }
    });
    note("settings saved (" + (ok ? "sent to app" : "app link down") + ") " +
         currentSettings.mode + " \"" + currentSettings.text + "\" " +
         currentSettings.wpm + "wpm key=" + currentSettings.lampKey +
         " cooldown=" + (currentSettings.cooldownMs || "auto"));
    updateTitle();
    updateCooldownHint();
  }

  function updateTitle() {
    var t = (titleEl.value || "").trim() || autoTitle();
    if (!t) return;
    // Stream Deck rejects setTitle sent by a property inspector, so ask the
    // bridge (the plugin) to send it on our behalf over its app session.
    bridgeSend({
      type: "title",
      title: t,
      context: app.context,
      device: app.device,
      action: app.action
    });
  }

  // ------------------------------------------------------------------ //
  // Bridge connection (test pad, detect, status pill, breadcrumbs)
  // ------------------------------------------------------------------ //
  var bridgeSock = null;
  var bridgeUp = false;
  var pending = [];

  function bridgeSend(obj) {
    if (bridgeSock && bridgeSock.readyState === 1) {
      bridgeSock.send(JSON.stringify(obj));
    } else {
      pending.push(obj);
      if (pending.length > 20) pending.length = 0;
    }
  }

  function note(text) {
    try { bridgeSend({ type: "pi", note: String(text) }); } catch (e) { }
  }

  function connectBridge() {
    bridgeSock = new WebSocket(BRIDGE_URL);
    bridgeSock.onopen = function () {
      bridgeUp = true;
      setStatus(true);
      bridgeSend({ type: "hello" });
      var q = pending.slice(); pending.length = 0;
      q.forEach(function (m) { if (m.type !== "hello") bridgeSend(m); });
      note("PI opened, ctx=" + app.context);
    };
    bridgeSock.onclose = function () {
      bridgeUp = false;
      setStatus(false);
      setTimeout(connectBridge, 800);
    };
    bridgeSock.onmessage = function (ev) {
      var msg = parseMaybe(ev.data);
      if (!msg) return;
      if (msg.type === "detect") {
        if (msg.ok && msg.key) {
          lampKeyEl.value = msg.key;
          persist();
          detectHint.textContent = "Detected lights key: " + msg.key + " (from " + msg.where + ")";
          note("detect ok key=" + msg.key);
        } else {
          detectHint.textContent = "No lights key found in the Star Citizen config folders — set it manually.";
          note("detect failed");
        }
      } else if (msg.type === "ack" && msg.ok === false) {
        console.warn("bridge error:", msg.error);
      }
    };
    bridgeSock.onerror = function () { };
  }

  // ------------------------------------------------------------------ //
  // UI
  // ------------------------------------------------------------------ //
  var $ = function (id) { return document.getElementById(id); };
  var modeEl = $("mode"), textEl = $("text"), wpmEl = $("wpm"),
      lampKeyEl = $("lampKey"), titleEl = $("title"), cooldownEl = $("cooldown"),
      wpmTitleEl = $("wpmInTitle"),
      statusEl = $("status"), detectHint = $("detectHint"),
      cooldownHint = $("cooldownHint");

  var currentSettings = { mode: "phrase", text: "", wpm: 8, lampKey: "l",
                          cooldownMs: 0, wpmInTitle: true, Title: "" };

  // Seconds typed in the box -> whole milliseconds for the bridge.
  // Blank or 0 means "auto": as long as the send itself takes.
  function cooldownMs() {
    var sec = parseFloat(cooldownEl.value);
    if (!isFinite(sec) || sec <= 0) return 0;
    return Math.round(sec * 1000);
  }

  // Same PARIS arithmetic the bridge uses (unit = 1200/WPM), so the estimate
  // shown here is the length the ring will actually run: every lit element is
  // tap(25ms) + element + tap(25ms) + one unit gap, two more units between
  // characters, six for a word space.
  var MORSE = {
    A: ".-", B: "-...", C: "-.-.", D: "-..", E: ".", F: "..-.", G: "--.",
    H: "....", I: "..", J: ".---", K: "-.-", L: ".-..", M: "--", N: "-.",
    O: "---", P: ".--.", Q: "--.-", R: ".-.", S: "...", T: "-", U: "..-",
    V: "...-", W: ".--", X: "-..-", Y: "-.--", Z: "--..",
    "0": "-----", "1": ".----", "2": "..---", "3": "...--", "4": "....-",
    "5": ".....", "6": "-....", "7": "--...", "8": "---..", "9": "----.",
    ".": ".-.-.-", ",": "--..--", "?": "..--..", "'": ".----.", "!": "-.-.--",
    "/": "-..-.", "(": "-.--.", ")": "-.--.-", "&": ".-...", ":": "---...",
    "=": "-...-", "+": ".-.-.-", "-": "-....-", "\"": ".-..-."
  };

  function estimateMs() {
    var wpm = parseInt(wpmEl.value, 10) || 8;
    if (wpm < 5) wpm = 5;
    if (wpm > 25) wpm = 25;
    var unit = Math.floor(1200 / wpm), m = modeEl.value, total = 0;
    if (m === "dot") return unit + 50 + unit;
    if (m === "dash") return 3 * unit + 50 + unit;
    if (m !== "phrase") return 0;
    var text = (textEl.value || "").toUpperCase();
    for (var i = 0; i < text.length; i++) {
      var c = text.charAt(i);
      if (c === " ") { total += 6 * unit; continue; }
      var code = MORSE[c];
      if (!code) continue;
      for (var j = 0; j < code.length; j++)
        total += (code.charAt(j) === "." ? unit : 3 * unit) + 50 + unit;
      total += 2 * unit;
    }
    return total;
  }

  function updateCooldownHint() {
    var m = modeEl.value;
    if (m === "on" || m === "off") {
      cooldownHint.textContent = "Lamp on/off is a single tap, so there is nothing " +
        "to wait for and the ring does not run.";
      $("cooldownRow").classList.add("hidden");
      return;
    }
    $("cooldownRow").classList.remove("hidden");
    var est = estimateMs() / 1000;
    var forced = cooldownMs() / 1000;
    if (forced > 0) {
      cooldownHint.textContent = "Forced to " + forced + " s (this send takes about " +
        est.toFixed(1) + " s). Presses while the ring is still running are ignored.";
    } else {
      cooldownHint.textContent = "Auto: the key recharges for exactly as long as this " +
        "send takes — about " + est.toFixed(1) + " s at " + wpmEl.value + " WPM. " +
        "Presses while the ring is still running are ignored.";
    }
  }

  function setStatus(up) {
    statusEl.textContent = up ? "bridge: connected" : "bridge: offline…";
    statusEl.className = "pill " + (up ? "ok" : "off");
  }

  function applySettings(s, source) {
    if (!s) return;
    loadIntoUi(s);
    currentSettings = {
      mode: s.mode || "phrase",
      text: typeof s.text === "string" ? s.text : "",
      wpm: parseInt(s.wpm, 10) || 8,
      lampKey: typeof s.lampKey === "string" && s.lampKey ? s.lampKey : "l",
      cooldownMs: parseInt(s.cooldownMs, 10) || 0,
      wpmInTitle: s.wpmInTitle !== false,
      Title: typeof s.Title === "string" ? s.Title : ""
    };
    note("settings applied from " + (source || "?") + " mode=" + currentSettings.mode +
         " text=" + currentSettings.text + " wpm=" + currentSettings.wpm +
         " key=" + currentSettings.lampKey +
         " cooldown=" + (currentSettings.cooldownMs || "auto"));
    updateCooldownHint();
  }

  function loadIntoUi(s) {
    if (!s) return;
    if (s.mode) modeEl.value = s.mode;
    if (typeof s.text === "string") textEl.value = s.text;
    if (s.wpm) wpmEl.value = s.wpm;
    if (typeof s.lampKey === "string" && s.lampKey) lampKeyEl.value = s.lampKey;
    if (typeof s.Title === "string") titleEl.value = s.Title;
    if (typeof s.wpmInTitle !== "undefined") wpmTitleEl.checked = s.wpmInTitle !== false;
    var ms = parseInt(s.cooldownMs, 10) || 0;
    cooldownEl.value = ms > 0 ? (ms / 1000) : "";   // blank means auto
    syncVisibility();
  }

  function syncVisibility() {
    var m = modeEl.value;
    $("phraseRow").classList.toggle("hidden", m !== "phrase");
    $("wpmRow").classList.toggle("hidden", m === "on" || m === "off");
    $("titleRow").classList.toggle("hidden", m !== "phrase");
  }

  function autoTitle() {
    var m = modeEl.value;
    if (m === "dot") return "•";
    if (m === "dash") return "—";
    if (m === "on") return "ON";
    if (m === "off") return "OFF";
    var words = (textEl.value || "").trim().toUpperCase().split(/\s+/).filter(Boolean);
    if (!words.length) return "";
    // First two words, never cut mid-word: a key this small only has room for
    // a hint of a longer phrase, so say so with the ellipsis instead of
    // pretending the title is the whole message.
    var head = words.slice(0, 2).join(" ");
    if (head.length > 14) {
      var cut = head.slice(0, 14);
      var sp = cut.lastIndexOf(" ");
      if (sp > 4) cut = cut.slice(0, sp);
      head = cut + "…";
    } else if (words.length > 2) {
      head += " …";
    }
    var tail = wpmTitleEl.checked ? " " + (parseInt(wpmEl.value, 10) || 8) + "w" : "";
    return "≡ " + head + tail;
  }

  // ------------------------------------------------------------------ //
  // Signal library: pick a signal, read what it means, append it.
  //
  // Sourced where possible from B.R. 232 Signal Card (Admiralty, 1937),
  // U.S. FTP 223A Anti-Submarine and Escort of Convoy Instructions (1945)
  // and the British-US Escort of Convoy Instructions supplement (BUSCI).
  // Notes say so, and the last two groups are labelled for what they are:
  // modern ICS (post-1969) and folklore with no WW2 source found.
  // Only signals you can actually key with a lamp are listed — B.R. 232
  // procedure signs such as INT, NO, FC, FN, PT and the executive sign have
  // their own code forms that are not the letters, so keying "INT" would be
  // wrong and they are left out.
  // ------------------------------------------------------------------ //
  var SIGNALS = [
    // --- calling and answering ---
    { code: "CQ",     cat: "Call & answer", note: "General call to all stations (general Morse practice)." },
    { code: "DE",     cat: "Call & answer", note: "\u201cFrom\u201d \u2014 identifies the calling ship or station." },
    { code: "VE",     cat: "Call & answer", note: "RN general call sign (B.R. 232) \u2014 calls any RN station." },
    { code: "AA",     cat: "Call & answer", note: "RN call to a ship whose identity is unknown (B.R. 232). Not \u201cmedico\u201d." },
    { code: "R",      cat: "Call & answer", note: "Heard you / received (general Morse practice)." },
    { code: "RARAR",  cat: "Call & answer", note: "RN RECEIPT SIGN \u2014 your message has been received (B.R. 232)." },
    { code: "LINK",   cat: "Call & answer", note: "Lamp call sign: unknown aircraft I am calling by light (BUSCI 5054)." },
    { code: "PISTOL", cat: "Call & answer", note: "Lamp call sign: any aircraft of this group (BUSCI 5054)." },

    // --- message procedure ---
    { code: "BT",     cat: "Procedure (B.R. 232)", note: "LONG BREAK SIGN \u2014 divides address, text and signature." },
    { code: "IM",     cat: "Procedure (B.R. 232)", note: "RN REPEAT SIGN \u2014 send it again." },
    { code: "RPT",    cat: "Procedure (B.R. 232)", note: "\u201cRepeat\u201d (general Morse practice, not the RN sign)." },
    { code: "AS",     cat: "Procedure (B.R. 232)", note: "Prosign WAIT \u2014 hold the message, stand by." },
    { code: "&",      cat: "Procedure (B.R. 232)", note: "FURTHER MESSAGE SIGN \u2014 more traffic from you follows." },
    { code: "EEEEEE", cat: "Procedure (B.R. 232)", note: "ERASE SIGN \u2014 undo what you just sent (practice: E six times)." },
    { code: "ER",     cat: "Procedure (B.R. 232)", note: "B.R. 232 brackets a word that must be spelled letter by letter." },
    { code: "AR",     cat: "Procedure (B.R. 232)", note: "End of message (general Morse practice; RN used its ending sign)." },

    // --- distress, urgency, safety ---
    { code: "SOS",    cat: "Distress & safety", note: "Distress \u2014 the vessel itself is in grave and imminent danger." },
    { code: "SSS",    cat: "Distress & safety", note: "Sight / attacked by SUBMARINE \u2014 suffix after SOS (WW2; later SSSS)." },
    { code: "RRR",    cat: "Distress & safety", note: "Attacked by SURFACE RAIDER \u2014 suffix after SOS (later RRRR)." },
    { code: "QQQ",    cat: "Distress & safety", note: "Attacked by UNKNOWN raider, probably disguised \u2014 suffix after SOS." },
    { code: "AAA",    cat: "Distress & safety", note: "Attacked by AIRCRAFT \u2014 suffix after SOS (WW2; later AAAA)." },
    { code: "TTT",    cat: "Distress & safety", note: "SAFETY signal \u2014 danger to navigation: wreck, ice, weather. Not torpedo." },
    { code: "XXX",    cat: "Distress & safety", note: "URGENCY in Morse (the voice PAN-PAN) \u2014 short of distress." },

    // --- convoy vocabulary: radio voice code, not lamp ---
    { code: "TANTIVY",       cat: "Convoy voice code (radio)", note: "Submarine sighted. FTP 223A voice code for TBS/RT \u2014 not a lamp signal." },
    { code: "TOASTY",        cat: "Convoy voice code (radio)", note: "Torpedo track sighted (voice code)." },
    { code: "MARSHMALLOW",   cat: "Convoy voice code (radio)", note: "Submarine track sighted (voice code)." },
    { code: "SHILLY-SHALLY", cat: "Convoy voice code (radio)", note: "Emergency 45-degree turn to starboard (voice code)." },
    { code: "PLASTER-PARIS", cat: "Convoy voice code (radio)", note: "Emergency 45-degree turn to port (voice code)." },
    { code: "LOUSY",         cat: "Convoy voice code (radio)", note: "Suspicious vessel in sight (voice code)." },
    { code: "PRESTO",        cat: "Convoy voice code (radio)", note: "Submarine has dived (voice code)." },
    { code: "SCRAM",         cat: "Convoy voice code (radio)", note: "Return to your ship (voice code)." },

    // --- plain-language manœuvres ---
    { code: "HEAVE TO", cat: "Plain-language words", note: "Manoeuvring order spelled out in Morse \u2014 not a code signal." },
    { code: "STBY",     cat: "Plain-language words", note: "\u201cStand by\u201d \u2014 modern shorthand, not a WW2 naval signal." },

    // --- modern ICS: useful, but post-1969, NOT WW2 ---
    { code: "K3", cat: "Modern ICS (post-1969)", note: "\u201cI wish to communicate with you \u2014 3 = by signalling lamp\u201d." },
    { code: "G",  cat: "Modern ICS (post-1969)", note: "I require a pilot." },
    { code: "Z",  cat: "Modern ICS (post-1969)", note: "I require a tug (the claimed \u201cBZ\u201d has no source)." },
    { code: "O",  cat: "Modern ICS (post-1969)", note: "Man overboard." },
    { code: "L",  cat: "Modern ICS (post-1969)", note: "Stop your vessel instantly." },
    { code: "W",  cat: "Modern ICS (post-1969)", note: "Medical assistance required \u2014 what \u201cAA = medico\u201d claims to be." },
    { code: "Y",  cat: "Modern ICS (post-1969)", note: "Your vessel is dragging anchor." },

    // --- things that are widely claimed but unsourced ---
    { code: "CS", cat: "Unverified \u2014 no WW2 source", note: "Claimed \u201cI wish to communicate with you\u201d; modern ICS uses K3." },
    { code: "EI", cat: "Unverified \u2014 no WW2 source", note: "Claimed \u201cemergency turn\u201d; the sourced equivalent is SHILLY-SHALLY." },
    { code: "RR", cat: "Unverified \u2014 no WW2 source", note: "Claimed \u201call received and understood\u201d; RN card uses RARAR." },
    { code: "AB", cat: "Unverified \u2014 no WW2 source", note: "Claimed \u201cI require a pilot\u201d; modern ICS uses G." },
    { code: "EE", cat: "Unverified \u2014 no WW2 source", note: "Claimed emergency signal \u2014 nothing found in any WW2 document." },
    { code: "RD", cat: "Unverified \u2014 no WW2 source", note: "In B.R. 232 this is the RED PENDANT, not a phrase to send." }
  ];

  var signalsEl = $("signals"), signalNote = $("signalNote");
  var defaultNote = signalNote.textContent;

  function noteFor(code) {
    for (var i = 0; i < SIGNALS.length; i++)
      if (SIGNALS[i].code === code) return SIGNALS[i].note;
    return "";
  }

  function showNote(code) {
    var n = noteFor(code);
    if (!n) return false;
    signalNote.textContent = code + " \u2014 " + n;
    return true;
  }

  function buildLibrary() {
    var order = [], groups = {};
    SIGNALS.forEach(function (s) {
      if (!groups[s.cat]) { groups[s.cat] = []; order.push(s.cat); }
      groups[s.cat].push(s);
    });
    while (signalsEl.firstChild) signalsEl.removeChild(signalsEl.firstChild);
    var blank = document.createElement("option");
    blank.value = "";
    blank.textContent = "\u2014 " + SIGNALS.length + " signals, pick one \u2014";
    signalsEl.appendChild(blank);
    order.forEach(function (cat) {
      var og = document.createElement("optgroup");
      og.label = cat;
      groups[cat].forEach(function (s) {
        var o = document.createElement("option");
        o.value = s.code;
        o.textContent = s.code;
        og.appendChild(o);
      });
      signalsEl.appendChild(og);
    });
  }

  // Picking a signal from the list appends it, saves and retitles the key in
  // one go — the phrase field, the stored settings and the key title all move
  // together, exactly as if you had typed it. The button only repeats the
  // current selection.
  function appendSignal(code) {
    if (!code) {
      signalNote.textContent = "Pick a signal from the list first.";
      return;
    }
    var cur = textEl.value.trim();
    textEl.value = cur.length ? cur + " " + code : code;
    persist();
    showNote(code);
  }

  signalsEl.addEventListener("change", function () {
    if (signalsEl.value) appendSignal(signalsEl.value);
    else signalNote.textContent = defaultNote;
  });

  $("addSignal").addEventListener("click", function () {
    appendSignal(signalsEl.value);
  });

  $("clearText").addEventListener("click", function () {
    textEl.value = "";
    persist();
  });

  $("detect").addEventListener("click", function () {
    detectHint.textContent = "Scanning Star Citizen config folders…";
    bridgeSend({ type: "detect" });
  });

  // Test pad: sends go through the bridge immediately.
  $("testDot").addEventListener("pointerdown", function (ev) {
    ev.preventDefault();
    bridgeSend({ type: "element", event: "dot", wpm: parseInt(wpmEl.value, 10) || 8,
                 key: lampKeyEl.value.trim() || "l" });
  });
  $("testDash").addEventListener("pointerdown", function (ev) {
    ev.preventDefault();
    bridgeSend({ type: "element", event: "dash", wpm: parseInt(wpmEl.value, 10) || 8,
                 key: lampKeyEl.value.trim() || "l" });
  });
  $("testOn").addEventListener("click", function () {
    bridgeSend({ type: "toggle", event: "on", key: lampKeyEl.value.trim() || "l" });
  });
  $("testOff").addEventListener("click", function () {
    bridgeSend({ type: "toggle", event: "off", key: lampKeyEl.value.trim() || "l" });
  });
  $("stopBtn").addEventListener("click", function () {
    bridgeSend({ type: "stop" });
  });

  // Play the recharge ring on the real key, no keystrokes: lets you judge the
  // timing of a phrase without firing the lamp.
  $("cooldownTest").addEventListener("click", function () {
    var ms = cooldownMs() > 0 ? cooldownMs() : estimateMs();
    if (ms <= 0) ms = 2000;
    bridgeSend({ type: "cooldown", context: app.context, device: app.device,
                 action: app.action, cooldownMs: ms });
    note("preview ring " + ms + "ms");
  });

  [modeEl, textEl, wpmEl, lampKeyEl, titleEl, cooldownEl, wpmTitleEl].forEach(function (el) {
    el.addEventListener("change", persist);
  });
  textEl.addEventListener("input", function () { updateTitle(); updateCooldownHint(); explainPhrase(); });
  cooldownEl.addEventListener("input", updateCooldownHint);
  wpmEl.addEventListener("change", updateCooldownHint);
  modeEl.addEventListener("change", function () { syncVisibility(); updateCooldownHint(); });

  function explainPhrase() {
    var t = (textEl.value || "").trim().toUpperCase();
    if (!t) return;
    if (showNote(t)) return;
    showNote(t.split(/\s+/)[0]);
  }

  buildLibrary();
  syncVisibility();
  updateCooldownHint();
  connectBridge();
})();
