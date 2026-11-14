using System.Collections.Generic;
using SheepGame.Core;
using UnityEditor;
using UnityEngine;

namespace SheepGame.EditorTools
{
    // Paint a silhouette on a grid; every painted cell becomes a plane. Facings are
    // solved for you, so a drawn heart or star is always clearable.
    public class LevelEditorWindow : EditorWindow
    {
        [MenuItem("SheepGame/Level Editor")]
        public static void Open()
        {
            var w = GetWindow<LevelEditorWindow>("Level Editor");
            w.minSize = new Vector2(420f, 560f);
        }

        enum Tool { Paint, Erase, Turn }

        const int MaxSize = 24;

        LevelData _target;
        int _width = 9;
        int _height = 11;
        bool[] _filled;
        Direction[] _facing;

        // Which cell each one is joined to, as a flat index, or -1 for a piece that stands on
        // its own. A long piece is two cells pointing at each other; the facing is kept on
        // both so the arrow can be drawn wherever it is wanted.
        int[] _partner;

        [System.NonSerialized] int _brushCells = 1;
        [System.NonSerialized] bool _brushAcross;

        Tool _tool = Tool.Paint;
        float _cellSize = 30f;
        int _seed;
        bool _autoSolveOnEdit = true;
        bool _noHoles = true;

        Vector2 _scroll;
        string _status = "Paint cells, then Save.";
        MessageType _statusKind = MessageType.Info;
        HashSet<Vector2Int> _stuck = new HashSet<Vector2Int>();

        GUIStyle _arrowStyle;

        void OnEnable()
        {
            if (_filled == null || _filled.Length != _width * _height) Resize(_width, _height);
        }

        // ---------------------------------------------------------------- grid state

        int Index(int x, int y) => x + y * _width;
        bool InBounds(int x, int y) => x >= 0 && x < _width && y >= 0 && y < _height;

        void Resize(int width, int height)
        {
            width = Mathf.Clamp(width, 3, MaxSize);
            height = Mathf.Clamp(height, 3, MaxSize);

            var filled = new bool[width * height];
            var facing = new Direction[width * height];
            var partner = new int[width * height];
            for (int i = 0; i < partner.Length; i++) partner[i] = -1;

            // Keep whatever still fits so resizing does not wipe a drawing.
            if (_filled != null)
            {
                for (int y = 0; y < Mathf.Min(height, _height); y++)
                    for (int x = 0; x < Mathf.Min(width, _width); x++)
                    {
                        filled[x + y * width] = _filled[Index(x, y)];
                        facing[x + y * width] = _facing[Index(x, y)];

                        // A link survives only if the cell it points at survived too, and it
                        // has to be re-expressed in the new width: a flat index means nothing
                        // once the row length changes.
                        int old = _partner != null ? _partner[Index(x, y)] : -1;
                        if (old >= 0)
                        {
                            int ox = old % _width, oy = old / _width;
                            if (ox < width && oy < height)
                                partner[x + y * width] = ox + oy * width;
                        }
                    }
            }

            _width = width;
            _height = height;
            _filled = filled;
            _facing = facing;
            _partner = partner;
            _stuck.Clear();
        }

        List<Vector2Int> FilledCells()
        {
            var cells = new List<Vector2Int>();
            for (int y = 0; y < _height; y++)
                for (int x = 0; x < _width; x++)
                    if (_filled[Index(x, y)]) cells.Add(new Vector2Int(x, y));
            return cells;
        }

        int PartnerOf(int i) => _partner != null && i >= 0 && i < _partner.Length ? _partner[i] : -1;

        void Unlink(int i)
        {
            int other = PartnerOf(i);
            if (other >= 0) _partner[other] = -1;
            if (i >= 0) _partner[i] = -1;
        }

