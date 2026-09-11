# AjazzDeck

Minimal C# driver for the **Ajazz AKP153E Stream Dock** (`VID 0x0300 / PID 0x1010`),
written for driving events in an IL-2 mission-execution tool.

No vendor SDK, no NuGet packages, no driver install. It talks to the stock Microsoft
`HidUsb` driver through `hid.dll` / `setupapi.dll` P/Invoke.

## Device facts (verified on this hardware)

| Property | Value |
| --- | --- |
| USB | `VID_0300` / `PID_1010`, serial `7ED3A3113555` |
| HID usage | page `0xFFA0`, usage `0x0001` (vendor-defined) |
| Driver | Microsoft `HidUsb` (generic) |
| Report size | 513 bytes in and out (1 report-ID byte + 512 payload) |
| Layout | 3 rows x 5 keys |
| Protocol | Mirabox/Ajazz "Stream Dock" v1, ASCII `CRT` header |

The dock also enumerates a separate USB Mass Storage device (`VID_346D&PID_5678`)
for its internal image store. It is not needed for button input; ignore it.

## The one thing that matters

**The deck sends nothing until the host initialises it.**

Straight after plugging in, the deck runs standalone: pressing a key toggles its own
screen and no HID input reports are produced. Sending `DIS` (wake) or `CONNECT`
(keepalive) does *not* change this. The activation sequence is:

```
set brightness  ->  clear all keys  ->  flush (STP)
```

That is what `Initialize()` does, and it must be called before key events arrive.
This cost several hours to find, so do not remove it.

## Usage

```csharp
using AjazzDeck;

using var deck = AjazzStreamDock.OpenFirst();

deck.KeyChanged += (s, e) =>
{
    // e.Key    0-14, row-major, top-left = 0
    // e.Row    0-2, 0 = top
    // e.Column 0-4, 0 = left
    Console.WriteLine($"pressed {e}");
};

deck.Initialize();   // REQUIRED - without this the deck stays silent
deck.Start();        // begins the background reader thread

Console.ReadLine();
```

Wiring keys to mission events is then just a dispatch table:

```csharp
var actions = new Dictionary<int, Action>
{
    [0]  = () => mission.TriggerEvent("StartMission"),
    [1]  = () => mission.TriggerEvent("SpawnFlight"),
    [14] = () => mission.TriggerEvent("AbortMission"),
};

deck.KeyChanged += (s, e) =>
{
    if (actions.TryGetValue(e.Key, out var act)) act();
};
```

`KeyChanged` is raised on the reader thread. In WinForms/WPF marshal to the UI
thread with `Invoke` / `Dispatcher.Invoke` before touching controls.

## Key numbering

The hardware numbers keys column-major from the **right**: device key 1 is the
top-right key, 13 is the top-left. The library converts this to a natural
row-major index, so you can ignore the hardware numbering.

```
          col 0   col 1   col 2   col 3   col 4
row 0        0       1       2       3       4      (device 13 10  7  4  1)
row 1        5       6       7       8       9      (device 14 11  8  5  2)
row 2       10      11      12      13      14      (device 15 12  9  6  3)
```

Protocol v1 reports a **single event per press** - there is no separate down/up.
`e.Pressed` is therefore always true. (`StateOffset` is set to -1 for this reason;
protocol v3 devices use byte 10 for state.)

## Wire format

Host to device, 513 bytes, zero padded:

```
byte 0      report id, always 0x00
bytes 1-5   "CRT" 0x00 0x00
bytes 6+    command
```

| Command | Bytes from offset 6 | Effect |
| --- | --- | --- |
| `LIG` | `4C 49 47 00 00 <0-100>` | screen brightness |
| `CLE` | `43 4C 45 00 00 00 <key+1 or 0xFF>` | clear one key / all keys |
| `STP` | `53 54 50` | flush pending writes |
| `BAT` | `42 41 54 00 00 <sizeHi> <sizeLo> <key+1>` | begin image upload |
| `DIS` | `44 49 53` | wake panel |
| `CONNECT` | `43 4F 4E 4E 45 43 54` | keepalive |
| `HAN` | `48 41 4E` | sleep panel |

Device to host, key press:

```
00 41 43 4B 00 00 4F 4B 00 00 0D
   A  C  K        O  K        ^^ device key, 1-15
```

Byte 10 of the buffer (payload byte 9) is the key number.

## Profiles

`profiles/il2-korea.json` is a worked flight-sim profile: 7 pages, 84 keys.

