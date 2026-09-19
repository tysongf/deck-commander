using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AjazzDeck.Core
{
    /// <summary>Crude built-in placeholder art, so a key can be recognisable without an image file.</summary>
    public enum KeyGlyph
    {
        None = 0, Circle, Square, Triangle, Diamond, Cross, Check,
        ArrowUp, ArrowDown, ArrowLeft, ArrowRight,
        Play, Stop, Pause, Star, Warning, Power, Target
    }

    public enum DeckActionType
    {
        None = 0,
        /// <summary>Raises a named event for the host application to handle.</summary>
        Event = 1
    }

    /// <summary>One configurable key on a page.</summary>
    public sealed class KeyDefinition
    {
        public string Label { get; set; } = "";
        /// <summary>Absolute or profile-relative path to an image, optional.</summary>
        public string ImagePath { get; set; }
        /// <summary>Background colour as #RRGGBB.</summary>
        public string Background { get; set; } = "#2A2E37";
        public KeyGlyph Glyph { get; set; } = KeyGlyph.None;
        public DeckActionType ActionType { get; set; } = DeckActionType.None;
        /// <summary>Event name handed to the host application, e.g. "AbortMission".</summary>
        public string EventName { get; set; } = "";

        public bool IsEmpty
        {
            get
            {
                return string.IsNullOrWhiteSpace(Label)
                    && string.IsNullOrWhiteSpace(ImagePath)
                    && Glyph == KeyGlyph.None
                    && ActionType == DeckActionType.None;
            }
        }

        public KeyDefinition Clone()
        {
            return (KeyDefinition)MemberwiseClone();
        }
    }

    /// <summary>What a knob does. Turning and pressing are configured separately.</summary>
    public sealed class EncoderDefinition
    {
        public string Label { get; set; } = "";
        public string TurnLeftEvent { get; set; } = "";
        public string TurnRightEvent { get; set; } = "";
        public string PressEvent { get; set; } = "";

        public bool IsEmpty
        {
            get
            {
                return string.IsNullOrWhiteSpace(Label)
                    && string.IsNullOrWhiteSpace(TurnLeftEvent)
                    && string.IsNullOrWhiteSpace(TurnRightEvent)
                    && string.IsNullOrWhiteSpace(PressEvent);
            }
        }

        public EncoderDefinition Clone() { return (EncoderDefinition)MemberwiseClone(); }
    }

    /// <summary>
    /// A page of keys. <see cref="Keys"/> holds one entry per grid slot, indexed by
    /// position (top-left = 0, reading order), whether or not the navigation column is
    /// reserved. Storing every slot means toggling the navigation column never loses a key.
    /// </summary>
    public sealed class DeckPage
    {
        public string Name { get; set; } = "Page";
        public List<KeyDefinition> Keys { get; set; } = new List<KeyDefinition>();
        public List<EncoderDefinition> Encoders { get; set; } = new List<EncoderDefinition>();

        public static List<KeyDefinition> NewKeys(int count)
        {
            var list = new List<KeyDefinition>(count);
            for (int i = 0; i < count; i++) list.Add(new KeyDefinition());
            return list;
        }

        /// <summary>
        /// Resizes the page to the target grid, moving each key to the same row and column
        /// it occupied on the source grid. Keys that fall outside the new grid are dropped;
        /// slots the old deck did not have come up empty.
        /// </summary>
        public void Normalise(int sourceRows, int sourceColumns, int rows, int columns, int encoders)
        {
            int total = rows * columns;

            if (Keys == null) Keys = NewKeys(total);
            else
            {
                for (int i = 0; i < Keys.Count; i++)
                    if (Keys[i] == null) Keys[i] = new KeyDefinition();

                if (sourceColumns != columns || Keys.Count != total)
                {
                    var grid = NewKeys(total);
                    for (int slot = 0; slot < Keys.Count; slot++)
                    {
                        int row = slot / sourceColumns, col = slot % sourceColumns;
                        if (row >= rows || col >= columns) continue;
                        grid[row * columns + col] = Keys[slot];
                    }
                    Keys = grid;
                }
            }

            if (Encoders == null) Encoders = new List<EncoderDefinition>();
            while (Encoders.Count < encoders) Encoders.Add(new EncoderDefinition());
            while (Encoders.Count > encoders) Encoders.RemoveAt(Encoders.Count - 1);
            for (int i = 0; i < Encoders.Count; i++)
                if (Encoders[i] == null) Encoders[i] = new EncoderDefinition();
        }
    }

    public sealed class DeckProfile
    {
        public string Name { get; set; } = "Default";
        public int Brightness { get; set; } = 60;

        /// <summary>
        /// The deck this profile was last saved for, as "vvvv:pppp". A profile still loads
        /// onto a different deck; the pages are remapped to the new grid on the way in.
        /// </summary>
        public string Model { get; set; }

        /// <summary>
        /// Grid the pages are stored in. Written on save so a profile can be remapped
        /// correctly later; inferred from the key count when absent.
        /// </summary>
        public int Rows { get; set; }
        public int Columns { get; set; }

        /// <summary>
        /// When true the rightmost column becomes page up / indicator / page down. When
        /// false every key is usable and pages are changed from Deck Commander.
        /// </summary>
        public bool ReservePageColumn { get; set; } = true;

        public List<DeckPage> Pages { get; set; } = new List<DeckPage> { new DeckPage { Name = "Page 1" } };

        /// <summary>Resizes every page to the given deck, remapping by row and column.</summary>
        public void Normalise(DeckModel target)
        {
            if (target == null) target = DeckModel.Default;

            if (Pages == null || Pages.Count == 0)
                Pages = new List<DeckPage> { new DeckPage { Name = "Page 1" } };

            int srcRows = Rows, srcCols = Columns;
            if (srcRows <= 0 || srcCols <= 0)
                InferGrid(out srcRows, out srcCols);

            foreach (var p in Pages)
            {
                if (p == null) continue;
                MigrateLegacyActionSlots(p, srcCols);
                p.Normalise(srcRows, srcCols, target.Rows, target.Columns, target.EncoderCount);
            }
            Pages.RemoveAll(p => p == null);

            Rows = target.Rows;
            Columns = target.Columns;
            Model = target.Key;

            if (Brightness < 5) Brightness = 5;
            if (Brightness > 100) Brightness = 100;
        }

        /// <summary>
        /// Works out what grid an older profile was written in. Profiles from before the
        /// multi-device support carry no geometry, only a key count.
        /// </summary>
        void InferGrid(out int rows, out int columns)
        {
            int count = 0;
            foreach (var p in Pages)
                if (p != null && p.Keys != null && p.Keys.Count > count) count = p.Keys.Count;

            switch (count)
            {
                case 9: rows = 3; columns = 3; return;   // AKP03
                case 10: rows = 2; columns = 5; return;  // AKP05
                case 18: rows = 3; columns = 6; return;  // AKP153
                default: rows = 3; columns = 5; return;  // the original 3x5 assumption
            }
        }

        /// <summary>
        /// Profiles written when a page held 12 action-slot entries stored them as
        /// slot = row*4 + col, with the navigation column excluded entirely.
        /// </summary>
        static void MigrateLegacyActionSlots(DeckPage page, int sourceColumns)
        {
            if (page.Keys == null || page.Keys.Count != 12 || sourceColumns != 5) return;

            var grid = DeckPage.NewKeys(15);
            for (int slot = 0; slot < 12; slot++)
            {
                int row = slot / 4, col = slot % 4;
                grid[row * 5 + col] = page.Keys[slot] ?? new KeyDefinition();
            }
            page.Keys = grid;
        }
    }

    public static class ProfileStore
    {
        static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public static void Save(DeckProfile profile, string path)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            // write to a temp file first so a crash cannot leave a truncated profile
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(profile, Options));
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }

        /// <summary>
        /// Loads a profile and fits it to <paramref name="target"/>. Passing null fits it
        /// to the deck the profile was written for, or the default deck if it names none.
        /// </summary>
        public static DeckProfile Load(string path, DeckModel target = null)
        {
            if (!File.Exists(path)) return null;
            var p = JsonSerializer.Deserialize<DeckProfile>(File.ReadAllText(path), Options);
            if (p == null) return null;
            p.Normalise(target ?? DeckModel.FromKey(p.Model) ?? DeckModel.Default);
            return p;
        }

        public static string DefaultPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AjazzDeck", "profile.json");
            }
        }
    }
}
