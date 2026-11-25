// Aldis Lamp — Stream Deck plugin bridge for Star Citizen ship lights.
// Copyright (C) 2026 Sundowner
// SPDX-License-Identifier: GPL-3.0-only
//
// This program is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License version 3 as published by the
// Free Software Foundation. It comes with no warranty at all; see the LICENSE
// file in this repository or <https://www.gnu.org/licenses/>.
//
// Forked from the Key Inject bridge (same proven plumbing):
//   - WebSocket server for the Property Inspector on 127.0.0.1:12346
//   - Stream Deck v2 native registration (registerPlugin over the app socket)
//   - physical keyDown handled natively (buttons work with no PI open)
//   - SendInput keystroke injection
//
// The game key is a TOGGLE, so every lit Morse element is a pair of taps:
// tap(on) -> element length -> tap(off) -> gap. After each element the lamp
// is back in whatever state it was before.
//
// Compiled with .NET Framework 4 csc.exe => C# 5 only.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace AldisLamp
{
    internal static class Program
    {
        private const int PiPort = 12346;

        private static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                Logger.Write("Unhandled error: " + e.ExceptionObject);
            };

            string port = GetArg(args, "-port");
            string pluginUuid = GetArg(args, "-pluginUUID");
            string registerEvent = GetArg(args, "-registerEvent");
            if (registerEvent == null || registerEvent.Length == 0) registerEvent = "registerPlugin";

            LampExecutor executor = new LampExecutor();

            try
            {
                WebSocketServer server = new WebSocketServer(PiPort, executor);
                server.Start();
                Logger.Write("Aldis Lamp bridge started; PI socket on 127.0.0.1:" + PiPort);
            }
            catch (Exception ex)
            {
                Logger.Write("Could not listen on port " + PiPort + ": " + ex.Message);
            }

            if (port == null)
            {
                // Standalone (launched by hand): keep the PI/test socket up.
                Logger.Write("No -port argument: standalone mode (no Stream Deck session).");
                while (true) Thread.Sleep(60000);
            }

            StreamDeckClient.Run("ws://127.0.0.1:" + port, pluginUuid, registerEvent, executor);
            while (true) Thread.Sleep(60000);
        }

        private static string GetArg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }

    // ---------------------------------------------------------------------
    // Simple file logger: %LOCALAPPDATA%\AldisLamp\bridge.log
    // ---------------------------------------------------------------------
    internal static class Logger
    {
        private static readonly object gate = new object();
        private static readonly string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AldisLamp", "bridge.log");

        public static void Write(string message)
        {
            try
            {
                lock (gate)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.AppendAllText(path,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + message + "\n");
                }
            }
            catch { }
            try { Console.WriteLine(message); } catch { }
        }
    }

    // ---------------------------------------------------------------------
    // Key resolution: token text -> Win32 virtual-key code + extended flag
    // ---------------------------------------------------------------------
    internal static class KeyMap
    {
        private static readonly Dictionary<string, int> Named = BuildNamed();
        private static readonly HashSet<int> Extended = BuildExtended();

        private static Dictionary<string, int> BuildNamed()
        {
            Dictionary<string, int> d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            d["esc"] = 0x1B; d["escape"] = 0x1B;
            d["tab"] = 0x09;
            d["enter"] = 0x0D; d["return"] = 0x0D;
            d["space"] = 0x20; d["spacebar"] = 0x20;
            d["backspace"] = 0x08;
            d["insert"] = 0x2D; d["delete"] = 0x2E; d["del"] = 0x2E;
            d["home"] = 0x24; d["end"] = 0x23;
            d["pageup"] = 0x21; d["prior"] = 0x21;
            d["pagedown"] = 0x22; d["next"] = 0x22;
            d["up"] = 0x26; d["down"] = 0x28; d["left"] = 0x25; d["right"] = 0x27;
            d["printscreen"] = 0x2C; d["prtsc"] = 0x2C;
            d["pause"] = 0x13; d["capslock"] = 0x14;
            d["ctrl"] = 0x11; d["control"] = 0x11;
            d["rctrl"] = 0xA3; d["alt"] = 0x12; d["menu"] = 0x12;
            d["ralt"] = 0xB5; d["shift"] = 0x10;
            d["rshift"] = 0xA1; d["win"] = 0x5B; d["lwin"] = 0x5B;
            d["rwin"] = 0x5C;
            d["numlock"] = 0x90; d["scrolllock"] = 0x91;
            for (int n = 1; n <= 12; n++) d["f" + n] = 0x6F + n;
            // Numpad (explicit names; a bare digit is always the main-row key)
            d["num0"] = 0x60; d["num1"] = 0x61; d["num2"] = 0x62; d["num3"] = 0x63;
            d["num4"] = 0x64; d["num5"] = 0x65; d["num6"] = 0x66; d["num7"] = 0x67;
            d["num8"] = 0x68; d["num9"] = 0x69;
            d["nummultiply"] = 0x6A; d["numadd"] = 0x6B; d["numsub"] = 0x6D;
            d["numdec"] = 0x6E;
            d["luent"] = 0x6C; // numpad Enter (extended)
            return d;
        }

        private static HashSet<int> BuildExtended()
        {
            // Virtual-key codes that require KEYEVENTF_EXTENDEDKEY.
            return new HashSet<int>(new int[]
            {
                0x2D, 0x2E, 0x24, 0x23, 0x21, 0x22,   // ins del home end pgup pgdn
                0x26, 0x28, 0x25, 0x27,               // arrows
                0x2C, 0x13,                           // prtsc, pause
                0xA3, 0xB5,                           // rctrl, ralt
                0x5C,                                 // rwin
                0x6C                                  // numpad enter
            });
        }

        public static bool TryResolve(string token, out ushort vk, out bool extended)
        {
            vk = 0; extended = false;
            token = token.Trim();
            if (token.Length == 0) return false;

            int code;
            if (Named.TryGetValue(token, out code))
            {
                vk = (ushort)code;
                extended = Extended.Contains(code);
                return true;
            }
            if (token.Length == 1)
            {
                char c = char.ToUpperInvariant(token[0]);
                if (c >= 'A' && c <= 'Z') { vk = (ushort)c; return true; }
                if (c >= '0' && c <= '9') { vk = (ushort)c; return true; }
                switch (c)
                {
                    case '-': vk = 0xBD; return true;
                    case '=': vk = 0xBB; return true;
                    case '[': vk = 0xDB; return true;
                    case ']': vk = 0xDD; return true;
                    case '\\': vk = 0xDC; return true;
                    case ';': vk = 0xBA; return true;
                    case '\'': vk = 0xDE; return true;
                    case ',': vk = 0xBC; return true;
                    case '.': vk = 0xBE; return true;
                    case '/': vk = 0xBF; return true;
                    case '`': vk = 0xC0; return true;
                }
            }
            return false;
        }
    }

    // ---------------------------------------------------------------------
    // SendInput plumbing
    // ---------------------------------------------------------------------
    // Union with the INPUT struct: sized/padded so sizeof matches the real
    // INPUT union (32 bytes on x64) even though we only fill the ki member.
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    internal struct INPUT_UNION
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct INPUT
    {
        public uint type;
        public INPUT_UNION U;
    }

    internal static class Native
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        public static extern uint MapVirtualKey(uint uCode, uint uMapType);
    }

    // ---------------------------------------------------------------------
    // Lamp executor: one serial job queue; Morse timing per PARIS standard.
    //
    // The game key toggles the lamp, so a lit element is always an EVEN pair
    // of taps: tap(on) sleep(element) tap(off) sleep(gap). The lamp is back
    // in its previous state after every element.
    //
    // Cancellation: every job captures the executor's generation counter.
    // A new press (or Stop) bumps the counter; running jobs see the mismatch
    // between elements and stop cleanly. Queued lamp jobs are dropped.
    // ---------------------------------------------------------------------
    internal sealed class LampExecutor
    {
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

        private const int LAMP_UNKNOWN = 0;
        private const int LAMP_ON = 1;
        private const int LAMP_OFF = 2;

        private sealed class LampJob
        {
            public string Kind;   // phrase | dot | dash | on | off | stop
            public string Text;
            public int Wpm;
            public string Key;
            public long Gen;      // generation captured at enqueue time
            public string Context;// which deck key asked for it ("" = test pad)
            public string Device;
            public int CooldownMs;// 0 = use the send's own computed length
        }

        private readonly Queue<LampJob> queue = new Queue<LampJob>();
        private readonly object queueGate = new object();
        private Thread worker;
        private long generation;          // read/written under queueGate; volatile via Interlocked reads
        private int lampState = LAMP_UNKNOWN;
        private string lastKey = "l";     // for stop: which key to tap to extinguish

        private static readonly Dictionary<char, string> Morse = BuildMorse();

        private static Dictionary<char, string> BuildMorse()
        {
            Dictionary<char, string> m = new Dictionary<char, string>();
            m['A'] = ".-"; m['B'] = "-..."; m['C'] = "-.-."; m['D'] = "-.."; m['E'] = ".";
            m['F'] = "..-."; m['G'] = "--."; m['H'] = "...."; m['I'] = ".."; m['J'] = ".---";
            m['K'] = "-.-"; m['L'] = ".-.."; m['M'] = "--"; m['N'] = "-."; m['O'] = "---";
            m['P'] = ".--."; m['Q'] = "--.-"; m['R'] = ".-."; m['S'] = "..."; m['T'] = "-";
            m['U'] = "..-"; m['V'] = "...-"; m['W'] = ".--"; m['X'] = "-..-"; m['Y'] = "-.--";
            m['Z'] = "--..";
            m['0'] = "-----"; m['1'] = ".----"; m['2'] = "..---"; m['3'] = "...--";
            m['4'] = "....-"; m['5'] = "....."; m['6'] = "-...."; m['7'] = "--...";
            m['8'] = "---.."; m['9'] = "----.";
            m['.'] = ".-.-.-"; m[','] = "--..--"; m['?'] = "..--.."; m['\''] = ".----.";
            m['!'] = "-.-.--"; m['/'] = "-..-."; m['('] = "-.--."; m[')'] = "-.--.-";
            m['&'] = ".-..."; m[':'] = "---..."; m['='] = "-...-"; m['+'] = ".-.-.-";
            m['-'] = "-....-"; m['"'] = ".-..-.";
            return m;
        }

        public void Enqueue(string kind, string text, int wpm, string key)
        {
            Enqueue(kind, text, wpm, key, "", "", 0);
        }

        public void Enqueue(string kind, string text, int wpm, string key,
                            string context, string device, int cooldownMs)
        {
            if (kind != "phrase" && kind != "dot" && kind != "dash" &&
                kind != "on" && kind != "off")
            {
                Logger.Write("Unknown job kind \"" + kind + "\"; ignored.");
                return;
            }
            lock (queueGate)
            {
                LampJob job = new LampJob();
                job.Kind = kind;
                job.Text = text == null ? "" : text;
                job.Wpm = wpm;
                job.Key = (key == null || key.Trim().Length == 0) ? "l" : key.Trim();
                job.Gen = generation;
                job.Context = context == null ? "" : context;
                job.Device = device == null ? "" : device;
                job.CooldownMs = cooldownMs;
                queue.Enqueue(job);
                if (worker == null)
                {
                    worker = new Thread(WorkerLoop);
                    worker.IsBackground = true;
                    worker.Start();
                }
            }
        }

        // How long a job will take, in the same units the worker sleeps in:
        // a lit element is tap(25) + length + tap(25), one unit of gap after
        // every element, two more to make the 3u inter-character gap, and 6u
        // for a word space. This is what the key's recharge ring is timed to,
        // so the ring empties exactly when the lamp stops flashing.
        public static int EstimateMs(string kind, string text, int wpm)
        {
            int unit = UnitMs(wpm);
            if (kind == "dot") return unit + 2 * TapMs + unit;
            if (kind == "dash") return 3 * unit + 2 * TapMs + unit;
            if (kind != "phrase") return 0;   // lamp on/off: a single tap

            long total = 0;
            string upper = (text == null ? "" : text).ToUpperInvariant();
            foreach (char c in upper)
            {
                if (c == ' ') { total += 6 * unit; continue; }
                string code;
                if (!Morse.TryGetValue(c, out code)) continue;
                foreach (char e in code)
                    total += (e == '.' ? unit : 3 * unit) + 2 * TapMs + unit;
                total += 2 * unit;
            }
            if (total > MaxAutoCooldownMs) return MaxAutoCooldownMs;
            return (int)total;
        }

        private const int TapMs = 25;              // the pause inside a tap
        private const int MaxAutoCooldownMs = 120000;

        // Cancels the running send and drops queued lamp jobs (stop jobs kept).
        public void CancelLamp(string reason)
        {
            lock (queueGate)
            {
                generation++;
                int dropped = 0;
                List<LampJob> keep = new List<LampJob>();
                while (queue.Count > 0)
                {
                    LampJob j = queue.Dequeue();
                    if (j.Kind == "stop") keep.Add(j);
                    else dropped++;
                }
                for (int n = 0; n < keep.Count; n++) queue.Enqueue(keep[n]);
                if (dropped > 0 || worker != null)
                    Logger.Write("send cancelled (" + reason + "; " + dropped + " queued dropped)");
            }
            // A cancelled send means the lamp is done flashing, so the recharge
            // rings stop too -- the key must not sit there counting down for a
            // send that no longer exists.
            StreamDeckClient.EndAllCooldowns();
        }

        // Full stop: cancel everything, then queue a job that extinguishes
        // the lamp if we tracked it as lit.
        public void StopAll(string reason)
        {
            CancelLamp(reason);
            lock (queueGate)
            {
                LampJob job = new LampJob();
                job.Kind = "stop";
                job.Text = "";
                job.Wpm = 8;
                job.Key = "";
                job.Gen = generation;
                queue.Enqueue(job);
                if (worker == null)
                {
                    worker = new Thread(WorkerLoop);
                    worker.IsBackground = true;
                    worker.Start();
                }
            }
        }

        private void WorkerLoop()
        {
            while (true)
            {
                LampJob job = null;
                lock (queueGate)
                {
                    if (queue.Count > 0) job = queue.Dequeue();
                    else worker = null;
                }
                if (job == null) return;
                bool animated = (job.Kind == "phrase" || job.Kind == "dot" || job.Kind == "dash") &&
                                job.Context.Length > 0;
                if (animated)
                {
                    int ms = job.CooldownMs > 0
                        ? job.CooldownMs
                        : EstimateMs(job.Kind, job.Text, job.Wpm);
                    StreamDeckClient.BeginCooldown(job.Context, job.Device, ms);
                }
                try { Run(job); }
                catch (Exception ex) { Logger.Write("Send job failed: " + ex.Message); }
                finally
                {
                    // The ring follows the lamp, not a guessed timer: as soon as
                    // the send is done (or aborted) the key is handed back.
                    if (animated) StreamDeckClient.FinishCooldown(job.Context);
                }
            }
        }

        private void Run(LampJob job)
        {
            if (job.Kind == "stop")
            {
                string k = job.Key.Length > 0 ? job.Key : lastKey;
                if (lampState == LAMP_ON)
                {
                    Logger.Write("Stop: extinguishing lamp (tap \"" + k + "\")");
                    Tap(k);
                    lampState = LAMP_OFF;
                }
                else
                {
                    Logger.Write("Stop: lamp not tracked as lit; nothing tapped.");
                }
                return;
            }

            if (job.Gen != generation)
            {
                Logger.Write("Send job skipped, cancelled before start (" + job.Kind + ")");
                return;
            }

            if (job.Kind == "on")
            {
                if (lampState != LAMP_ON)
                {
                    Logger.Write("Lamp ON: tap \"" + job.Key + "\"");
                    Tap(job.Key);
                    lampState = LAMP_ON;
                }
                else Logger.Write("Lamp already on; no tap.");
                return;
            }
            if (job.Kind == "off")
            {
                if (lampState == LAMP_ON)
                {
                    Logger.Write("Lamp OFF: tap \"" + job.Key + "\"");
                    Tap(job.Key);
                    lampState = LAMP_OFF;
                }
                else Logger.Write("Lamp not lit (state " + lampState + "); no tap.");
                return;
            }

            int unit = UnitMs(job.Wpm);

            if (job.Kind == "dot" || job.Kind == "dash")
            {
                Logger.Write("Element send: " + job.Kind + " at " + ClampWpm(job.Wpm) +
                    " WPM (unit=" + unit + "ms) key=\"" + job.Key + "\"");
                Element(job.Key, job.Kind == "dot", unit);
                SleepInterruptible(unit, job); // post-element gap
                Logger.Write("Element send done.");
                return;
            }

            // phrase
            string text = job.Text.ToUpperInvariant();
            Logger.Write("Phrase send: \"" + (text.Length > 60 ? text.Substring(0, 60) : text) +
                "\" at " + ClampWpm(job.Wpm) + " WPM (unit=" + unit + "ms) key=\"" + job.Key + "\"");
            foreach (char c in text)
            {
                if (job.Gen != generation) { Logger.Write("send cancelled mid-phrase"); return; }
                if (c == ' ') { SleepInterruptible(6 * unit, job); continue; }
                string code;
                if (!Morse.TryGetValue(c, out code))
                {
                    Logger.Write("Unknown character '" + c + "' skipped.");
                    continue;
                }
                foreach (char e in code)
                {
                    if (job.Gen != generation) { Logger.Write("send cancelled mid-character"); return; }
                    Element(job.Key, e == '.', unit);
                    SleepInterruptible(unit, job);      // element trailing gap (1u)
                }
                SleepInterruptible(2 * unit, job);      // to 3u inter-character total
            }
            Logger.Write("Phrase send complete.");
        }

        // One lit element: tap on, keep lit for its length, tap off.
        // (Even taps -> lamp returns to its prior state.)
        private void Element(string key, bool isDot, int unit)
        {
            Tap(key);
            Thread.Sleep(isDot ? unit : 3 * unit);
            Tap(key);
        }

        private void SleepInterruptible(int ms, LampJob job)
        {
            // Jobs check cancellation between elements; sleep in small
            // slices so a stop reacts quickly.
            int slept = 0;
            while (slept < ms)
            {
                if (job.Gen != generation) return;
                int slice = ms - slept < 25 ? ms - slept : 25;
                Thread.Sleep(slice);
                slept += slice;
            }
        }

        private void Tap(string token)
        {
            ushort vk; bool extended;
            if (!KeyMap.TryResolve(token, out vk, out extended))
            {
                Logger.Write("Unknown lamp key \"" + token + "\"; send aborted.");
                return;
            }
            lastKey = token;
            SendKey(vk, extended, false);
            Thread.Sleep(25);
            SendKey(vk, extended, true);
        }

        private static void SendKey(ushort vk, bool extended, bool up)
        {
            // Send ONE event carrying both the virtual key and the hardware
            // scan code. Raw Input / DirectInput readers (games) key off wScan:
            // with wScan = 0 they receive MakeCode 0 and ignore the press,
            // which is why keys landed in Notepad but not in-game.
            INPUT[] inputs = new INPUT[1];
            inputs[0].type = 1; // INPUT_KEYBOARD
            inputs[0].U.ki.wVk = vk;
            inputs[0].U.ki.wScan = (ushort)Native.MapVirtualKey(vk, 0);
            inputs[0].U.ki.dwFlags =
                (up ? KEYEVENTF_KEYUP : 0) | (extended ? KEYEVENTF_EXTENDEDKEY : 0);
            inputs[0].U.ki.time = 0;
            inputs[0].U.ki.dwExtraInfo = IntPtr.Zero;
            Native.SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        private static int ClampWpm(int wpm)
        {
            if (wpm < 5) return 5;
            if (wpm > 25) return 25;
            return wpm;
        }

        // PARIS standard: unit = 1200 / wpm milliseconds.
        private static int UnitMs(int wpm)
        {
            return 1200 / ClampWpm(wpm);
        }
    }

    // ---------------------------------------------------------------------
    // Minimal strict JSON reader for our flat {"k":"v",...} protocol
    // ---------------------------------------------------------------------
    internal sealed class JsonReader
    {
        private readonly string s;
        private int i;

        public JsonReader(string s) { this.s = s; i = 0; }

        public void Expect(char c)
        {
            SkipWs();
            if (i >= s.Length || s[i] != c) throw new Exception("Expected '" + c + "' at offset " + i);
            i++;
        }

        public bool TryEat(char c)
        {
            SkipWs();
            if (i < s.Length && s[i] == c) { i++; return true; }
            return false;
        }

        // Value may be a quoted string or a bare token (number, true/false).
        public string ReadValue()
        {
            SkipWs();
            if (i < s.Length && s[i] == '"') return ReadString();
            StringBuilder sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ',' || c == '}' || char.IsWhiteSpace(c)) break;
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        public string ReadString()
        {
            SkipWs();
            if (i < s.Length && s[i] == '"')
            {
                i++;
                StringBuilder sb = new StringBuilder();
                while (i < s.Length && s[i] != '"')
                {
                    char c = s[i];
                    if (c == '\\')
                    {
                        i++;
                        if (i >= s.Length) break;
                        char e = s[i];
                        switch (e)
                        {
                            case 'n': sb.Append('\n'); break;
                            case 't': sb.Append('\t'); break;
                            case 'r': sb.Append('\r'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'u':
                                if (i + 4 < s.Length)
                                {
                                    sb.Append((char)int.Parse(s.Substring(i + 1, 4), NumberStyles.HexNumber));
                                    i += 4;
                                }
                                break;
                            default: sb.Append(e); break;
                        }
                    }
                    else sb.Append(c);
                    i++;
                }
                if (i >= s.Length) throw new Exception("Unterminated string");
                i++; // closing quote
                return sb.ToString();
            }
            throw new Exception("Expected string at offset " + i);
        }

        private void SkipWs()
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }
    }

    // ---------------------------------------------------------------------
    // Keybind detection: best-effort scan of Star Citizen binding files for
    // the ship-lights key. Never throws; caps the walk.
    // ---------------------------------------------------------------------
    internal static class KeybindDetect
    {
        public sealed class Result
        {
            public bool Ok;
            public string Key = "";
            public string Where = "";
        }

        public static Result Find()
        {
            List<string> roots = new List<string>();
            AddRoot(roots, Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Star Citizen"));
            AddRoot(roots, Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Stars Citizen")); // historical typo'd folder, still scanned
            AddRoot(roots, Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Star Citizen"));

            if (roots.Count == 0)
            {
                Logger.Write("Detect: no Star Citizen config folders found.");
                return new Result { Ok = false };
            }

            foreach (string root in roots)
            {
                List<string> files = new List<string>();
                try { CollectFiles(root, 0, files); }
                catch (Exception ex) { Logger.Write("Detect: scan error in " + root + ": " + ex.Message); }
                Logger.Write("Detect: scanning " + files.Count + " binding file(s) under " + root);

                foreach (string file in files)
                {
                    string content = null;
                    try { content = File.ReadAllText(file); }
                    catch { continue; }
                    if (content.IndexOf("light", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    Result r = ScanFile(content, file);
                    if (r != null && r.Ok)
                    {
                        Logger.Write("Detect: found lights key \"" + r.Key + "\" in " + r.Where);
                        return r;
                    }
                }
            }
            Logger.Write("Detect: no plausible lights key found.");
            return new Result { Ok = false };
        }

        private static void AddRoot(List<string> roots, string path)
        {
            try { if (Directory.Exists(path)) roots.Add(path); } catch { }
        }

        // Recursive walk: depth <= 6, max ~200 candidate files, errors ignored.
        private static void CollectFiles(string dir, int depth, List<string> files)
        {
            if (depth > 6 || files.Count >= 200) return;
            string[] subDirs = new string[0];
            try { subDirs = Directory.GetDirectories(dir); } catch { }
            string[] fs = new string[0];
            try { fs = Directory.GetFiles(dir); } catch { }
            foreach (string f in fs)
            {
                string name = Path.GetFileName(f);
                if (name.EndsWith(".keys", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "client.cfg", StringComparison.OrdinalIgnoreCase))
                {
                    files.Add(f);
                    if (files.Count >= 200) return;
                }
            }
            foreach (string d in subDirs) CollectFiles(d, depth + 1, files);
        }

        private static Result ScanFile(string content, string path)
        {
            string[] lines = content.Replace("\r", "").Split('\n');
            // Preference order: light_master > lights > light.
            string[] needles = { "light_master", "lights", "light" };
            foreach (string needle in needles)
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    // Closest first: same element/line, then +-1, then +-2.
                    int[] order = { 0, -1, 1, -2, 2 };
                    for (int n = 0; n < order.Length; n++)
                    {
                        int j = i + order[n];
                        if (j < 0 || j >= lines.Length) continue;
                        string key = ExtractKeyToken(lines[j]);
                        if (key != null) return new Result { Ok = true, Key = key, Where = path };
                    }
                }
            }
            return null;
        }

        private static string ExtractKeyToken(string line)
        {
            Match m = Regex.Match(line, "b[12]\\s*=\\s*[\"']([^\"']{1,4})[\"']");
            if (m.Success && Plausible(m.Groups[1].Value)) return m.Groups[1].Value.ToUpperInvariant();
            m = Regex.Match(line, "key\\s*=\\s*[\"']([^\"']{1,4})[\"']");
            if (m.Success && Plausible(m.Groups[1].Value)) return m.Groups[1].Value.ToUpperInvariant();
            m = Regex.Match(line, "<bind[^>]*[\"']([A-Za-z0-9]{1,4})[\"']");
            if (m.Success && Plausible(m.Groups[1].Value)) return m.Groups[1].Value.ToUpperInvariant();
            m = Regex.Match(line, "\\be([A-Za-z0-9])(?![A-Za-z0-9])");
            if (m.Success) return m.Groups[1].Value.ToUpperInvariant();
            return null;
        }

        private static bool Plausible(string token)
        {
            token = token.Trim().ToUpperInvariant();
            if (token.Length == 1)
                return (token[0] >= 'A' && token[0] <= 'Z') || (token[0] >= '0' && token[0] <= '9');
            return Regex.IsMatch(token, "^F([1-9]|1[0-2])$");
        }
    }

    // ---------------------------------------------------------------------
    // Stream Deck registration client: connects to the WebSocket URL Stream
    // Deck passed on the command line, registers, answers its pings, and
    // executes physical button presses (keyDown) straight from the app.
    // ---------------------------------------------------------------------
    internal static class StreamDeckClient
    {
        public static void Run(string url, string pluginUuid, string registerEvent, LampExecutor executor)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
            {
                Logger.Write("Unparseable --plugin URL: " + url);
                return;
            }
            for (int attempt = 0; attempt < 10; attempt++)
            {
                bool connected = false;
                try
                {
                    connected = Session(uri, pluginUuid, registerEvent, executor);
                }
                catch (Exception ex)
                {
                    Logger.Write("Stream Deck link failed: " + ex.Message);
                }
                if (connected)
                {
                    // The link dropped deliberately (upgrade, reload, Stream Deck
                    // quitting): exit so Stream Deck manages our lifecycle.
                    Environment.Exit(0);
                }
                Thread.Sleep(3000);
            }
            Logger.Write("Could not reach Stream Deck; giving up registration.");
        }

        // Returns true if the session actually connected before it ended.
        private static bool Session(Uri uri, string pluginUuid, string registerEvent, LampExecutor executor)
        {
            int port = uri.Port > 0 ? uri.Port : 80;
            using (TcpClient socket = new TcpClient())
            {
                socket.Connect(uri.Host, port);
                socket.NoDelay = true;
                NetworkStream stream = socket.GetStream();

                byte[] nonce = new byte[16];
                using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(nonce);
                string key = Convert.ToBase64String(nonce);
                string path = string.IsNullOrEmpty(uri.Query) ? "/" : uri.PathAndQuery;
                string request = "GET " + path + " HTTP/1.1\r\n" +
                    "Host: " + uri.Host + ":" + port + "\r\n" +
                    "Upgrade: websocket\r\nConnection: Upgrade\r\n" +
                    "Sec-WebSocket-Key: " + key + "\r\n" +
                    "Sec-WebSocket-Version: 13\r\n\r\n";
                byte[] rb = Encoding.ASCII.GetBytes(request);
                stream.Write(rb, 0, rb.Length);
                stream.Flush();

                StreamReader reader = new StreamReader(stream, Encoding.ASCII);
                string statusLine = reader.ReadLine();
                if (statusLine == null || !statusLine.Contains("101"))
                    throw new Exception("handshake refused: " + statusLine);
                string line;
                while ((line = reader.ReadLine()) != null && line.Length > 0) { }

                // v2 native protocol: { "event": <registerEvent>, "uuid": <pluginUUID> }
                // (the older v1 form used "eventType"; Stream Deck 7.6 ignores that
                // and terminates a plugin that never registers).
                Send(stream, "{\"event\":\"" + registerEvent + "\",\"uuid\":\"" + pluginUuid + "\"}", true);
                Logger.Write("Registration sent; waiting for Stream Deck ack.");

                // Publish the live stream so other parts (PI title relays) can
                // send commands to Stream Deck while the session is up.
                lock (appStreamGate) appStream = stream;
                try
                {
                    // Read loop: log early frames, answer pings, execute presses.
                    int logged = 0;
                    // willAppear carries the button's whole settings blob, which
                    // is the only place the app could hand over a user-picked
                    // icon. Log a few of those in full while diagnosing.
                    int loggedAppear = 0;
                    while (true)
                    {
                        string message = ReadFrame(stream);
                        if (message == null) return true;
                        bool appear = message.Contains("\"willAppear\"");
                        if (logged < 5 || (appear && loggedAppear < 6))
                        {
                            int cap = appear ? 4000 : 300;
                            Logger.Write("Stream Deck <- " + (message.Length > cap ? message.Substring(0, cap) : message));
                            if (appear) loggedAppear++;
                            if (logged < 5) logged++;
                        }
                        if (message.Contains("ping"))
                            Send(stream, message, true);
                        else if (message.Contains("\"keyDown\"") || message.Contains("\"keyUp\""))
                            HandleDeckEvent(message, executor);
                    }
                }
                finally
                {
                    lock (appStreamGate) appStream = null;
                }
            }
        }

        // ----- plugin -> Stream Deck outbound channel -----

        private static NetworkStream appStream;
        private static readonly object appStreamGate = new object();

        public static bool SendToApp(string json)
        {
            lock (appStreamGate)
            {
                if (appStream == null) return false;
                try { Send(appStream, json, true); return true; }
                catch (Exception ex)
                {
                    Logger.Write("SendToApp failed: " + ex.Message);
                    appStream = null;
                    return false;
                }
            }
        }

        // ----- cooldown animation -------------------------------------------------
        //
        // Stream Deck has no animation support in setImage, so the recharge is a
        // short film: one PNG frame per 100 ms pushed over our app session, and
        // the key handed back at the end by sending the command with "image"
        // omitted -- per the protocol an omitted field restores the artwork the
        // user configured. (An SVG data URI is accepted by the socket and then
        // silently never drawn, so the frames have to be real rasters.)
        //
        // What differs from Key Inject is the length: nobody has to guess it. It
        // is the send's own duration, derived from the same PARIS timing the
        // worker is about to execute, and the film is cut short the moment the
        // send really ends -- which matters here because a Stop can abort a
        // phrase halfway through.

        private const string LampAction = "com.sundowner.aldislamp.lamp";
        private const int CooldownFrameMs = 100;    // 10 fps: smooth enough, cheap
        private const int CooldownGraceMs = 15000;  // how long an empty ring is
                                                    // held if a send outlives its
                                                    // estimate

        private sealed class CooldownRun
        {
            public long EndTicks;
            public int Generation;
            public bool Finished;
        }

        private static readonly Dictionary<string, CooldownRun> cooldowns =
            new Dictionary<string, CooldownRun>();
        private static readonly object cooldownGate = new object();
        private static int cooldownCounter;

        public static bool IsCooling(string context)
        {
            if (context.Length == 0) return false;
            lock (cooldownGate)
            {
                CooldownRun r;
                if (!cooldowns.TryGetValue(context, out r)) return false;
                if (r.Finished || DateTime.UtcNow.Ticks >= r.EndTicks)
                {
                    cooldowns.Remove(context);
                    return false;
                }
                return true;
            }
        }

        public static void BeginCooldown(string context, string device, int durationMs)
        {
            if (context.Length == 0 || durationMs <= 0) return;
            int gen;
            long end;
            lock (cooldownGate)
            {
                gen = ++cooldownCounter;
                end = DateTime.UtcNow.Ticks + (long)durationMs * TimeSpan.TicksPerMillisecond;
                cooldowns[context] = new CooldownRun { EndTicks = end, Generation = gen };
            }
            Thread t = new Thread(delegate () { AnimateCooldown(context, device, durationMs, end, gen); });
            t.IsBackground = true;
            t.Start();
        }

        // The send is over (finished, aborted, or extinguished): stop the film
        // now instead of letting a timer that no longer matches the lamp run out.
        public static void FinishCooldown(string context)
        {
            if (context.Length == 0) return;
            lock (cooldownGate)
            {
                CooldownRun r;
                if (cooldowns.TryGetValue(context, out r)) r.Finished = true;
            }
        }

        public static void EndAllCooldowns()
        {
            lock (cooldownGate)
            {
                foreach (KeyValuePair<string, CooldownRun> kv in cooldowns) kv.Value.Finished = true;
            }
        }

        public static bool AnyCooling()
        {
            lock (cooldownGate) return cooldowns.Count > 0;
        }

        private static void AnimateCooldown(string context, string device, int durationMs, long endTicks, int gen)
        {
            try
            {
                AnimateLoop(context, device, durationMs, endTicks, gen);
            }
            catch (Exception ex)
            {
                Logger.Write("Animate crashed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void AnimateLoop(string context, string device, int durationMs, long endTicks, int gen)
        {
            double totalTicks = (double)durationMs * TimeSpan.TicksPerMillisecond;
            int lastArc = -1;
            while (true)
            {
                bool finished;
                long remaining;
                lock (cooldownGate)
                {
                    CooldownRun r;
                    if (!cooldowns.TryGetValue(context, out r) || r.Generation != gen) return;
                    finished = r.Finished;
                    remaining = r.EndTicks - DateTime.UtcNow.Ticks;
                }
                if (finished) break;
                if (remaining <= 0)
                {
                    // A send that outlives its own estimate should not happen;
                    // hold the empty ring rather than claim the key is ready.
                    long over = (DateTime.UtcNow.Ticks - endTicks) / TimeSpan.TicksPerMillisecond;
                    if (over > CooldownGraceMs) break;
                }

                double frac = remaining <= 0 ? 0.0 : remaining / totalTicks;
                int arc = (int)Math.Round(frac * 100.0);
                if (arc != lastArc)
                {
                    lastArc = arc;
                    if (!SendToApp("{\"event\":\"setImage\",\"action\":\"" + LampAction +
                        "\",\"context\":\"" + Escape(context) + "\",\"device\":\"" + Escape(device) +
                        "\",\"payload\":{\"image\":\"" + RingImage(frac) + "\",\"state\":0}}"))
                    {
                        Logger.Write("Cooldown frames stopped: no app link.");
                        break;
                    }
                }
                Thread.Sleep(CooldownFrameMs);
            }

            lock (cooldownGate)
            {
                CooldownRun r;
                if (cooldowns.TryGetValue(context, out r) && r.Generation == gen) cooldowns.Remove(context);
            }

            // Undo the film. Omitting "image" resets the key to the artwork of
            // its current state; setState re-applies that artwork as well, so
            // the key cannot be left showing the empty ring. No log line: the
            // send itself is already in the log, and this would be noise.
            SendToApp("{\"event\":\"setState\",\"action\":\"" + LampAction +
                "\",\"context\":\"" + Escape(context) + "\",\"device\":\"" + Escape(device) +
                "\",\"payload\":{\"state\":0}}");
            SendToApp("{\"event\":\"setImage\",\"action\":\"" + LampAction +
                "\",\"context\":\"" + Escape(context) + "\",\"device\":\"" + Escape(device) +
                "\",\"payload\":{\"state\":0}}");
        }

        public static string ShortId(string context)
        {
            if (context.Length <= 8) return context.Length == 0 ? "none" : context;
            return context.Substring(0, 8);
        }

        // Frames are cached per remaining percent (101 possible frames), drawn
        // once with GDI+ at 144x144 so they stay crisp on the larger devices.
        private static readonly Dictionary<int, string> ringFrames = new Dictionary<int, string>();
        private static readonly object ringGate = new object();

        private static string RingImage(double fractionRemaining)
        {
            double f = fractionRemaining < 0 ? 0 : (fractionRemaining > 1 ? 1 : fractionRemaining);
            int pct = (int)Math.Round(f * 100.0);
            lock (ringGate)
            {
                string cached;
                if (ringFrames.TryGetValue(pct, out cached)) return cached;
                string png = DrawRingPng(pct);
                ringFrames[pct] = png;
                return png;
            }
        }

        private static string DrawRingPng(int pct)
        {
            const int size = 144;
            const float stroke = 14f;
            float pad = stroke / 2f + 3f;
            RectangleF ring = new RectangleF(pad, pad, size - 2f * pad, size - 2f * pad);

            using (Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);

                    using (Pen track = new Pen(Color.FromArgb(255, 43, 43, 43), stroke))
                        g.DrawArc(track, ring, 0f, 360f);

                    Color live = pct > 25
                        ? Color.FromArgb(255, 0x4e, 0xc9, 0xff)
                        : Color.FromArgb(255, 0xff, 0xb0, 0x20);
                    using (Pen arc = new Pen(live, stroke))
                    {
                        arc.StartCap = LineCap.Round;
                        arc.EndCap = LineCap.Round;
                        float sweep = 360f * pct / 100f;
                        if (sweep >= 360f) g.DrawArc(arc, ring, 0f, 360f);
                        else if (sweep > 0.5f) g.DrawArc(arc, ring, -90f, sweep);
                    }

                    // Two bars in the middle: the key is resting, not ready.
                    using (SolidBrush glyph = new SolidBrush(Color.FromArgb(255, 236, 236, 236)))
                    {
                        g.FillRectangle(glyph, 51f, 52f, 15f, 40f);
                        g.FillRectangle(glyph, 78f, 52f, 15f, 40f);
                    }

                    using (MemoryStream ms = new MemoryStream())
                    {
                        bmp.Save(ms, ImageFormat.Png);
                        return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
                    }
                }
            }
        }

        // ----- physical button execution (keyDown from the app) -----

        private static void HandleDeckEvent(string message, LampExecutor executor)
        {
            bool lamp = message.Contains("\"action\":\"com.sundowner.aldislamp.lamp\"");
            bool stop = message.Contains("\"action\":\"com.sundowner.aldislamp.stop\"");
            if (!lamp && !stop) return;
            if (!message.Contains("\"keyDown\"")) return; // releases need no work: taps cannot stick

            if (stop)
            {
                Logger.Write("Deck press: stop action");
                executor.StopAll("stop button pressed");
                return;
            }

            string settings = ExtractObject(message, "\"settings\":");
            string mode = ExtractString(settings, "\"mode\":\"");
            string text = ExtractString(settings, "\"text\":\"");
            int wpm = ExtractInt(settings, "\"wpm\":", 8);
            string key = ExtractString(settings, "\"lampKey\":\"");
            int cooldownMs = ExtractInt(settings, "\"cooldownMs\":", 0);
            string context = ExtractString(message, "\"context\":\"");
            string device = ExtractString(message, "\"device\":\"");
            if (key.Length == 0) key = "l";
            if (mode.Length == 0) mode = "phrase";

            int estimate = LampExecutor.EstimateMs(mode, text, wpm);
            Logger.Write("Deck press [" + StreamDeckClient.ShortId(context) + "]: mode=" + mode +
                " wpm=" + wpm + " key=\"" + key + "\" cooldown=" +
                (cooldownMs > 0 ? cooldownMs + "ms set" : "auto") +
                " send=" + (estimate / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + "s" +
                " text=\"" + (text.Length > 40 ? text.Substring(0, 40) : text) + "\"");

            if (mode == "phrase" && text.Trim().Length == 0)
            {
                Logger.Write("Press ignored: empty phrase.");
                StreamDeckClient.ShowAlert(context);
                return;
            }
            if (StreamDeckClient.IsCooling(context))
            {
                // The ring is still unwinding, which means the previous send is
                // still flashing. Two overlapping phrases on one lamp would just
                // be noise, so this press is dropped, not queued.
                Logger.Write("Press ignored: " + StreamDeckClient.ShortId(context) +
                    " still signalling.");
                return;
            }
            // One lamp on screen: a new press replaces whatever send is running.
            executor.CancelLamp("new deck press");
            executor.Enqueue(mode, text, wpm, key, context, device, cooldownMs);
        }

        // Flash the red "not available" indicator so a dead button is not silent.
        public static void ShowAlert(string context)
        {
            if (context.Length == 0) return;
            SendToApp("{\"event\":\"showAlert\",\"context\":\"" + Escape(context) + "\"}");
        }

        // Minimal flat extractors for nested JSON (values are simple
        // string/number fields; good enough for our own settings shape).
        private static string ExtractString(string s, string marker)
        {
            int i = s.IndexOf(marker, StringComparison.Ordinal);
            if (i < 0) return "";
            i += marker.Length;
            StringBuilder sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length) { sb.Append(s[i + 1]); i += 2; continue; }
                if (c == '"') break;
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        private static string ExtractObject(string s, string marker)
        {
            int i = s.IndexOf(marker, StringComparison.Ordinal);
            if (i < 0) return "";
            i += marker.Length;
            if (i >= s.Length || s[i] != '{') return "";
            int depth = 0;
            int start = i;
            for (; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '{') depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0) return s.Substring(start, i - start + 1);
                }
            }
            return "";
        }

        private static int ExtractInt(string s, string marker, int fallback)
        {
            int i = s.IndexOf(marker, StringComparison.Ordinal);
            if (i < 0) return fallback;
            i += marker.Length;
            int value = 0, digits = 0;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9')
            {
                value = value * 10 + (s[i] - '0');
                i++;
                digits++;
            }
            return digits == 0 ? fallback : value;
        }

        internal static string Escape(string s)
        {
            StringBuilder sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') { sb.Append('\\'); sb.Append(c); }
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\t') sb.Append("\\t");
                else if (c < ' ') { /* drop control characters */ }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private static string ReadFrame(NetworkStream stream)
        {
            int b0 = stream.ReadByte();
            if (b0 < 0) return null;
            int b1 = stream.ReadByte();
            if (b1 < 0) return null;
            long len = b1 & 0x7F;
            if (len == 126)
            {
                int hi = stream.ReadByte(), lo = stream.ReadByte();
                if (hi < 0 || lo < 0) return null;
                len = (hi << 8) | lo;
            }
            else if (len == 127)
            {
                len = 0;
                for (int n = 0; n < 8; n++)
                {
                    int q = stream.ReadByte();
                    if (q < 0) return null;
                    len = (len << 8) | (long)(uint)(q & 0xFF);
                }
            }
            if (len > 1048576) return null;
            bool masked = (b1 & 0x80) != 0;
            byte[] mask = null;
            if (masked)
            {
                mask = new byte[4];
                if (!ReadBytes(stream, mask, 4)) return null;
            }
            byte[] payload = new byte[len];
            if (len > 0 && !ReadBytes(stream, payload, (int)len)) return null;
            if (masked)
                for (int n = 0; n < payload.Length; n++) payload[n] ^= mask[n % 4];
            if ((b0 & 0x0F) == 0x8) return null; // close
            return Encoding.UTF8.GetString(payload);
        }

        private static bool ReadBytes(Stream s, byte[] buf, int count)
        {
            int off = 0;
            while (off < count)
            {
                int n = s.Read(buf, off, count - off);
                if (n <= 0) return false;
                off += n;
            }
            return true;
        }

        private static void Send(NetworkStream stream, string text, bool masked)
        {
            byte[] payload = Encoding.UTF8.GetBytes(text);
            List<byte> frame = new List<byte>();
            frame.Add(0x81);
            byte[] mask = new byte[4];
            if (masked)
            {
                using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(mask);
                if (payload.Length < 126) frame.Add((byte)(0x80 | payload.Length));
                else
                {
                    frame.Add((byte)(0x80 | 126));
                    frame.Add((byte)(payload.Length >> 8));
                    frame.Add((byte)(payload.Length & 0xFF));
                }
                frame.AddRange(mask);
                for (int i = 0; i < payload.Length; i++) frame.Add((byte)(payload[i] ^ mask[i % 4]));
            }
            else
            {
                if (payload.Length < 126) frame.Add((byte)payload.Length);
                else
                {
                    frame.Add(126);
                    frame.Add((byte)(payload.Length >> 8));
                    frame.Add((byte)(payload.Length & 0xFF));
                }
                frame.AddRange(payload);
            }
            byte[] bytes = frame.ToArray();
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
        }
    }

    // ---------------------------------------------------------------------
    // WebSocket server (RFC 6455 subset: text, close, ping/pong) for the
    // Property Inspector on port 12346 (Key Inject keeps 12345).
    // ---------------------------------------------------------------------
    internal sealed class WebSocketServer
    {
        private const string MagicGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

        private readonly TcpListener listener;
        private readonly LampExecutor executor;
        private readonly List<ClientLink> clients = new List<ClientLink>();
        private readonly object clientsGate = new object();
        private volatile bool running;

        public WebSocketServer(int port, LampExecutor executor)
        {
            this.listener = new TcpListener(IPAddress.Loopback, port);
            this.executor = executor;
        }

        public void Start()
        {
            listener.Start();
            running = true;
            Thread t = new Thread(AcceptLoop);
            t.IsBackground = true;
            t.Start();
        }

        public void Stop()
        {
            running = false;
            try { listener.Stop(); } catch { }
        }

        private void AcceptLoop()
        {
            while (running)
            {
                TcpClient socket;
                try { socket = listener.AcceptTcpClient(); }
                catch { break; }
                Thread t = new Thread(delegate () { Handle(socket); });
                t.IsBackground = true;
                t.Start();
            }
        }

        private void Handle(TcpClient socket)
        {
            ClientLink link = null;
            try
            {
                socket.NoDelay = true;
                NetworkStream stream = socket.GetStream();

                // --- HTTP upgrade handshake ---
                StreamReader reader = new StreamReader(stream, Encoding.ASCII);
                string requestLine = reader.ReadLine();
                if (requestLine == null || !requestLine.StartsWith("GET")) { socket.Close(); return; }
                string key = null;
                string line;
                while ((line = reader.ReadLine()) != null && line.Length > 0)
                {
                    int colon = line.IndexOf(':');
                    if (colon > 0 &&
                        string.Equals(line.Substring(0, colon).Trim(), "Sec-WebSocket-Key", StringComparison.OrdinalIgnoreCase))
                        key = line.Substring(colon + 1).Trim();
                }
                if (key == null) { socket.Close(); return; }

                using (SHA1 sha1 = SHA1.Create())
                {
                    byte[] hash = sha1.ComputeHash(Encoding.ASCII.GetBytes(key + MagicGuid));
                    string accept = Convert.ToBase64String(hash);
                    string response = "HTTP/1.1 101 Switching Protocols\r\n" +
                                      "Upgrade: websocket\r\n" +
                                      "Connection: Upgrade\r\n" +
                                      "Sec-WebSocket-Accept: " + accept + "\r\n\r\n";
                    byte[] bytes = Encoding.ASCII.GetBytes(response);
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush();
                }

                link = new ClientLink(this, socket, stream);
                lock (clientsGate) clients.Add(link);
                Logger.Write("PI client connected.");

                while (running)
                {
                    string message = ReadFrame(stream);
                    if (message == null) break;
                    Dispatch(link, message);
                }
            }
            catch (Exception ex)
            {
                Logger.Write("Connection error: " + ex.Message);
            }
            finally
            {
                if (link != null)
                {
                    lock (clientsGate) clients.Remove(link);
                    try { link.CloseSocket(); } catch { }
                }
                // No ReleaseAll here: every send is an even pair of taps, so a
                // dropped Property Inspector cannot leave anything stuck down.
                Logger.Write("PI client disconnected.");
            }
        }

        private void Dispatch(ClientLink link, string message)
        {
            try
            {
                JsonReader r = new JsonReader(message);
                r.Expect('{');
                string type = "";
                string note = "";
                string ctx = "";
                string dev = "";
                string ttl = "";
                string act = "";
                string txt = "";
                string key = "";
                string evt = "";
                int wpm = 8;
                int cooldownMs = 0;
                while (true)
                {
                    string name = r.ReadString();
                    r.Expect(':');
                    string value = r.ReadValue();
                    if (name == "type") type = value;
                    else if (name == "note") note = value;
                    else if (name == "context") ctx = value;
                    else if (name == "device") dev = value;
                    else if (name == "title") ttl = value;
                    else if (name == "action") act = value;
                    else if (name == "text") txt = value;
                    else if (name == "key") key = value;
                    else if (name == "event") evt = value;
                    else if (name == "wpm")
                    {
                        int parsed;
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                            wpm = parsed;
                    }
                    else if (name == "cooldownMs")
                    {
                        int parsed;
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                            cooldownMs = parsed;
                    }
                    if (!r.TryEat(',')) break;
                }
                if (act.Length == 0) act = "com.sundowner.aldislamp.lamp";
                if (key.Length == 0) key = "l";

                if (type == "pi")
                {
                    // Breadcrumb from a Property Inspector, for diagnostics.
                    Logger.Write("PI: " + note);
                }
                else if (type == "title")
                {
                    // Stream Deck refuses setTitle from a Property Inspector,
                    // so relay it as a proper plugin command over our session.
                    if (ctx.Length == 0)
                    {
                        Logger.Write("Title push ignored: no context given.");
                    }
                    else
                    {
                        bool pushed = StreamDeckClient.SendToApp(
                            "{\"event\":\"setTitle\",\"action\":\"" + StreamDeckClient.Escape(act) + "\"," +
                            "\"context\":\"" + StreamDeckClient.Escape(ctx) + "\"," +
                            "\"device\":\"" + StreamDeckClient.Escape(dev) + "\"," +
                            "\"payload\":{\"title\":\"" + StreamDeckClient.Escape(ttl) + "\",\"target\":\"action\"}}");
                        Logger.Write("Title push: \"" + (ttl.Length > 40 ? ttl.Substring(0, 40) : ttl) +
                            "\" " + (pushed ? "-> app" : "(no app link)"));
                    }
                }
                else if (type == "send")
                {
                    if (txt.Trim().Length == 0)
                    {
                        link.SendJson("{\"type\":\"ack\",\"ok\":false,\"error\":\"empty phrase\"}");
                    }
                    else
                    {
                        executor.Enqueue("phrase", txt, wpm, key);
                        link.SendJson("{\"type\":\"ack\",\"ok\":true}");
                    }
                }
                else if (type == "element")
                {
                    executor.Enqueue(evt == "dash" ? "dash" : "dot", "", wpm, key);
                    link.SendJson("{\"type\":\"ack\",\"ok\":true}");
                }
                else if (type == "toggle")
                {
                    executor.Enqueue(evt == "off" ? "off" : "on", "", wpm, key);
                    link.SendJson("{\"type\":\"ack\",\"ok\":true}");
                }
                else if (type == "cooldown")
                {
                    // Preview: play the recharge ring on the real deck key with
                    // no keystrokes at all, so the timing can be judged outside
                    // the game.
                    if (ctx.Length == 0)
                    {
                        Logger.Write("Cooldown preview ignored: no context given.");
                    }
                    else
                    {
                        int ms = cooldownMs > 0 ? cooldownMs
                                                : LampExecutor.EstimateMs("phrase", txt, wpm);
                        Logger.Write("Cooldown preview: " + ms + " ms on " +
                                     StreamDeckClient.ShortId(ctx));
                        StreamDeckClient.BeginCooldown(ctx, dev, ms);
                        link.SendJson("{\"type\":\"ack\",\"ok\":true}");
                    }
                }
                else if (type == "stop")
                {
                    executor.StopAll("PI stop button");
                    link.SendJson("{\"type\":\"ack\",\"ok\":true}");
                }
                else if (type == "detect")
                {
                    KeybindDetect.Result dr = KeybindDetect.Find();
                    link.SendJson("{\"type\":\"detect\",\"ok\":" + (dr.Ok ? "true" : "false") +
                        ",\"key\":\"" + StreamDeckClient.Escape(dr.Key) +
                        "\",\"where\":\"" + StreamDeckClient.Escape(dr.Where) + "\"}");
                }
                else if (type == "hello" || type == "ping")
                {
                    link.SendJson("{\"type\":\"hello\",\"name\":\"aldis-lamp\",\"version\":\"1.0\",\"port\":" +
                        ((IPEndPoint)listener.LocalEndpoint).Port + "}");
                }
                // unknown types: ignore
            }
            catch (Exception ex)
            {
                link.SendJson("{\"type\":\"ack\",\"ok\":false,\"error\":\"" + Escape(ex.Message) + "\"}");
            }
        }

        public void BroadcastJson(string json)
        {
            lock (clientsGate)
                foreach (ClientLink c in clients)
                    try { c.SendJson(json); } catch { }
        }

        private static string ReadFrame(NetworkStream stream)
        {
            int b0 = stream.ReadByte();
            if (b0 < 0) return null;
            int b1 = stream.ReadByte();
            if (b1 < 0) return null;
            long len = b1 & 0x7F;
            if (len == 126)
            {
                int hi = stream.ReadByte(), lo = stream.ReadByte();
                if (hi < 0 || lo < 0) return null;
                len = (hi << 8) | lo;
            }
            else if (len == 127)
            {
                len = 0;
                for (int n = 0; n < 8; n++)
                {
                    int q = stream.ReadByte();
                    if (q < 0) return null;
                    len = (len << 8) | (long)(uint)(q & 0xFF);
                }
            }
            if (len > 1048576) return null;
            bool masked = (b1 & 0x80) != 0;
            byte[] mask = null;
            if (masked)
            {
                mask = new byte[4];
                if (!ReadExact(stream, mask, 4)) return null;
            }
            byte[] payload = new byte[len];
            if (len > 0 && !ReadExact(stream, payload, (int)len)) return null;
            if (masked)
                for (int n = 0; n < payload.Length; n++) payload[n] ^= mask[n % 4];
            if ((b0 & 0x0F) == 0x8) return null; // close
            return Encoding.UTF8.GetString(payload);
        }

        private static bool ReadExact(Stream s, byte[] buf, int count)
        {
            int off = 0;
            while (off < count)
            {
                int n = s.Read(buf, off, count - off);
                if (n <= 0) return false;
                off += n;
            }
            return true;
        }

        private static string Escape(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }

    internal sealed class ClientLink
    {
        private readonly WebSocketServer server;
        private readonly TcpClient socket;
        private readonly NetworkStream stream;
        private readonly object sendGate = new object();

        public ClientLink(WebSocketServer server, TcpClient socket, NetworkStream stream)
        {
            this.server = server;
            this.socket = socket;
            this.stream = stream;
        }

        public void SendJson(string json)
        {
            SendRaw(0x81, Encoding.UTF8.GetBytes(json));
        }

        public void SendRaw(byte opcode, byte[] payload)
        {
            List<byte> frame = new List<byte>();
            frame.Add(opcode); // FIN set, server frames are unmasked
            if (payload.Length < 126)
            {
                frame.Add((byte)payload.Length);
            }
            else if (payload.Length <= 0xFFFF)
            {
                frame.Add(126);
                frame.Add((byte)(payload.Length >> 8));
                frame.Add((byte)(payload.Length & 0xFF));
            }
            else
            {
                frame.Add(127);
                ulong len2 = (ulong)payload.Length;
                for (int n = 7; n >= 0; n--) frame.Add((byte)((len2 >> (n * 8)) & 0xFF));
            }
            frame.AddRange(payload);
            byte[] bytes = frame.ToArray();
            lock (sendGate)
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush();
            }
        }

        public void CloseSocket()
        {
            try { socket.Close(); } catch { }
        }
    }
}
