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

    /// <summary>
    /// A page of keys. <see cref="Keys"/> is always 15 entries indexed by grid position
    /// (top-left = 0, reading order), whether or not the navigation column is reserved.
    /// Storing all 15 means toggling the navigation column never loses a key.
    /// </summary>
    public sealed class DeckPage
    {
        public string Name { get; set; } = "Page";
        public List<KeyDefinition> Keys { get; set; } = NewKeys();

        public static List<KeyDefinition> NewKeys()
        {
            var list = new List<KeyDefinition>(DeckLayout.TotalKeys);
            for (int i = 0; i < DeckLayout.TotalKeys; i++) list.Add(new KeyDefinition());
            return list;
        }

        public void Normalise()
        {
            if (Keys == null) { Keys = NewKeys(); return; }

            // Migrate profiles written when a page held 12 action-slot entries
            // (slot = row*4 + col) to the 15-entry grid layout.
            if (Keys.Count == 12)
            {
                var grid = NewKeys();
                for (int slot = 0; slot < 12; slot++)
                {
                    int row = slot / 4, col = slot % 4;
                    grid[row * DeckLayout.Columns + col] = Keys[slot] ?? new KeyDefinition();
                }
                Keys = grid;
                return;
            }

            while (Keys.Count < DeckLayout.TotalKeys) Keys.Add(new KeyDefinition());
            while (Keys.Count > DeckLayout.TotalKeys) Keys.RemoveAt(Keys.Count - 1);
            for (int i = 0; i < Keys.Count; i++)
                if (Keys[i] == null) Keys[i] = new KeyDefinition();
        }
    }

    public sealed class DeckProfile
    {
        public string Name { get; set; } = "Default";
        public int Brightness { get; set; } = 60;

        /// <summary>
        /// When true the rightmost column becomes page up / indicator / page down,
        /// leaving 12 action keys. When false all 15 keys are usable and pages are
        /// changed from Deck Commander.
        /// </summary>
        public bool ReservePageColumn { get; set; } = true;
        public List<DeckPage> Pages { get; set; } = new List<DeckPage> { new DeckPage { Name = "Page 1" } };

        public void Normalise()
        {
            if (Pages == null || Pages.Count == 0)
                Pages = new List<DeckPage> { new DeckPage { Name = "Page 1" } };
            foreach (var p in Pages) p.Normalise();
            if (Brightness < 5) Brightness = 5;
            if (Brightness > 100) Brightness = 100;
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

        public static DeckProfile Load(string path)
        {
            if (!File.Exists(path)) return null;
            var p = JsonSerializer.Deserialize<DeckProfile>(File.ReadAllText(path), Options);
            if (p != null) p.Normalise();
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