        // Clears a cell and, if it was half of a long piece, the other half with it: half a
        // suitcase is not a thing the board can spawn.
        void ClearCell(int i)
        {
            int other = PartnerOf(i);
            Unlink(i);
            _filled[i] = false;
            if (other >= 0) _filled[other] = false;
        }

        /// <summary>Every piece on the grid: its cells, and the flat index it is keyed by.</summary>
        // Keyed by the lower of the two indices so a long piece is visited once, not twice.
        List<KeyValuePair<int, LevelSolver.PieceShape>> Pieces()
        {
            var pieces = new List<KeyValuePair<int, LevelSolver.PieceShape>>();

            for (int y = 0; y < _height; y++)
                for (int x = 0; x < _width; x++)
                {
                    int i = Index(x, y);
                    if (!_filled[i]) continue;

                    int other = PartnerOf(i);
                    if (other >= 0 && other < i) continue;          // counted from its partner
                    if (other >= 0 && !_filled[other]) { Unlink(i); other = -1; }

                    var cell = new Vector2Int(x, y);
                    if (other < 0)
                        pieces.Add(new KeyValuePair<int, LevelSolver.PieceShape>(
                            i, new LevelSolver.PieceShape(cell)));
                    else
                        pieces.Add(new KeyValuePair<int, LevelSolver.PieceShape>(
                            i, new LevelSolver.PieceShape(cell, new Vector2Int(other % _width, other / _width))));
                }

            return pieces;
        }

        // ---------------------------------------------------------------- operations

        void AutoSolve()
        {
            var pieces = Pieces();
            if (pieces.Count == 0) { SetStatus("Nothing painted yet.", MessageType.Warning); return; }

            var shapes = new List<LevelSolver.PieceShape>(pieces.Count);
            foreach (var piece in pieces) shapes.Add(piece.Value);

            if (!LevelSolver.TryAssignDirections(shapes, _width, _height, _seed, out var assigned))
            {
                // Long pieces can only travel along their own length, so unlike a plain
                // silhouette this really can be unsolvable. Another seed often finds a way.
                SetStatus("Could not assign facings. Try another seed, or free a long piece.",
                          MessageType.Error);
                return;
            }

            // Written to every cell of a piece, so the arrow can be drawn on whichever end
            // happens to be the front once the facing is known.
            foreach (var entry in assigned)
            {
                int front = Index(entry.position.x, entry.position.y);
                _facing[front] = entry.direction;

                int other = PartnerOf(front);
                if (other >= 0) _facing[other] = entry.direction;
            }

            _stuck.Clear();
            SetStatus($"Solved {pieces.Count} piece(s) (seed {_seed}).", MessageType.Info);
        }

        void Validate()
        {
            var level = BuildLevel();
            if (level.pieces.Count == 0) { SetStatus("Nothing painted yet.", MessageType.Warning); return; }

            bool ok = LevelSolver.TrySolve(level, out var stuck);
            _stuck = new HashSet<Vector2Int>(stuck);
            Object.DestroyImmediate(level);

            SetStatus(ok
                    ? $"Solvable — all {FilledCells().Count} planes can leave."
                    : $"{stuck.Count} plane(s) can never leave (marked red).",
                ok ? MessageType.Info : MessageType.Error);
        }

        LevelData BuildLevel()
        {
            var level = CreateInstance<LevelData>();
            level.gridWidth = _width;
            level.gridHeight = _height;

            foreach (var piece in Pieces())
            {
                var shape = piece.Value;
                Direction dir = _facing[piece.Key];

                // A long piece cannot travel sideways, so a facing left over from before it was
                // joined has to be brought back onto its own axis.
                if (!shape.Allows(dir))
                {
                    foreach (var candidate in System.Enum.GetValues(typeof(Direction)))
                        if (shape.Allows((Direction)candidate)) { dir = (Direction)candidate; break; }
                }

                level.pieces.Add(new LevelPieceEntry
                {
                    // The cell it leaves from, which for a long piece depends on the facing.
                    position = shape.Front(dir),
                    direction = dir,
                    variantIndex = 0,
                    cells = shape.Cells.Length,
                });
            }

            return level;
        }

