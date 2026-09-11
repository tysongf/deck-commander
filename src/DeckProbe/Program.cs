using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Drawing;
using AjazzDeck;
using AjazzDeck.Core;

class Program
{
    static Color FromHue(int hue)
    {
        double h = hue / 60.0, c = 0.62, x = c * (1 - Math.Abs(h % 2 - 1));
        double r = 0, g = 0, b = 0;
        if (h < 1) { r = c; g = x; } else if (h < 2) { r = x; g = c; }
        else if (h < 3) { g = c; b = x; } else if (h < 4) { g = x; b = c; }
        else if (h < 5) { r = x; b = c; } else { r = c; b = x; }
        return Color.FromArgb((int)((r + 0.15) * 255), (int)((g + 0.15) * 255), (int)((b + 0.15) * 255));
    }

    /// <summary>Loads a profile through the real deserialiser and reports what it contains.</summary>
    static int CheckProfile(string path)
    {
        DeckProfile profile;
        try { profile = ProfileStore.Load(path); }
        catch (Exception ex)
        {
            Console.WriteLine("FAILED to parse: " + ex.Message);
            return 1;
        }
        if (profile == null) { Console.WriteLine("File not found: " + path); return 1; }

        Console.WriteLine("Profile      : " + profile.Name);
        Console.WriteLine("Brightness   : " + profile.Brightness);
        Console.WriteLine("Nav column   : " + (profile.ReservePageColumn ? "reserved" : "not reserved"));
        Console.WriteLine("Pages        : " + profile.Pages.Count);
        Console.WriteLine();

        int configured = 0, withEvent = 0, withGlyph = 0;
        foreach (var page in profile.Pages)
        {
            int n = 0;
            foreach (var k in page.Keys)
            {
                if (k == null || k.IsEmpty) continue;
                n++; configured++;
                if (k.ActionType == DeckActionType.Event && !string.IsNullOrWhiteSpace(k.EventName)) withEvent++;
                if (k.Glyph != KeyGlyph.None) withGlyph++;
            }
            Console.WriteLine(string.Format("  {0,-18} {1,2} keys   keys[]={2}",
                page.Name, n, page.Keys.Count));
        }
        Console.WriteLine();
        Console.WriteLine(string.Format("configured keys: {0}, with an event: {1}, with a glyph: {2}",
            configured, withEvent, withGlyph));

        var first = profile.Pages[0];
        Console.WriteLine();
        Console.WriteLine("sample (page 1):");
        for (int g = 0; g < DeckLayout.TotalKeys; g++)
        {
            var k = first.Keys[g];
            if (k == null || k.IsEmpty) continue;
            Console.WriteLine(string.Format("  grid {0,2} (r{1} c{2})  {3,-22} {4,-9} {5}",
                g, DeckLayout.RowOf(g) + 1, DeckLayout.ColumnOf(g) + 1,
                (k.Label ?? "").Replace("\n", " "), k.Glyph, k.EventName));
        }
        return 0;
    }

    /// <summary>Runs the deck from a saved profile, printing every action.</summary>
    static int RunProfile(string path, int seconds)
    {
        var profile = ProfileStore.Load(path);
        if (profile == null) { Console.WriteLine("File not found: " + path); return 1; }

        using (var deck = AjazzStreamDock.OpenFirst())
        using (var ctrl = new DeckController(deck))
        {
            ctrl.ActionTriggered += (s, e) =>
                Console.WriteLine(string.Format("{0:HH:mm:ss}  {1}", DateTime.Now, e));
            ctrl.PageChanged += (s, e) =>
                Console.WriteLine(string.Format("{0:HH:mm:ss}  PAGE {1}/{2}  {3}",
                    DateTime.Now, ctrl.PageIndex + 1, ctrl.Profile.Pages.Count, ctrl.CurrentPage.Name));
            ctrl.Start();
            ctrl.Profile = profile;
            Console.WriteLine("Running '" + profile.Name + "' for " + seconds + "s.");
            System.Threading.Thread.Sleep(seconds * 1000);
        }
        return 0;
    }

