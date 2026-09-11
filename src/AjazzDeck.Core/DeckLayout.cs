using System.Collections.Generic;

namespace AjazzDeck.Core
{
    /// <summary>
    /// Physical layout rules. The deck is 3 rows of 5 keys. The rightmost column can
    /// optionally be reserved for page navigation; when it is not, all 15 keys are
    /// available and pages are changed from Deck Commander instead.
    /// </summary>
    public static class DeckLayout
    {
        public const int Rows = 3;
        public const int Columns = 5;
        public const int TotalKeys = Rows * Columns;

        /// <summary>Index of the column used for navigation when reserved.</summary>
        public const int NavColumn = 4;

        public const int PageUpKey = 4;        // row 0, col 4
        public const int PageIndicatorKey = 9; // row 1, col 4
        public const int PageDownKey = 14;     // row 2, col 4

        static readonly int[] ReservedGrids = { 0, 1, 2, 3, 5, 6, 7, 8, 10, 11, 12, 13 };
        static readonly int[] AllGrids = { 0,1,2,3,4,5,6,7,8,9,10,11,12,13,14 };

        public static bool IsNavKey(int gridIndex, bool navReserved)
        {
            return navReserved && gridIndex % Columns == NavColumn;
        }

        /// <summary>Grid indices usable as action keys, in reading order.</summary>
        public static int[] ActionGrids(bool navReserved)
        {
            return navReserved ? ReservedGrids : AllGrids;
        }

        public static int ActionKeyCount(bool navReserved)
        {
            return navReserved ? ReservedGrids.Length : AllGrids.Length;
        }

        public static int RowOf(int gridIndex) { return gridIndex / Columns; }
        public static int ColumnOf(int gridIndex) { return gridIndex % Columns; }
    }
}
