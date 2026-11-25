# Aldis Lamp — Stream Deck plugin for Star Citizen

A virtual [Aldis lamp](https://en.wikipedia.org/wiki/Aldis_lamp) for the
Stream Deck: one button signals the Star Citizen ship lights key in proper
Morse code (PARIS timing, adjustable WPM), with dedicated dot / dash and
lamp on / off buttons, plus a Stop button that cancels everything.

Windows only. Forked from the Key Inject plugin (same proven bridge
plumbing); installs side by side with it (separate UUID, separate bridge
port 12346, separate log).

## Install

1. Run `build.ps1` (uses the .NET Framework `csc.exe` and System.Drawing —
   no other tools required). It produces `AldisLamp.streamDeckPlugin`.
2. Open the bundle (double-click, or `streamdeck://app/openfile/<path>`).
   Allow community plugins if Stream Deck asks.

## Actions

| Action | UUID | What it does |
|---|---|---|
| **Aldis Lamp** | `com.sundowner.aldislamp.lamp` | Mode from the Property Inspector: Morse phrase, single dot, single dash, lamp on, lamp off. |
| **Aldis Stop** | `com.sundowner.aldislamp.stop` | Cancels the running send, clears the queue, and extinguishes the lamp if the bridge tracked it as lit. No Property Inspector. |

The two icons are the same fitting: the Lamp key is the signal lamp with its
shutter open and the lens lit, the Stop key is that lamp with the shutter
closed. Both are drawn by `tools/make-icons.ps1`.

The Lamp icon lifts the lamp slightly and paints a dark band along the bottom
of the key, edge to edge, so the Stream Deck title stays readable instead of
washing into the bright lens. The band is deliberately empty: the app draws the
title over exactly that area, so anything painted there — an earlier draft had
Morse marks in it — collides with the text. `tools/make-icons.ps1 -sets T7`
redraws the shipped icon; `-sets T5` is the same lamp with `.-` marks in the
band if you ever want them back.

The plugin-list icon (`plugin/assets/plugin.png`) is the same lamp sitting on a
dark plate, which is what the `P` designs are for: a key always sits on the
Stream Deck background, but the app draws this one wherever it likes, including
on light panels. `-sets P1,P2,P3,P4` drafts them, and `-sets P3 -pluginIcon`
installs the chosen one at the 96 px the manifest wants. `tools/icon-sizes.ps1
-Names P1-plate,P2-plate,P3-plate,P4-plate` draws any drafts at 96, 48 and 28 px
on both a dark and a light background — the hood arc and the small Morse marks
that read fine at 144 turn to mush by 48 px, which is why the shipped plate has
neither.

The game key (default `L`) is a **toggle**, so every lit Morse element is an
even pair of taps: tap (light on) → element length → tap (light out). The
lamp is back in its previous state after every element, so nothing can get
stuck lit from an interrupted send.

## Settings (per button)

- **mode**: `phrase` | `dot` | `dash` | `on` | `off`
- **text**: phrase (A–Z, 0–9 and `. , ? ' ! / ( ) & : = + - "` ; unknown characters are skipped, spaces = inter-word gap)
- **wpm**: 5–25 (PARIS standard: unit = 1200/wpm ms; dot = 1u, dash = 3u, intra-char 1u, inter-char 3u, inter-word 7u)
- **lampKey**: the single game key that toggles the lights (default `l`)
- **cooldownMs**: recharge time in ms; `0`/blank = **auto**, i.e. exactly as
  long as this send takes at the chosen WPM. Only meaningful for
  `phrase` / `dot` / `dash`.
- **Title**: auto title (`≡ WORDS 8w`, `•`, `—`, `ON`, `OFF`) or your own
- **wpmInTitle**: `true`/`false`, default `true` — whether the auto title
  carries the speed, so `≡ HEAVE TO 12w` rather than just `≡ HEAVE TO`. Tick
  box in the inspector (shown in `phrase` mode only); it changes the title the
  bridge writes for the key, and does nothing while the title field holds your
  own text.

**Detect** in the inspector scans the Star Citizen config folders
(`%LOCALAPPDATA%\Star Citizen`, `%USERPROFILE%\Stars Citizen`,
`%APPDATA%\Star Citizen`) for `*.keys` / `client.cfg` files mentioning
`light_master` / `lights` and reads the bound key. Best-effort; if it finds
nothing you can set the key manually.

### Signal library

The inspector carries 46 signals a WW2 warship would plausibly send, grouped in
a dropdown: **choosing one adds it to the phrase straight away** — it saves the
button and retitles the key in the same move, and shows a one-line meaning under
the list. **Add again** repeats the signal you picked, **Clear** empties the
phrase, and typing text that matches a known signal shows its meaning too. The
key title is the first two words of the phrase, so a longer one reads
`≡ HEAVE TO … 12w`.

Where the meanings come from, strongest first:

- **B.R. 232 Signal Card** (Admiralty, 1937) — the procedure signs a lamp
  operator really keyed: `BT` long break, `EEEEEE` erase, `IM` repeat, `RARAR`
  receipt, `&` further message, `VE` general call, `AA` unknown-ship call, and
  the rule that a word spelled letter-by-letter is bracketed by `ER`.
- **U.S. FTP 223A** (1945) and the **British–US BUSCI** escort instructions —
  the flashing-light call signs `PISTOL` and `LINK`, and the convoy vocabulary
  (`TANTIVY`, `TOASTY`, `SHILLY-SHALLY`, `PRESTO`…). That vocabulary is **radio
  / TBS voice code, not lamp** — the group is labelled so: FTP 223A §6103 keeps
  visual signalling to recognition and emergency identification in darkness.
- Distress work: `SSS` / `RRR` / `QQQ` / `AAA` as attack-type suffixes sent
  after `SOS`, `TTT` as the **safety** signal (1914 SOLAS — not "torpedo"),
  `XXX` as urgency in Morse.

Two groups are labelled as what they are: **Modern ICS (post-1969)** (`K3`, `Z`,
`O`, `W`…) which people wrongly quote as WW2, and **Unverified — no WW2 source**
for signals that circulate online (`CS`, `EI`, `RR`, `AB`, `EE`, `RD`). `RD` is
real in B.R. 232 but it is the red *pendant*, not something you key. The WW2
A–Z single-letter tables live in the *Admiralty Signal Book* and the *Allied
Naval Signal Book*, neither published online, so that category is left out
rather than guessed.

Also left out on purpose: B.R. 232 signs such as `INT`, `NO`, `FC`, `FN`, `PT`
and the executive/ending signs, because their code forms are not the letters —
keying "INT" would send the wrong thing.

The list is the `SIGNALS` array near the top of `plugin/property-inspector/pi.js`;
add your own entries there. The code has to use characters the bridge can key
(A–Z, 0–9 and the punctuation in `BuildMorse`).

Pressing any lamp button again while a send runs **cancels** it (single
lamp, last press wins). Stop also cancels and, if the bridge thinks the
lamp is lit from a previous **Lamp on** press, taps once to extinguish.

### Cooldown ring

After a `phrase` / `dot` / `dash` press the key plays a **draining ring**
until it is ready again, and presses while the ring is still running are
ignored (the send itself is not cut short). The length defaults to *auto*:
the bridge computes the send's own duration with the same PARIS arithmetic
it is about to perform, so the ring unwinds exactly while the lamp is
flashing — no guessing, and it follows the WPM slider. A number in the
Cooldown box forces a longer wait instead. Stop, or a new press that
replaces the send, ends the ring immediately rather than leaving a phantom
countdown; `Lamp on` / `Lamp off` have no ring because a toggle is one tap.

The ring is drawn as ~10 raster **PNG** frames per second (144×144, GDI+),
because the app accepts SVG data URIs over the socket but silently never
draws them; two pale **bars** sit in the middle of every frame, so the key
reads as waiting rather than ready. A **Preview** button in the inspector
plays the ring on the
real key without sending a single keystroke, so the timing can be judged
outside the game. The key's artwork is restored by resetting its state,
not by touching its title — the button's own title is never taken over.

One limit, checked on the real device: a key whose **icon you picked in
Stream Deck itself** ignores everything the plugin draws. Both pushed
frames and a manifest state switch left such a key untouched, because the
chosen icon is app-owned and takes priority over plugin images. The ring
therefore only appears on keys that still use this plugin's artwork (or
no image at all); the send itself works either way, only the recharge
animation is missing. Key Inject behaves identically for the same reason.

