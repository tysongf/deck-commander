# AjazzDeck

C# driver for the **Ajazz / Mirabox "Stream Dock" family** of macro pads, written for
driving events in an IL-2 mission-execution tool.

No vendor SDK, no NuGet packages, no driver install. It talks to the stock Microsoft
`HidUsb` driver through `hid.dll` / `setupapi.dll` P/Invoke.

## Quick start

**Needs:** Windows, and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
Nothing else. No driver to install, and the one NuGet package restores itself.

```
git clone <this repo>
cd ajazz-deck
dotnet build AjazzDeck.sln -c Release
```

That builds all four projects. Check it works with no hardware at all:

```
src\DeckProbe\bin\Release\net8.0-windows\DeckProbe.exe --selftest
```

45 models should pass and it should exit 0.

### Run the editor

```
src\DeckCommander\bin\Release\net8.0-windows\DeckCommander.exe
```

- **No deck?** Pick a model from the **Deck** dropdown and press **Virtual deck**. You
  get a working deck on screen that fires the same events as the hardware.
- **Deck plugged in?** Press **Connect deck**. It identifies the model itself and
  overrides the dropdown. Edits push to the keys as you type.
- **Open...** then `profiles/il2-korea.json` for a worked seven-page example.

### Drive a deck from your own code

Reference `src/AjazzDeck.Core`, which has no UI dependency, and handle one event:

```csharp
using var deck = AjazzStreamDock.OpenFirst();       // or: new VirtualDeck(DeckModel.Default)
using var ctrl = new DeckController(deck);

ctrl.ActionTriggered += (s, e) => mission.TriggerEvent(e.EventName);

ctrl.Start();                                        // REQUIRED, see below
ctrl.Profile = ProfileStore.Load("profiles/il2-korea.json", deck.Model);
```

`ActionTriggered` fires on the reader thread, so marshal with `Invoke` or
`Dispatcher.Invoke` before touching UI. Swap `AjazzStreamDock` for `VirtualDeck` and
everything above still works with no deck attached.

### Visual Studio

Open `AjazzDeck.sln`. Set **DeckCommander** as the startup project.

### If nothing happens when you press a key