        void Save()
        {
            // Enforce the invariant at the point it matters rather than on every brush
            // stroke, which would make the eraser useless inside a shape.
            int plugged = _noHoles ? FillEnclosed(_filled, _width, _height) : 0;
            if (plugged > 0)
            {
                AutoSolve();
                SetStatus($"Filled {plugged} enclosed hole(s) before saving.", MessageType.Info);
            }

            var built = BuildLevel();

            if (_target == null)
            {
                string path = EditorUtility.SaveFilePanelInProject(
                    "Save level", "Level", "asset", "Choose where to save the level",
                    "Assets/_Game/Data/Levels");
                if (string.IsNullOrEmpty(path)) { Object.DestroyImmediate(built); return; }

                AssetDatabase.CreateAsset(built, path);
                _target = built;
            }
            else
            {
                _target.gridWidth = built.gridWidth;
                _target.gridHeight = built.gridHeight;
                _target.pieces = new List<LevelPieceEntry>(built.pieces);
                Object.DestroyImmediate(built);
            }

            EditorUtility.SetDirty(_target);
            AssetDatabase.SaveAssets();

            bool ok = LevelSolver.TrySolve(_target, out var stuck);
            SetStatus(ok
                    ? $"Saved {_target.name} ({_target.pieces.Count} planes)."
                    : $"Saved {_target.name}, but {stuck.Count} plane(s) are stuck.",
                ok ? MessageType.Info : MessageType.Warning);
        }

        void Load()
        {
            if (_target == null) { SetStatus("Assign a level asset first.", MessageType.Warning); return; }

            Resize(_target.gridWidth, _target.gridHeight);
            System.Array.Clear(_filled, 0, _filled.Length);

            for (int i = 0; i < _partner.Length; i++) _partner[i] = -1;

            foreach (var p in _target.pieces)
            {
                if (!InBounds(p.position.x, p.position.y)) continue;

                int front = Index(p.position.x, p.position.y);
                _filled[front] = true;
                _facing[front] = p.direction;

                // Longer than one cell: the rest trails backwards out of the front, exactly
                // as the board lays it out.
                Vector2Int back = -DirectionUtil.ToVector(p.direction);
                Vector2Int tail = p.position + back;
                if (p.Cells >= 2 && InBounds(tail.x, tail.y))
                {
                    int t = Index(tail.x, tail.y);
                    _filled[t] = true;
                    _facing[t] = p.direction;
                    _partner[front] = t;
                    _partner[t] = front;
                }
            }

            _stuck.Clear();
            SetStatus($"Loaded {_target.name} ({_target.pieces.Count} planes).", MessageType.Info);
        }

        void SetStatus(string text, MessageType kind)
        {
            _status = text;
            _statusKind = kind;
        }

        // ---------------------------------------------------------------- stamps

        // Implicit curves sampled per cell, so a stamp adapts to whatever grid size the
        // designer is working at instead of being a fixed pixel pattern.
        void Stamp(string shape)
        {
            for (int y = 0; y < _height; y++)
                for (int x = 0; x < _width; x++)
                {
                    // Normalised to roughly [-1, 1] with y pointing up.
                    float nx = (x - (_width - 1) * 0.5f) / ((_width - 1) * 0.5f);
                    float ny = (y - (_height - 1) * 0.5f) / ((_height - 1) * 0.5f);
                    _filled[Index(x, y)] = Inside(shape, nx, ny);
                }

            if (_noHoles) FillEnclosed(_filled, _width, _height);

            if (_autoSolveOnEdit) AutoSolve();
            else _stuck.Clear();
        }