## How it works / files

- `src/Bridge.cs` — the plugin process (`windows/aldis-bridge.exe`):
  registers with Stream Deck over the app's WebSocket (v2 `registerPlugin`),
  executes physical button presses itself (`keyDown` + `payload.settings`,
  so buttons work with no inspector open), relays `setTitle` for the
  inspector (PIs may not send it), runs a WebSocket server for the
  inspector on **127.0.0.1:12346**, and injects keystrokes via `SendInput`.
- `plugin/` — manifest (2 actions), Property Inspector (self-contained,
  manual protocol: settings are stored by Stream Deck and re-read via
  `getSettings` on open), icons.
- `tools/make-icons.ps1` — draws the key artwork (the shuttered signal-lens
  lamp) with GDI+; drafts and a `preview.html` comparison page land in
  `tools/icon-drafts/`. Run it with `-sets B4,T7,S4` to redraw only some
  designs — `T7` is the lamp icon with the empty title band, `P3` is the plugin-list plate, installed with
   `-sets P3 -pluginIcon`, and `tools/icon-sizes.ps1` shows drafts at the small
   sizes the app uses. `build.ps1` keeps
  whatever is in `plugin/assets` and
  only falls back to its own simple placeholders when run with `-regenIcons`.
- Log: `%LOCALAPPDATA%\AldisLamp\bridge.log`.

### Inspector ⇄ bridge protocol (port 12346)

`{type:"pi",note}` breadcrumb · `{type:"send",text,wpm,key}` phrase test ·
`{type:"element",event:"dot"|"dash",wpm,key}` · `{type:"toggle",event:"on"|"off",key}` ·
`{type:"cooldown",context,device,cooldownMs}` preview ring on the real key ·
`{type:"stop"}` · `{type:"detect"}` → reply `{type:"detect",ok,key,where}` ·
`{type:"title",title,context,device,action}` (relayed to the app) ·
`hello` → ack. Unknown types are ignored. Test-pad commands carry no deck
context, so they never animate a key.

## Troubleshooting

- Nothing happens on press → check Stream Deck shows the plugin as loaded
  and look in `bridge.log` (look for `Deck press:` lines).
- Morse timing feels wrong → check **WPM**; at 8 WPM a dot is 150 ms.
- The lamp ends up lit after fiddling → press **Lamp off** or the **Stop**
  button.
