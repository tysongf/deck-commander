using System;
using System.Collections.Generic;
using DeckCommander.Devices;

namespace DeckCommander.Core
{
    /// <summary>
    /// Where things sit on a particular deck. The rightmost column can optionally be
    /// reserved for page navigation; when it is not, every key is available and pages are
    /// changed from Deck Commander instead.
    /// </summary>
    /// <remarks>
    /// This used to be a static class holding the AKP153E's 3x5 geometry as constants.
    /// It is now built from a <see cref="DeckModel"/>, because the family runs from a 3x3
    /// knob deck to a 3x6 panel and nothing about the grid is universal.
    /// </remarks>
    public sealed class DeckLayout
    {
        public DeckModel Model { get; private set; }

        public int Rows { get; private set; }
        public int Columns { get; private set; }
        public int TotalKeys { get; private set; }

        /// <summary>Index of the column used for navigation when reserved, or -1.</summary>
        public int NavColumn { get; private set; }

        public int PageUpKey { get; private set; }
        public int PageIndicatorKey { get; private set; }
        public int PageDownKey { get; private set; }

        /// <summary>False on decks too small to give up a column.</summary>
        public bool CanReserveNav { get { return NavColumn >= 0; } }

        /// <summary>
        /// False when the deck has fewer than three rows, so there is nowhere to put the
        /// page counter between the two arrows. The page selector is then app-only.
        /// </summary>
        public bool HasPageIndicator { get { return PageIndicatorKey >= 0; } }

        int[] _actionGrids;
        int[] _allGrids;

        DeckLayout() { }

        public static DeckLayout For(DeckModel model)
        {
            if (model == null) throw new ArgumentNullException("model");

            var l = new DeckLayout
            {
                Model = model,
                Rows = model.Rows,
                Columns = model.Columns,
                TotalKeys = model.KeyCount
            };

            // Navigation has to land on a column you can actually press. On the AKP153
            // the rightmost column is a display strip with no switches under it, so the
            // arrows belong one column to its left.
            l.NavColumn = -1;
            if (model.Columns >= 2 && model.Rows >= 2)
            {
                for (int c = model.Columns - 1; c >= 1; c--)
                {
                    bool allPressable = true;
                    for (int r = 0; r < model.Rows; r++)
                        if (!model.IsPressable(r * model.Columns + c)) { allPressable = false; break; }
                    if (allPressable) { l.NavColumn = c; break; }
                }
            }

            if (l.NavColumn >= 0)
            {
                l.PageUpKey = l.NavColumn;
                l.PageDownKey = (model.Rows - 1) * model.Columns + l.NavColumn;
                l.PageIndicatorKey = model.Rows >= 3
                    ? (model.Rows / 2) * model.Columns + l.NavColumn
                    : -1;
            }
            else
            {
                l.PageUpKey = l.PageDownKey = l.PageIndicatorKey = -1;
            }

            // A slot with no switch under it can never be an action key, whether or not
            // the navigation column is reserved.
            var all = new List<int>(l.TotalKeys);
            var action = new List<int>(l.TotalKeys);
            for (int i = 0; i < l.TotalKeys; i++)
            {
                if (!model.IsPressable(i)) continue;
                all.Add(i);
                if (l.NavColumn < 0 || i % model.Columns != l.NavColumn) action.Add(i);
            }
            l._allGrids = all.ToArray();
            l._actionGrids = action.ToArray();

            return l;
        }

        /// <summary>The layout the editor uses when no deck is attached.</summary>
        public static DeckLayout Default { get { return For(DeckModel.Default); } }

        public bool IsNavKey(int gridIndex, bool navReserved)
        {
            return navReserved && NavColumn >= 0 && gridIndex % Columns == NavColumn;
        }

        /// <summary>True when the slot has a screen behind it and can be drawn on.</summary>
        public bool HasScreen(int gridIndex) { return Model.HasScreen(gridIndex); }

        /// <summary>True when there is a switch under the slot.</summary>
        public bool IsPressable(int gridIndex) { return Model.IsPressable(gridIndex); }

        /// <summary>Grid indices usable as action keys, in reading order.</summary>
        public int[] ActionGrids(bool navReserved)
        {
            return navReserved && NavColumn >= 0 ? _actionGrids : _allGrids;
        }

        public int ActionKeyCount(bool navReserved)
        {
            return ActionGrids(navReserved).Length;
        }

        public int RowOf(int gridIndex) { return gridIndex / Columns; }
        public int ColumnOf(int gridIndex) { return gridIndex % Columns; }
    }
}