        // Fills any empty cell that cannot be reached from outside the shape, so a
        // silhouette is always a solid mass of planes rather than an outline with a
        // hollow middle. Flood-fills the background inwards from the border; whatever
        // the flood never touches is an enclosed hole.
        public static int FillEnclosed(bool[] filled, int width, int height)
        {
            var outside = new bool[width * height];
            var queue = new Queue<Vector2Int>();

            void Seed(int x, int y)
            {
                if (x < 0 || x >= width || y < 0 || y >= height) return;
                int i = x + y * width;
                if (filled[i] || outside[i]) return;
                outside[i] = true;
                queue.Enqueue(new Vector2Int(x, y));
            }

            for (int x = 0; x < width; x++) { Seed(x, 0); Seed(x, height - 1); }
            for (int y = 0; y < height; y++) { Seed(0, y); Seed(width - 1, y); }

            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                Seed(p.x + 1, p.y);
                Seed(p.x - 1, p.y);
                Seed(p.x, p.y + 1);
                Seed(p.x, p.y - 1);
            }

            int plugged = 0;
            for (int i = 0; i < filled.Length; i++)
                if (!filled[i] && !outside[i]) { filled[i] = true; plugged++; }

            return plugged;
        }

        // Public so level-authoring scripts stamp through exactly the same maths as the
        // buttons, and a generated level stays reproducible from the panel.
        public static bool Inside(string shape, float nx, float ny)
        {
            switch (shape)
            {
                case "Heart":
                {
                    // Classic (x^2 + y^2 - 1)^3 - x^2 y^3 <= 0. Below the axis the y^3
                    // term is negative, which forces the shape inside the unit circle,
                    // so the curve bottoms out at exactly y = -1 while the lobes bulge
                    // up to about y = 1.3. Map the grid onto that box or the heart
                    // floats with dead rows beneath it.
                    float hx = nx * 1.15f;
                    float hy = ny * 1.15f + 0.15f;
                    float t = hx * hx + hy * hy - 1f;
                    return t * t * t - hx * hx * hy * hy * hy <= 0f;
                }
                case "Diamond":
                    return Mathf.Abs(nx) + Mathf.Abs(ny) <= 1f;
                case "Circle":
                    return nx * nx + ny * ny <= 1f;
                case "Square":
                    return true;
                case "Cross":
                    return Mathf.Abs(nx) <= 0.34f || Mathf.Abs(ny) <= 0.34f;
                case "Star":
                {
                    // Five-pointed: radius oscillates with angle.
                    float d = Mathf.Sqrt(nx * nx + ny * ny);
                    if (d < 0.001f) return true;
                    float a = Mathf.Atan2(ny, nx);
                    float r = 0.62f + 0.38f * Mathf.Cos(5f * (a - Mathf.PI * 0.5f));
                    return d <= r;
                }
                default:
                    return false;
            }
        }

        // ---------------------------------------------------------------- gui

        void OnGUI()
        {
            _arrowStyle ??= new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14
            };