The deck ships in standalone mode and stays silent until the host initialises it. That
is what `Initialize()` and `DeckController.Start()` do. See
[The one thing that matters](#the-one-thing-that-matters).

## Supported devices

45 devices across four hardware families, all sharing one protocol. `DeckModel` holds
the catalogue; `DeckProbe.exe --models` prints it.

| Family | Grid | Knobs | Protocol | Key image |
| --- | --- | --- | --- | --- |
| AKP153 / HSV293S | 3 x 6 (18 keys) | none | v1 and v3 | 85px (v1), 95px with an 82px sixth column (v3) |
| AKP03 / N3 | 3 x 3 (6 screens + 3 plain buttons) | 3 | v2 and v3 | 60px (v2), 64px (v3) |
| AKP05 / N4 | 2 x 5 (10 keys) | 4, each with a 176x112 screen | v3 | 112px |
| Mirabox 293 V3 | 3 x 5 (15 keys) | none | v3 | 112px |

Ajazz-branded: AKP153, AKP153E, AKP153R (and both rev. 2 boards), AKP03, AKP03E,
AKP03R (and rev. 2), AKP05, AKP05E, AKP05E Pro, AKP05CN Pro. The same boards are sold
as Mirabox, Mars Gaming, VSDInside, Soomfon, Streonor, Redragon, TreasLin, Mad Dog,
Risemode, TMICE, Womier and Monstargear, and all of those are in the catalogue too.

> **Only the Ajazz AKP153E (`0300:1010`, protocol v1) has been verified against real
> hardware.** Every other entry is transcribed from the reference implementations listed
> under [References](#references). The geometry, protocol and key maps are faithful to
> those sources, but nobody has plugged the other 44 in and watched what happened.

Adding a device that is already in one of the families is one line in `DeckModel._all`.

## Device facts (verified on the AKP153E)

| Property | Value |
| --- | --- |
| USB | `VID_0300` / `PID_1010` |
| HID usage | page `0xFFA0`, usage `0x0001` (vendor-defined) |
| Driver | Microsoft `HidUsb` (generic) |
| Report size | 513 bytes in and out (1 report-ID byte + 512 payload) |
| Layout | 3 rows x 6 screens, but only 15 switches: the narrow sixth column displays and does not press |
| Protocol | Mirabox/Ajazz "Stream Dock" v1, ASCII `CRT` header |

The dock also enumerates a separate USB Mass Storage device for its internal image
store. It is not needed for button input; ignore it.

## The one thing that matters

**The deck sends nothing until the host initialises it.**

Straight after plugging in, the deck runs standalone: pressing a key toggles its own
screen and no HID input reports are produced. Sending `DIS` (wake) or `CONNECT`
(keepalive) alone does *not* change this. The activation sequence is:

```
DIS  ->  brightness 0  ->  clear all keys  ->  set brightness  ->  flush (STP)
```

That is what `Initialize()` does, and it must be called before key events arrive.
This cost several hours to find, so do not remove it.

## Usage

```csharp
using AjazzDeck;

using var deck = AjazzStreamDock.OpenFirst();

Console.WriteLine($"{deck.Model.Name}: {deck.Rows}x{deck.Columns}");

deck.KeyChanged += (s, e) =>
{
    // e.Key    grid index, row-major, top-left = 0
    // e.Row    0 = top
    // e.Column 0 = left
    Console.WriteLine($"pressed {e}");
};

// knob decks only
deck.EncoderChanged += (s, e) => Console.WriteLine($"{e}");

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
    [17] = () => mission.TriggerEvent("AbortMission"),
};

deck.KeyChanged += (s, e) =>
{
    if (actions.TryGetValue(e.Key, out var act)) act();
};
```

`KeyChanged` is raised on the reader thread. In WinForms/WPF marshal to the UI
thread with `Invoke` / `Dispatcher.Invoke` before touching controls.

Write code against `deck.Model` rather than against constants. Nothing about the grid
is universal across the family: `Rows`, `Columns`, `DisplayKeys`, `EncoderCount`,
`KeyPixels(grid)` and `Orientation` all vary.

Two separate questions get asked about a slot, and the answer is not the same one:

- `HasScreen(grid)` is false for the AKP03's three plain buttons. They press, but
  nothing can be drawn on them.
- `IsPressable(grid)` is false for the AKP153's narrow sixth column. Those three draw,
  but nothing happens when you touch them.

The second case is worth designing for rather than around. Three always-visible tiles
that no one can press by accident are a good place for status: mission phase, a clock,
connection state. Give them a label and a colour like any other key and leave the action
as `None`; page navigation skips them automatically and the page selector leaves them
showing while it is open.

## Key numbering

Every family numbers its keys differently, and two of them number *images* differently
from *presses*. `DeckModel` owns both mappings, so callers only ever use the row-major
grid index.

The AKP153 family numbers the main block column-major from the **right**, then appends
the narrow sixth column at the end: device key 1 is row 0 column 4, device key 13 is
the top-left, and device keys 16-18 are the sixth column top to bottom.

```
         col 0   col 1   col 2   col 3   col 4   col 5
row 0       0       1       2       3       4       5     (device 13 10  7  4  1 16)
row 1       6       7       8       9      10      11     (device 14 11  8  5  2 17)
row 2      12      13      14      15      16      17     (device 15 12  9  6  3 18)
```

The Mirabox 293 V3 is the awkward one: images are addressed row-major from the bottom
left, presses row-major from the top left. The middle row is identical either way,
which is what makes the asymmetry easy to miss.

Protocol v1 and v2 report a **single event per press** - there is no separate down/up,
so `e.Pressed` is always true and long-press is impossible. Protocol v3 reports both,
using payload byte 10 for the state; `StateOffset` is set from the model.

## Wire format

Host to device, zero padded. Payload is 512 bytes on protocol v1 and 1024 on v2/v3,
but the HID descriptor is authoritative and the driver uses whatever it reports.

```
byte 0      report id, always 0x00
bytes 1-5   "CRT" 0x00 0x00
bytes 6+    command
```

| Command | Bytes from offset 6 | Effect |
| --- | --- | --- |
| `LIG` | `4C 49 47 00 00 <0-100>` | screen brightness |
| `LBLIG` | `4C 42 4C 49 47 <0-100>` | knob LED brightness |
| `SETLB` | `53 45 54 4C 42 <r g b>...` | per-knob LED colours |
| `CLE` | `43 4C 45 00 00 00 <key+1 or 0xFF>` | clear one key / all keys |
| `STP` | `53 54 50` | flush pending writes |
| `BAT` | `42 41 54 00 00 <sizeHi> <sizeLo> <key+1>` | begin image upload |
| `DIS` | `44 49 53` | wake panel |
| `MOD` | `4D 4F 44 00 00 <0x30+mode>` | set device mode (some firmwares) |
| `CONNECT` | `43 4F 4E 4E 45 43 54` | keepalive |
| `HAN` | `48 41 4E` | sleep panel |

Device to host, key press:

```
00 41 43 4B 00 00 4F 4B 00 00 0D
   A  C  K        O  K        ^^ input code
```

Payload byte 9 (buffer index 10) is the input code. On the AKP153 and 293 V3 families
that is the hardware key number; the knob decks pack keys, knob presses and knob
rotation into the same byte, which `DeckModel.TryDecodeInput` unpacks.

Clearing every key needs an explicit `STP` on v2 and v3. The AKP03, AKP05 and 293 V3
drop off the bus after roughly 35 seconds of silence, so `Initialize()` starts a
keepalive thread for the models that need one.

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

A profile records the deck it was written for (`Model`, `Rows`, `Columns`). Loading it
onto a different deck remaps every key to the same row and column on the new grid, so a
3 x 5 profile opened on a 3 x 6 deck keeps its layout and gains an empty sixth column.
Keys that fall outside the smaller grid are dropped. Profiles written before any of this
existed carry no geometry and are assumed to be 3 x 5.

## Virtual deck

Press **Virtual deck** in Deck Commander to run the selected model on screen with no
hardware attached. It is a real `IDeckDevice`, so it goes through the same
`DeckController` as the metal: the same renderer paints it, page navigation works, and
clicking a key raises exactly the events the hardware would.

That makes three things possible without owning the device: building a profile for a
deck you do not have, developing and demonstrating a host application before any deck is
plugged in, and checking a model's key map and orientation, since an image that comes out
sideways on screen would come out sideways on the panel.

The window applies `DeckImage.InverseOrientation` before display, so it undoes the
pre-rotation exactly as the physical panel does rather than being handed an upright copy.
Brightness dims it. Keys with no switch under them are not clickable.

```csharp
using var deck = new VirtualDeck(DeckModel.Default);
using var ctrl = new DeckController(deck);
ctrl.ActionTriggered += (s, e) => Console.WriteLine(e.EventName);
ctrl.Start();
ctrl.Profile = ProfileStore.Load("profiles/il2-korea.json", deck.Model);

deck.Press(0);                       // as if someone pressed the top-left key
var art = deck.GetKeyBitmap(0);      // what that key is showing
```

## Adding another kind of device

`IDeckDevice` is the seam. `DeckController`, the renderer and the profile format know
nothing about USB; they only know that something has a `DeckModel`, accepts JPEGs by grid
index, and raises key events. `AjazzStreamDock` and `VirtualDeck` are the two
implementations.

An Elgato Stream Deck belongs here as a third one. It speaks a different wire protocol
(1024-byte image reports, key states delivered as a full bitmap rather than a single key
number) but the same shape of behaviour, and Elgato publish the HID protocol at
<https://docs.elgato.com/streamdeck/hid/intro/>. Worth noting that those devices report
press *and* release on every model, so long-press becomes possible, which protocol v1
Ajazz hardware cannot do.

## Projects

- `src/AjazzDeck` - device catalogue, `IDeckDevice`, HID transport, virtual deck, images
- `src/AjazzDeck.Core` - profiles, pages, key rendering, page navigation, action dispatch (no UI)
- `src/DeckCommander` - WPF editor and page switcher
- `src/DeckProbe` - diagnostic console app

`AjazzDeck.Core` has no UI dependency, so the runtime that drives the deck during a
mission does not need the editor. A host application should reference `AjazzDeck.Core`
and handle `DeckController.ActionTriggered` (and `EncoderTriggered` on a knob deck).

## Deck Commander

Left panel manages pages, the centre is a live preview of the deck, the right panel
edits the selected key. The preview is rendered with the same code that paints the
hardware, so it is genuinely what you get.

The **Deck** dropdown picks which device you are designing for, so a profile can be
built without the hardware present. Connecting a deck overrides the choice: the thing
that is actually plugged in wins. Knob decks get a strip of knobs under the grid, each
with separate events for turn left, turn right and press.

Press **Connect deck** to edit against real hardware: every change is pushed to the key
immediately, changing page in the editor changes the page on the deck, and pressing a
physical key selects it in the editor.

Profiles are JSON (`ProfileStore`), written through a temp file so a crash cannot leave
a half-written profile behind.

## Page navigation

Page navigation is optional, controlled by `DeckProfile.ReservePageColumn` and the
toolbar checkbox in Deck Commander.

**Off**: every key is an action key and pages are changed from Deck Commander.

**On**: the rightmost column becomes page controls. On a 3 x 6 deck that leaves 15
action keys per page:

| Key | Role |
| --- | --- |
| row 0, last col | page up |
| middle row, last col | page indicator, "3 of 7" |
| last row, last col | page down |

Pressing the indicator opens the page selector: the action keys become page tiles
(current page highlighted) and page up/down scroll through them a screenful at a time.
While the selector is open the indicator becomes a red CANCEL. Page up/down wrap around
at the ends.

A two-row deck such as the AKP05 has nowhere to put the indicator between the arrows, so
it gets page up and page down only and the selector is reachable from the app instead
(`DeckLayout.HasPageIndicator`).

### Global hotkeys

Deck Commander registers system-wide hotkeys, so they work while IL-2 has focus:

| Hotkey | Action |
| --- | --- |
| `Ctrl+Alt+PageUp` | previous page |
| `Ctrl+Alt+PageDown` | next page |

These matter most with the navigation column switched off, where the deck itself has no
page controls. Registration can fail if another application already owns the combination;
the status bar says so when that happens.

A page always stores every key definition regardless of the setting, so toggling the
navigation column never destroys keys.

## DeckProbe

```
DeckProbe.exe --models                 # print the device catalogue
DeckProbe.exe --selftest               # check every model without hardware
DeckProbe.exe --init --seconds 300     # initialise, then log key presses
DeckProbe.exe --init --imagegrid       # number every key, to check the key map
DeckProbe.exe --init --imagetest       # push all 8 orientations, to find the right one
DeckProbe.exe --init --blink           # brightness test, confirms the output path
DeckProbe.exe --brightness 80 --no-listen
```

`--selftest` runs all 45 models through a `VirtualDeck` and checks what can be checked
without the hardware: that both key maps are one to one, that navigation never lands on a
key with no switch, that every screen gets painted at the size the model claims, and that
pressing a key raises the event the profile asked for. It exits non-zero on failure, so
it belongs in a build script. It does not test the wire protocol; only real hardware can.

The probe prints a `[reader alive] cycles=.. timeouts=.. lastErr=..` heartbeat, which
distinguishes "device is silent" from "our reader is broken". It also lists
*unrecognised* vendor-defined HID interfaces and will open one as an AKP153E if nothing
in the catalogue is attached, which is how an unlisted model gets investigated.

## Bringing up an untested model

1. `--models` to confirm whether it is in the catalogue.
2. `--init --imagegrid`. If the keys read 1..n in reading order the key map is right.
   If they are scrambled, the map needs its own entry: note which physical key shows
   which number and build the table from that.
3. `--init --imagetest`. Whichever key shows an upright F with the white wedge in its
   top-left corner names the correct `Orientation`.
4. `--init --seconds 60` and press every key. The `==>` lines should walk the grid in
   order. Codes that do not decode print nothing, which means the input scheme is wrong.

## Status

Verified on AKP153E hardware: enumeration, model matching, init, brightness, clear,
key input and mapping, image upload to all 18 screens, and profile migration from the
old 3 x 5 layout.

The 3 x 6 geometry is confirmed on the metal. All eighteen screens accept and display an
image, and the three in the sixth column have no switches behind them, so the deck is
eighteen displays and fifteen keys. That is why `HasScreen` and `IsPressable` are
separate: on this hardware they genuinely disagree.

Key images must be pre-rotated (`DeckModel.Orientation`, 270 degrees on the AKP153),
and image slots use each family's own numbering, which `SetKeyImageJpeg` handles.

Verified without hardware, via `--selftest`: the key maps, layout rules, image sizes and
event dispatch for all 45 models.

Not verified: the wire protocol of every device other than the AKP153E, and in particular
the knob input decoding, the v3 state byte, encoder screens, the keepalive timing, and
the 293 V3's split image/press numbering. All of it is transcribed from working
implementations, none of it has been run against the metal.

Not yet done: binding actions to anything real. `DeckActionType` currently has only
`Event`, which raises `ActionTriggered` for a host application to handle.

## References

The device tables, protocol versions, image formats and key maps for everything other
than the AKP153E come from these projects:

- https://github.com/4ndv/mirajazz - the shared Rust library; protocol versions, packet sizes, command set
- https://github.com/4ndv/opendeck-akp153 - AKP153 / HSV293S family, key maps, 3 x 6 grid
- https://github.com/4ndv/opendeck-akp03 - AKP03 / N3 family, knob input codes
- https://github.com/ambiso/opendeck-akp05 - AKP05 / N4 family, encoder screens
- https://github.com/Grizbeard/opendeck-mirabox293v3 - 293 V3, split image/press numbering
- https://github.com/Uriziel01/Ajazz-AKP153-reverse-engineering - original AKP153 protocol notes
- https://gist.github.com/ZCube/430fab6039899eaa0e18367f60d36b3c