    static int RunDemo(int seconds)
    {
        var profile = new DeckProfile { Name = "Demo", Brightness = 65 };
        profile.Pages.Clear();
        string[] pageNames = { "Startup", "Taxi", "Takeoff", "Nav", "Combat", "Landing", "Debrief" };
        for (int p = 0; p < pageNames.Length; p++)
        {
            var page = new DeckPage { Name = pageNames[p] };
            var grids = DeckLayout.ActionGrids(true);
            for (int k = 0; k < grids.Length; k++)
            {
                page.Keys[grids[k]] = new KeyDefinition
                {
                    Label = pageNames[p].Substring(0, 3) + "\n" + (k + 1),
                    Background = Hex(FromHue(((p * 47) + k * 13) % 360)),
                    ActionType = DeckActionType.Event,
                    EventName = pageNames[p] + "." + (k + 1)
                };
            }
            profile.Pages.Add(page);
        }

        using (var deck = AjazzStreamDock.OpenFirst())
        using (var ctrl = new DeckController(deck))
        {
            ctrl.ActionTriggered += (s, e) =>
                Console.WriteLine(string.Format("{0:HH:mm:ss}  ACTION  {1}", DateTime.Now, e));
            ctrl.PageChanged += (s, e) =>
                Console.WriteLine(string.Format("{0:HH:mm:ss}  PAGE    {1} / {2}  ({3})",
                    DateTime.Now, ctrl.PageIndex + 1, ctrl.Profile.Pages.Count, ctrl.CurrentPage.Name));

            ctrl.Start();
            ctrl.Profile = profile;

            Console.WriteLine();
            Console.WriteLine("DEMO: 7 pages, 12 action keys each.");
            Console.WriteLine("  right column: top = page up, middle = page indicator, bottom = page down");
            Console.WriteLine("  press the indicator to open the page selector, press it again to cancel");
            Console.WriteLine();
            System.Threading.Thread.Sleep(seconds * 1000);
        }
        return 0;
    }

    static string Hex(Color c)
    {
        return string.Format("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
    }

    static int Main(string[] args)
    {
        var sw = new StreamWriter(Console.OpenStandardOutput());
        sw.AutoFlush = true;
        Console.SetOut(sw);

        int seconds = 300;
        bool wake = false, connect = false, blink = false, clear = false, listen = true, init = false;
        bool imagetest = false, imagegrid = false, demo = false;
        string checkProfile = null, profilePath = null;
        int brightness = -1;
        int keepaliveMs = 0;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--seconds":    seconds = int.Parse(args[++i]); break;
                case "--brightness": brightness = int.Parse(args[++i]); break;
                case "--wake":       wake = true; break;
                case "--connect":    connect = true; break;
                case "--blink":      blink = true; break;
                case "--clear":      clear = true; break;
                case "--init":       init = true; break;
                case "--imagetest":  imagetest = true; break;
                case "--imagegrid":  imagegrid = true; break;
                case "--demo":       demo = true; break;
                case "--checkprofile": checkProfile = args[++i]; break;
                case "--profile":    profilePath = args[++i]; break;
                case "--no-listen":  listen = false; break;
                case "--keepalive":  keepaliveMs = int.Parse(args[++i]); break;
            }
        }

        if (checkProfile != null) return CheckProfile(checkProfile);
        if (profilePath != null) return RunProfile(profilePath, seconds);
        if (demo) return RunDemo(seconds);

        var decks = AjazzStreamDock.Enumerate();
        Console.WriteLine("Ajazz HID interfaces found: " + decks.Count);
        foreach (var d in decks) Console.WriteLine("  " + d);
        if (decks.Count == 0) return 1;

        DeckInfo pick = null;
        foreach (var d in decks)
            if (d.UsagePage >= 0xFF00 && d.OutputReportLength > 0) { pick = d; break; }
        if (pick == null) pick = decks[0];
        Console.WriteLine("Using: " + pick);
        Console.WriteLine();