            DrawToolbar();
            EditorGUILayout.Space(4f);
            DrawGrid();
            EditorGUILayout.Space(4f);
            EditorGUILayout.HelpBox(_status, _statusKind);
        }

        void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                var asset = (LevelData)EditorGUILayout.ObjectField("Level asset", _target, typeof(LevelData), false);
                if (EditorGUI.EndChangeCheck()) _target = asset;

                using (new EditorGUI.DisabledScope(_target == null))
                    if (GUILayout.Button("Load", GUILayout.Width(60f))) Load();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                int w = EditorGUILayout.IntField("Grid", _width, GUILayout.Width(160f));
                int h = EditorGUILayout.IntField(_height, GUILayout.Width(40f));
                if (EditorGUI.EndChangeCheck()) Resize(w, h);

                GUILayout.FlexibleSpace();
                GUILayout.Label("Order comes from GameManager > Levels", EditorStyles.miniLabel);
            }

            _tool = (Tool)GUILayout.Toolbar((int)_tool, new[] { "Paint", "Erase", "Turn" });

            // What the Paint brush lays down. A long piece is two joined cells; which way the
            // pair lies is chosen here, and which way it then *leaves* is the solver's answer,
            // so the two controls are deliberately not the same thing.
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Brush", GUILayout.Width(40f));
                int brush = GUILayout.Toolbar(_brushCells >= 2 ? 1 : 0,
                                              new[] { "1 cell", "2 cells" });
                _brushCells = brush == 1 ? 2 : 1;

                using (new EditorGUI.DisabledScope(_brushCells < 2))
                {
                    int lie = GUILayout.Toolbar(_brushAcross ? 1 : 0, new[] { "Vertical", "Horizontal" });
                    _brushAcross = lie == 1;
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Heart")) Stamp("Heart");
                if (GUILayout.Button("Star")) Stamp("Star");
                if (GUILayout.Button("Diamond")) Stamp("Diamond");
                if (GUILayout.Button("Circle")) Stamp("Circle");
                if (GUILayout.Button("Square")) Stamp("Square");
                if (GUILayout.Button("Cross")) Stamp("Cross");
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                _seed = EditorGUILayout.IntField("Seed", _seed, GUILayout.Width(160f));
                if (GUILayout.Button("Reroll", GUILayout.Width(70f)))
                {
                    _seed = Random.Range(0, int.MaxValue);
                    AutoSolve();
                }
                GUILayout.FlexibleSpace();
                _cellSize = EditorGUILayout.Slider(_cellSize, 14f, 48f, GUILayout.Width(160f));
            }

            _autoSolveOnEdit = EditorGUILayout.ToggleLeft(
                "Re-solve facings after every edit", _autoSolveOnEdit);

            using (new EditorGUILayout.HorizontalScope())
            {
                _noHoles = EditorGUILayout.ToggleLeft(
                    "No empty cells inside the shape (applied on stamp and save)", _noHoles);

                if (GUILayout.Button("Fill holes now", GUILayout.Width(110f)))
                {
                    int plugged = FillEnclosed(_filled, _width, _height);
                    if (plugged > 0) AutoSolve();
                    SetStatus(plugged > 0 ? $"Filled {plugged} enclosed hole(s)."
                                          : "No enclosed holes found.", MessageType.Info);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Solve facings")) AutoSolve();
                if (GUILayout.Button("Validate")) Validate();
                if (GUILayout.Button("Clear"))
                {
                    System.Array.Clear(_filled, 0, _filled.Length);
                    _stuck.Clear();
                    SetStatus("Cleared.", MessageType.Info);
                }
                if (GUILayout.Button("Save")) Save();
                if (GUILayout.Button("Save As…")) { _target = null; Save(); }
            }
        }

        void DrawGrid()
        {
            float w = _width * _cellSize;
            float h = _height * _cellSize;

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            Rect area = GUILayoutUtility.GetRect(w, h, GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));

            HandleMouse(area);

            EditorGUI.DrawRect(area, new Color(0.13f, 0.13f, 0.14f));

            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    // Row 0 is the bottom of the board, so flip for screen space.
                    var cell = new Rect(area.x + x * _cellSize,
                                        area.y + (_height - 1 - y) * _cellSize,
                                        _cellSize - 1f, _cellSize - 1f);

                    int i = Index(x, y);
                    bool on = _filled[i];
                    bool bad = on && _stuck.Contains(new Vector2Int(x, y));
                    int other = PartnerOf(i);

                    // A long piece is one colour across both its cells, a shade off the
                    // singles, so a pair reads as one thing rather than as two neighbours.
                    Color paint = bad ? new Color(0.72f, 0.22f, 0.22f)
                                : !on ? new Color(0.21f, 0.21f, 0.23f)
                                : other >= 0 ? new Color(0.30f, 0.62f, 0.55f)
                                             : new Color(0.36f, 0.55f, 0.78f);
                    EditorGUI.DrawRect(cell, paint);

                    // The arrow goes on the cell the piece leaves from, so a pair carries one
                    // arrow between them and it is obvious which end leads.
                    bool isFront = other < 0 || IsFrontCell(x, y, i, other);
                    if (on && isFront && _cellSize >= 18f)
                        GUI.Label(cell, Arrow(_facing[i]), _arrowStyle);
                }
            }

            EditorGUILayout.EndScrollView();
        }

        // Paints one cell, or joins two when the brush is set to a long piece. A long brush
        // that will not fit is refused outright rather than falling back to a single: quietly
        // painting something other than what the brush says is worse than painting nothing.
        bool Paint(int x, int y)
        {
            int i = Index(x, y);

            if (_brushCells <= 1)
            {
                if (_filled[i]) return false;
                _filled[i] = true;
                return true;
            }

            int nx = x + (_brushAcross ? 1 : 0);
            int ny = y + (_brushAcross ? 0 : 1);
            if (!InBounds(nx, ny)) { SetStatus("No room for a long piece there.", MessageType.Warning); return false; }

            int j = Index(nx, ny);
            if (_filled[i] || _filled[j])
            {
                SetStatus("Those cells are taken.", MessageType.Warning);
                return false;
            }

            _filled[i] = _filled[j] = true;
            _partner[i] = j;
            _partner[j] = i;
            return true;
        }

        // Steps to the next facing the piece is actually allowed. A long piece has two, not
        // four: it travels along its own length or not at all.
        void Turn(int x, int y)
        {
            int i = Index(x, y);
            int other = PartnerOf(i);

            var shape = other < 0
                ? new LevelSolver.PieceShape(new Vector2Int(x, y))
                : new LevelSolver.PieceShape(new Vector2Int(x, y),
                                             new Vector2Int(other % _width, other / _width));

            Direction dir = _facing[i];
            for (int step = 1; step <= 4; step++)
            {
                var next = (Direction)(((int)dir + step) % 4);
                if (!shape.Allows(next)) continue;
                _facing[i] = next;
                if (other >= 0) _facing[other] = next;
                break;
            }

            _stuck.Clear();
        }

        static string Arrow(Direction d) => d switch
        {
            Direction.Up => "↑",
            Direction.Down => "↓",
            Direction.Left => "←",
            Direction.Right => "→",
            _ => "?"
        };

        // Of the two cells of a long piece, the one further along the way it faces.
        bool IsFrontCell(int x, int y, int i, int other)
        {
            Vector2Int step = DirectionUtil.ToVector(_facing[i]);
            int ox = other % _width, oy = other / _width;
            return x * step.x + y * step.y >= ox * step.x + oy * step.y;
        }

        void HandleMouse(Rect area)
        {
            var e = Event.current;
            if (e.type != EventType.MouseDown && e.type != EventType.MouseDrag) return;
            if (!area.Contains(e.mousePosition)) return;

            int x = Mathf.FloorToInt((e.mousePosition.x - area.x) / _cellSize);
            int y = _height - 1 - Mathf.FloorToInt((e.mousePosition.y - area.y) / _cellSize);
            if (!InBounds(x, y)) return;

            // Right button always erases, whatever the active tool is.
            Tool tool = e.button == 1 ? Tool.Erase : _tool;
            bool changedShape = false;

            switch (tool)
            {
                case Tool.Paint:
                    changedShape = Paint(x, y);
                    break;

                case Tool.Erase:
                    if (_filled[Index(x, y)]) { ClearCell(Index(x, y)); changedShape = true; }
                    break;

                case Tool.Turn:
                    // Dragging would spin the arrow every frame, so only step on press.
                    if (e.type == EventType.MouseDown && _filled[Index(x, y)]) Turn(x, y);
                    break;
            }

            if (changedShape)
            {
                if (_autoSolveOnEdit) AutoSolve();
                else _stuck.Clear();
            }

            e.Use();
            Repaint();
        }
    }
}