| Page | Covers |
| --- | --- |
| Engine Start | master power, fuel pump, magnetos, starter, lights, chocks, shutdown |
| Taxi + Takeoff | brakes, flaps, gear, throttle, prop pitch, mixture, trim |
| Navigation | map, waypoints, autopilot, compass, radio, course hold |
| Combat | guns, cannons, rockets, bombs, master arm, drop tanks, bail out |
| Landing | gear down, landing flaps, airbrake, prop full, engine off |
| Views | cockpit, external, padlock, look directions, zoom |
| Mission Control | start, pause, stop, phase stepping, spawn, mark objective, debrief |

Open it with **Open...** in Deck Commander, or run it headless:

```
DeckProbe.exe --profile profiles/il2-korea.json --seconds 300
DeckProbe.exe --checkprofile profiles/il2-korea.json
```

Event names such as `Engine.Starter` and `Mission.Start` are a starting vocabulary. They
are strings passed through `ActionTriggered`; the host application decides what each one
does, so rename them to match whatever your application expects.

## Projects

- `src/AjazzDeck` - hardware layer: HID transport, key input, brightness, images
- `src/AjazzDeck.Core` - profiles, pages, key rendering, page navigation, action dispatch (no UI)
- `src/DeckCommander` - WPF editor and page switcher
- `src/DeckProbe` - diagnostic console app

`AjazzDeck.Core` has no UI dependency, so the runtime that drives the deck during a
mission does not need the editor. Your friend''s application should reference
`AjazzDeck.Core` and handle `DeckController.ActionTriggered`.

## Deck Commander

```
dotnet build src/DeckCommander/DeckCommander.csproj -c Release
```

Left panel manages pages, the centre is a live preview of the deck, the right panel
edits the selected key. The preview is rendered with the same code that paints the
hardware, so it is genuinely what you get.

Press **Connect deck** to edit against real hardware: every change is pushed to the key
immediately, changing page in the editor changes the page on the deck, and pressing a
physical key selects it in the editor.

Profiles are JSON (`ProfileStore`), written through a temp file so a crash cannot leave
a half-written profile behind.

## Page navigation

Page navigation is optional, controlled by `DeckProfile.ReservePageColumn` and the
toolbar checkbox in Deck Commander.

**Off**: all 15 keys are action keys and pages are changed from Deck Commander.

**On**: the rightmost column becomes page controls, leaving a 4x3 block of 12 action
keys per page:

| Key | Role |
| --- | --- |
| row 0, col 4 | page up |
| row 1, col 4 | page indicator, "3 of 7" |
| row 2, col 4 | page down |

Pressing the indicator opens the page selector: the 12 action keys become page tiles
(current page highlighted) and page up/down scroll through them 12 at a time. While the
selector is open the indicator becomes a red CANCEL. Page up/down wrap around at the ends.

### Global hotkeys

Deck Commander registers system-wide hotkeys, so they work while IL-2 has focus:

| Hotkey | Action |
| --- | --- |
| `Ctrl+Alt+PageUp` | previous page |
| `Ctrl+Alt+PageDown` | next page |

These matter most with the navigation column switched off, where the deck itself has no
page controls. Registration can fail if another application already owns the combination;
the status bar says so when that happens.

A page always stores all 15 key definitions regardless of the setting, so toggling the
navigation column never destroys keys. Profiles saved with the older 12-entry layout are
migrated on load.

```
dotnet build src/DeckProbe/DeckProbe.csproj -c Release

DeckProbe.exe --init --seconds 300     # initialise, then log key presses
DeckProbe.exe --init --blink           # brightness test, confirms the output path
DeckProbe.exe --brightness 80 --no-listen
```

The probe prints a `[reader alive] cycles=.. timeouts=.. lastErr=..` heartbeat, which
distinguishes "device is silent" from "our reader is broken".

## Status

Verified working: enumeration, init, brightness, clear, key input and mapping for all
15 keys.

Key images are verified working. Images must be pre-rotated 270 degrees
(`DeckImage.DefaultOrientation`), and image slots use the same column-major-from-the-right
numbering as key input, which `SetKeyImageJpeg` handles for you.

Not yet done: binding actions to anything real. `DeckActionType` currently has only
`Event`, which raises `ActionTriggered` for a host application to handle.

## References

- https://github.com/Uriziel01/Ajazz-AKP153-reverse-engineering
- https://github.com/4ndv/mirajazz
- https://github.com/4ndv/opendeck-akp153
- https://gist.github.com/ZCube/430fab6039899eaa0e18367f60d36b3c




