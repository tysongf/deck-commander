## Downloads

| File | Needs | Size |
| --- | --- | --- |
| `DeckCommander-v{V}-win-x64.zip` | nothing, the runtime is included | about 67 MB |
| `DeckCommander-v{V}-win-x64-needs-dotnet8.zip` | [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) | under 1 MB |

Unzip anywhere and run `DeckCommander.exe`. There is no installer and no driver
to install: the deck talks to the stock Microsoft `HidUsb` driver.

Windows SmartScreen will warn that the publisher is unknown, because these
binaries are not code signed. `SHA256SUMS.txt` is attached if you want to check
what you downloaded.

## Getting started

- **No deck?** Pick a model from the **Deck** dropdown and press **Virtual deck**.
- **Deck plugged in?** Press **Connect deck**.
- **Open...** then `profiles/il2-korea.json` for a worked seven-page example.
- `DeckProbe.exe --selftest` checks all 45 models with no hardware attached.

## Which hardware this has actually been run on

Only the **Ajazz AKP153E** (`0300:1010`, protocol v1). The other 44 entries in
the catalogue are transcribed from the open source implementations credited in the
README. The geometry, protocol and key maps are faithful to those sources, but
nobody has plugged those devices in and watched what happened.

**If you own one of the other 44, you can settle it in two minutes:**

```
DeckProbe.exe --models
DeckProbe.exe --init --imagegrid
DeckProbe.exe --init --seconds 60
```

Open an issue with what you saw, working or not. That is how the catalogue stops
being a transcription.
