using UnityEngine;

namespace SheepGame.Core
{
    // The diagonals are appended rather than interleaved: levels store the facing as its
    // number, and every level already written only knows the first four.
    public enum Direction
    {
        Up,
        Down,
        Left,
        Right,
        UpRight,
        UpLeft,
        DownRight,
        DownLeft
    }

    public static class DirectionUtil
    {
        public static Vector2Int ToVector(Direction d)
        {
            switch (d)
            {
                case Direction.Up: return new Vector2Int(0, 1);
                case Direction.Down: return new Vector2Int(0, -1);
                case Direction.Left: return new Vector2Int(-1, 0);
                case Direction.Right: return new Vector2Int(1, 0);
                case Direction.UpRight: return new Vector2Int(1, 1);
                case Direction.UpLeft: return new Vector2Int(-1, 1);
                case Direction.DownRight: return new Vector2Int(1, -1);
                case Direction.DownLeft: return new Vector2Int(-1, -1);
                default: return Vector2Int.zero;
            }
        }

        public static float ToYRotation(Direction d)
        {
            switch (d)
            {
                case Direction.Up: return 0f;
                case Direction.UpRight: return 45f;
                case Direction.Right: return 90f;
                case Direction.DownRight: return 135f;
                case Direction.Down: return 180f;
                case Direction.DownLeft: return 225f;
                case Direction.Left: return 270f;
                case Direction.UpLeft: return 315f;
                default: return 0f;
            }
        }

        public static bool IsDiagonal(Direction d) => d >= Direction.UpRight;

        /// <summary>
        /// Whether a piece this many cells long may face this way. A diagonal is one cell
        /// only: a long case at 45 degrees would cover its two cells corner to corner and
        /// overhang the four beside them.
        /// </summary>
        public static bool AllowedFor(Direction d, int cells) => cells <= 1 || !IsDiagonal(d);
    }
}
