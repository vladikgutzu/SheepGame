using System;
using System.Collections.Generic;
using UnityEngine;

namespace SheepGame.Core
{
    [Serializable]
    public struct LevelPieceEntry
    {
        [Tooltip("The cell the piece leaves from. A piece longer than one cell trails " +
                 "backwards from here, away from the direction it faces.")]
        public Vector2Int position;
        public Direction direction;
        public int variantIndex;

        [Tooltip("How many cells the piece occupies, laid along the way it faces. 0 and 1 " +
                 "both mean one cell, so levels authored before long pieces existed read " +
                 "back unchanged.")]
        public int cells;

        [Tooltip("Index into the board's palette (see PieceColours). Only read on a level " +
                 "marked Coloured; everything else is coloured by ColourDealer at load.")]
        public int colour;

        /// <summary>Cells occupied, never less than one.</summary>
        // In the level rather than in the model library on purpose: how much of the board a
        // piece covers is the puzzle, and the library is only ever about how it looks. It
        // also keeps LevelSolver free of any dependency on a library it would otherwise
        // need just to know how big a thing is.
        public int Cells => Mathf.Max(1, cells);
    }

    // Deliberately carries no level number: play order is the order of GameManager's
    // Levels list and nothing else. A number stored here would be a second, silently
    // ignored source of truth — two assets once shared number 1 and loaded the wrong
    // levels for it.
    [CreateAssetMenu(fileName = "LevelData", menuName = "SheepGame/Level Data")]
    public class LevelData : ScriptableObject
    {
        public int gridWidth = 7;
        public int gridHeight = 7;
        public List<LevelPieceEntry> pieces = new List<LevelPieceEntry>();

        [Tooltip("Taps allowed on this level. 0 lets EconomyConfig work it out from the " +
                 "piece count and the level number, which is what almost every level wants; " +
                 "set it only where a silhouette plays harder or easier than its size says. " +
                 "A negative value turns the limit off entirely — a tutorial the player can " +
                 "fail is not a tutorial.")]
        public int tapBudget;

        [Tooltip("Auto lets EconomyConfig's schedule decide whether this level runs on a " +
                 "clock. Override it where a level would be spoiled by one — a silhouette " +
                 "that has to be read, or the tutorial — or where one is wanted out of turn.")]
        public LevelModeOverride mode = LevelModeOverride.Auto;

        // Both of these exist for the tutorial and should stay off everywhere else.
        [Tooltip("Frames the camera on the pieces instead of on the whole field. A level of " +
                 "two or three cases would otherwise be specks in the middle of seventy " +
                 "empty cells. Leave off for the shape levels: they are drawn to fill the " +
                 "fixed field and want the identical frame it gives them.")]
        public bool frameOnPieces;

        [Tooltip("Points at a piece that can leave, for free, on load and after every " +
                 "successful tap. Teaches what the arrow on the lid means without spending " +
                 "a booster or a word of text.")]
        public bool tutorialHints;

        [Header("Colours")]
        [Tooltip("The colours and carts below were set by hand. Off, the level is coloured " +
                 "when it is dealt, which is how every level drawn before colours existed " +
                 "still plays.")]
        public bool coloured;

        [Tooltip("Colour of each cart, in the order they pull in. Each colour must have " +
                 "exactly as many cases as its carts have seats.")]
        public List<int> carts = new List<int>();

        [Tooltip("Seats in a cart. 0 means 3.")]
        public int cartCapacity;

        [Tooltip("Places in the queue. 0 lets EconomyConfig decide.")]
        public int queueSlots;

        [Tooltip("A hand points at the next case to tap, on load and after every tap. " +
                 "For the very first level only.")]
        public bool tutorialHand;

        /// <summary>Seats in a cart, never less than one.</summary>
        public int CartCapacity => cartCapacity > 0 ? cartCapacity : 3;
    }
}