        using (var deck = AjazzStreamDock.Open(pick))
        {
            deck.RawReport += report =>
            {
                int last = report.Length - 1;
                while (last > 0 && report[last] == 0) last--;
                var hex = new StringBuilder();
                var asc = new StringBuilder();
                for (int i = 0; i <= last && i < 32; i++)
                {
                    hex.Append(report[i].ToString("X2")).Append(' ');
                    asc.Append(report[i] >= 32 && report[i] < 127 ? (char)report[i] : '.');
                }
                var nz = new StringBuilder();
                for (int i = 0; i <= last; i++)
                    if (report[i] != 0) nz.Append("[" + i + "]=" + report[i] + " ");
                Console.WriteLine(string.Format("{0:HH:mm:ss.fff} INPUT len={1} | {2}| {3}",
                    DateTime.Now, report.Length, hex, asc));
                Console.WriteLine("            nonzero: " + nz);
            };
            string[] names = { "top-left", "top row 2", "top-middle", "top row 4", "top-right",
                               "mid-left", "mid row 2", "centre",     "mid row 4", "mid-right",
                               "bot-left", "bot row 2", "bot-middle", "bot row 4", "bot-right" };
            deck.KeyChanged += (s, e) =>
                Console.WriteLine(string.Format("            ==> {0}  [{1}]", e, names[e.Key]));

            if (listen) deck.Start();

            Action<string, Action> step = (label, act) =>
            {
                try { act(); Console.WriteLine("OK   " + label); }
                catch (Exception ex) { Console.WriteLine("FAIL " + label + ": " + ex.Message); }
            };

            if (init)    step("INIT: brightness 50 + clear all + STP", () => deck.Initialize(50));
            if (wake)    step("CRT..DIS  (wake)", deck.Wake);
            if (connect) step("CRT..CONNECT (keepalive)", deck.KeepAlive);
            if (clear)   step("CRT..CLE all + STP", () => { deck.ClearAll(); deck.Commit(); });

            if (imagegrid)
            {
                Console.WriteLine();
                Console.WriteLine("Numbering all 15 keys in reading order...");
                for (int i = 0; i < AjazzStreamDock.KeyCount; i++)
                {
                    int hue = (i * 24) % 360;
                    using (var tile = DeckImage.MakeTestTile((i + 1).ToString(), FromHue(hue)))
                        deck.SetKeyImageJpeg(i, DeckImage.ToKeyJpeg(tile));
                    Thread.Sleep(100);
                }
                Console.WriteLine("Keys should now read 1-15 left to right, top to bottom.");
                Console.WriteLine();
            }

            if (imagetest)
            {
                // One glyph, all eight possible orientations, one per key.
                // Row 0 cols 0-3 = variants 1-4, row 1 cols 0-3 = variants 5-8.
                int[] slots = { 0, 1, 2, 3, 5, 6, 7, 8 };
                Color[] colours =
                {
                    Color.FromArgb(200,40,40),   Color.FromArgb(40,140,60),
                    Color.FromArgb(40,80,190),   Color.FromArgb(170,110,20),
                    Color.FromArgb(130,40,160),  Color.FromArgb(20,140,150),
                    Color.FromArgb(150,60,90),   Color.FromArgb(70,90,40)
                };
                Console.WriteLine();
                Console.WriteLine("Pushing 8 orientation variants of the letter F:");
                for (int v = 0; v < 8; v++)
                {
                    int key = slots[v];
                    var orient = DeckImage.AllOrientations[v];
                    try
                    {
                        using (var tile = DeckImage.MakeTestTile("F", colours[v]))
                        {
                            byte[] jpeg = DeckImage.ToKeyJpeg(tile, orient);
                            deck.SetKeyImageJpeg(key, jpeg);
                            Console.WriteLine(string.Format(
                                "  variant {0} -> key {1} (row {2}, col {3})  {4}  {5} bytes",
                                v + 1, key, key / 5, key % 5, orient, jpeg.Length));
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("  variant " + (v + 1) + " FAILED: " + ex.Message);
                    }
                    Thread.Sleep(120);
                }
                Console.WriteLine();
                Console.WriteLine("Which key shows an upright, correctly readable F");
                Console.WriteLine("with the white wedge in its TOP-LEFT corner?");
                Console.WriteLine();
            }

            if (blink)
            {
                Console.WriteLine();
                Console.WriteLine(">>> WATCH THE DECK SCREEN NOW <<<");
                for (int round = 0; round < 3; round++)
                {
                    step("brightness 0   (should go DARK)",   () => deck.SetBrightness(0));
                    Thread.Sleep(1500);
                    step("brightness 100 (should go BRIGHT)", () => deck.SetBrightness(100));
                    Thread.Sleep(1500);
                }
                step("brightness 50 (restore)", () => deck.SetBrightness(50));
                Console.WriteLine(">>> END OF BLINK TEST <<<");
                Console.WriteLine();
            }
            else if (brightness >= 0)
            {
                step("brightness " + brightness, () => deck.SetBrightness(brightness));
            }

            if (listen)
            {
                Console.WriteLine("Listening " + seconds + "s. PRESS DECK BUTTONS ANY TIME."
                    + (keepaliveMs > 0 ? "  (CONNECT every " + keepaliveMs + "ms)" : ""));
                var end = DateTime.Now.AddSeconds(seconds);
                var nextBeat = DateTime.Now.AddSeconds(10);
                while (DateTime.Now < end)
                {
                    Thread.Sleep(keepaliveMs > 0 ? keepaliveMs : 500);
                    if (keepaliveMs > 0) { try { deck.KeepAlive(); } catch { } }
                    if (DateTime.Now >= nextBeat)
                    {
                        nextBeat = DateTime.Now.AddSeconds(10);
                        Console.WriteLine(string.Format(
                            "  [reader alive] cycles={0} timeouts={1} lastErr={2}",
                            deck.ReadCycles, deck.ReadTimeouts, deck.LastReadError));
                    }
                }
                deck.Stop();
            }
        }
        Console.WriteLine("done.");
        return 0;
    }
}








