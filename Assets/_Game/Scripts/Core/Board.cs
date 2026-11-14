using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace SheepGame.Core
{
    public class Board : MonoBehaviour
    {
        public PieceModelLibrary modelLibrary;
        [Tooltip("How many of the library's models a board may mix. 1 keeps every piece " +
                 "identical. Which model a piece gets is decoration, so it is decided here " +
                 "rather than stored per level.")]
        public int modelVariety = 1;
        public Camera raycastCamera;
        public float cellSize = 1.2f;
        public float roadMargin = 1.8f;

        [Tooltip("Share of a cell a piece's footprint should span, measured and applied as " +
                 "it spawns. Lets one set of model prefabs sit on a board of any size — the " +
                 "same luggage fills a 7-unit airport cell and a 2-unit parking bay — " +
                 "instead of every re-scale meaning a pass over every prefab. 0 leaves each " +
                 "prefab exactly as it was authored.")]
        [Range(0f, 1.2f)] public float pieceFill = 0f;

        [Tooltip("The playfield every level is laid out in, so the hall around the board is " +
                 "one fixed size and only how many cells are filled changes. A level drawn " +
                 "on a smaller grid is centred inside it. Zero uses each level's own size, " +
                 "which resizes the apron — and anything built around it — per level.")]
        public Vector2Int fieldSize = Vector2Int.zero;

        [Tooltip("Height of the belt surface pieces ride once they leave their cell. They " +
                 "hop up onto it on the way out, so a raised conveyor carries them rather " +
                 "than passing through it. Zero keeps travel flat on the ground.")]
        public float beltHeight = 0f;

        [Header("Hopping onto the belt")]
        [Tooltip("How far short of the lane a piece stops before it jumps up onto the belt. " +
                 "Capped at the road margin, so it never starts the hop inside the grid.")]
        public float hopDistance = 5f;
        [Tooltip("Seconds the hop takes, pose and all.")]
        public float hopTime = 0.45f;
        [Tooltip("How far above the belt the jump arcs. Zero is a flat slide up.")]
        public float hopArc = 4.5f;

        [Header("Departure")]
        [Tooltip("Editable route out to the runway and away. Takes precedence over the " +
                 "exit point / runway end pair below when assigned.")]
        public DeparturePath departurePath;
        [Tooltip("Where the perimeter route ends: the runway threshold, or a gate for ground themes.")]
        public Transform exitPoint;
        [Tooltip("Far end of the runway. Leave empty to have pieces simply despawn at the exit point.")]
        public Transform runwayEnd;

        [Tooltip("The mouths pieces leave through, one marker per lane. A piece takes the " +
                 "lane on the side it came round the ring, so a claim belt with two openings " +
                 "is fed from both instead of everything queueing through the middle. Each " +
                 "marker also sets where its lane leaves the ring — directly behind it. " +
                 "Empty falls back to exitPoint / runwayEnd.")]
        public Transform[] outfeeds;
        public float taxiSpeed = 7f;
        public float takeoffSpeed = 30f;
        public float turnSpeed = 360f;
        [Tooltip("Fraction of the runway rolled before the nose lifts.")]
        [Range(0.1f, 0.9f)] public float rotatePoint = 0.45f;
        public float climbHeight = 40f;
        public float climbPitch = 18f;

        [Tooltip("Painted playfield. Resized to each level so small levels do not sit " +
                 "in the middle of an oversized slab. Found in children if left empty.")]
        public ApronSurface apron;

        [Tooltip("Optional conveyor around the field. Re-fitted to the board as each level " +
                 "loads, exactly like the apron, and it hands back the belt height and the " +
                 "outfeed lanes below — so those are readouts, not settings, whenever one " +
                 "is present. Found in children if left empty.")]
        public BeltRing belt;

        [Tooltip("Optional. Re-frames the camera per level so every board fills the " +
                 "screen the same way. Leave empty to keep a fixed camera.")]
        public CameraFramer cameraFramer;

        [Tooltip("Cells of air kept in shot beyond the lane around the field. Zero crops to " +
                 "the belt itself; raise it to show more of the ground the board stands on.")]
        public float framePadding = 0.25f;

        [Header("Hint")]
        [Tooltip("Seconds the hint keeps pointing at a piece before it settles back.")]
        public float hintDuration = 3f;

        [Header("Tapping")]
        [Tooltip("Furthest a finger may travel between press and release and still count as a " +
                 "tap, in inches. Anything further was a swipe and belongs to the system.")]
        public float tapSlopInches = 0.1f;
        [Tooltip("Longest a finger may stay down and still count as a tap. A long press is " +
                 "somebody reading the board, not choosing on it.")]
        public float tapHoldSeconds = 0.7f;
        [Tooltip("How long the player holds the board before the game speeds up. Kept below " +
                 "Tap Hold Seconds so the gesture has a quick, deliberate response.")]
        [Min(0.15f)] public float speedHoldSeconds = 0.4f;

        [Header("Gizmos")]
        [Tooltip("Grid size drawn in the scene view before a level is loaded.")]
        public Vector2Int previewGrid = new Vector2Int(7, 7);

        [Header("Leaving")]
        [Tooltip("Old departure: ride the belt all the way round to a mouth. Off, a case " +
                 "slides straight off the board and hops out towards the cart or the queue, " +
                 "which is what the colour rules want -- the rules hear about a case when it " +
                 "arrives, and a lap of the belt is several seconds of the player waiting to " +
                 "find out where it went. It was also where cases ran through each other: " +
                 "every lane merges into the same belt with nothing keeping them apart.")]
        public bool rideBelt = false;
        [Tooltip("The belt past the junction: one lane, the queue on it, and the check-in " +
                 "terminal at its end. With this set and rideBelt on, a case slides to the edge, " +
                 "hops onto the ring and rides it to the lane, keeping its distance from the " +
                 "others all the way.")]
        public CheckInLane checkIn;
        [Tooltip("How fast a case slides off the board, in cells per second.")]
        public float slideCellsPerSecond = 9f;
        [Tooltip("Seconds of the hop-and-shrink at the edge of the board.")]
        public float popTime = 0.3f;
        [Tooltip("How high that hop goes, as a share of a cell.")]
        public float popHeight = 0.8f;

        public event Action OnBoardEmpty;
        /// <summary>Raised when a stationary long press starts or ends.</summary>
        public event Action<bool> SpeedHoldChanged;
        public event Action OnBoardChanged;

        /// <summary>
        /// A case has left the board and is on its way to the cart or the queue. The position
        /// is where it was last seen in the world, for anything that picks it up on screen.
        /// </summary>
        public event Action<PuzzlePiece, Vector3> PieceArrived;

        readonly Dictionary<Vector2Int, PuzzlePiece> _occupied = new Dictionary<Vector2Int, PuzzlePiece>();
        readonly List<PuzzlePiece> _allPieces = new List<PuzzlePiece>();
        readonly List<PuzzlePiece> _departing = new List<PuzzlePiece>();

        // A case sliding off the board, and the cells it still has to cross. A case whose own
        // way out crosses those waits for it: the rows and columns are clear of cases that are
        // standing still, not of cases that are already on the move -- which is how two of them
        // used to drive through each other.
        sealed class Slide
        {
            public PuzzlePiece Piece;
            public Direction Dir;
            public Vector2Int Start;
            public int Cells;
            public List<Vector2Int> Lane;
            public bool Moving;
            public bool InPlace;     // hopping out where it stands, not sliding
            public float Progress;   // cells travelled
        }

        readonly List<Slide> _slides = new List<Slide>();
        int _nextPieceId;

        GameManager _gameManager;
        LevelData _level;
        PuzzlePiece _hinted;
        int _width;
        int _height;
        // The first level should already be framed on frame one; later levels glide.
        bool _hasFramedOnce;

        public IReadOnlyList<PuzzlePiece> AllPieces => _allPieces;

        public void SetGameManager(GameManager gm) => _gameManager = gm;

        public void LoadLevel(LevelData level)
        {
            ClearBoard();
            _level = level;

            // A level records the grid it was drawn on; the board it plays on may be wider.
            // Padding is safe because an empty cell blocks nothing: a piece that could reach
            // the edge still can, and one that was walled in is walled in by the same
            // neighbour. So centring a small level in a fixed field leaves the puzzle
            // exactly as authored while the hall around it stays put.
            _width = fieldSize.x > 0 ? Mathf.Max(fieldSize.x, level.gridWidth) : level.gridWidth;
            _height = fieldSize.y > 0 ? Mathf.Max(fieldSize.y, level.gridHeight) : level.gridHeight;
            var offset = new Vector2Int((_width - level.gridWidth) / 2,
                                        (_height - level.gridHeight) / 2);

            if (apron == null) apron = GetComponentInChildren<ApronSurface>();
            if (apron != null) apron.Rebuild(_width, _height, cellSize, roadMargin);

            FitBelt();

            foreach (var entry in level.pieces)
                SpawnPiece(entry.position + offset, entry.direction, entry.variantIndex, entry.Cells,
                           level.coloured ? entry.colour : -1);

            RecomputeEscapable();

            // Framed after the pieces exist, because it is the pieces that are framed.
            FrameLevel(immediate: !_hasFramedOnce);
            _hasFramedOnce = true;
        }

        // Frames the playfield and the lane around it — the lane being where the belt runs,
        // and a frame cropped tighter than that leaves the player unable to see where
        // anything they tap is going.
        //
        // Which makes the field size the thing that decides how big pieces are on screen:
        // padded out to a fixed 9x11, a five-cell level is framed with two thirds of the
        // shot on empty cells. Leaving fieldSize at zero sizes the field, the belt and the
        // frame to each level in turn, and the ground underneath simply shows around it.
        /// <summary>
        /// Re-runs the framing for the level as loaded. For the off-screen render, which
        /// changes the camera's aspect and has to re-fit to it.
        /// </summary>
        // Public so tooling can ask for the frame rather than rebuild the rectangle itself.
        // SceneAudit did rebuild it, from fieldSize, and so went on framing the whole field
        // after levels gained the option not to — the phone shot then disagreed with the
        // game, which is the one thing that tool exists not to do.
        public void RefitFrame(bool immediate = true) => FrameLevel(immediate);

        void FrameLevel(bool immediate)
        {
            if (cameraFramer == null) return;

            float pad = roadMargin + cellSize * Mathf.Max(0f, framePadding);

            // The lane and its terminal are where every case ends up, so they are in shot too.
            List<Vector3> include = null;
            if (checkIn != null && rideBelt)
            {
                include = new List<Vector3>(checkIn.FramePoints());
                if (include.Count == 0) include = null;
                else
                {
                    // The far side of the belt, held on screen too. Without it the framer is
                    // free to push the board up past the top of the frame to make room.
                    float edge = cellSize * 0.5f;
                    include.Add(LocalToWorld(RingLeft - edge, 0f, RingBack + edge));
                    include.Add(LocalToWorld(RingRight + edge, 0f, RingBack + edge));
                    // And the middle of the near side, so centring the shot never crops the belt off the
                    // bottom. Not its corners: those are nearest the lens and widest on screen, and
                    // holding them in would shrink the whole board to spare a sliver at each side.
                    include.Add(LocalToWorld(0f, 0f, RingFront - edge));
                }
            }

            // Opt-in, per level, and only the tutorial uses it: three cases centred in the
            // fixed 7x10 field would be specks with the shot spent on empty floor. Every
            // shape level is drawn to fill that field and wants the identical frame it
            // gives them, which is the whole reason the frame stopped following the pieces.
            if (_level != null && _level.frameOnPieces && _allPieces.Count > 0)
            {
                Vector3 min = GridToLocal(_allPieces[0].GridPos);
                Vector3 max = min;

                for (int i = 1; i < _allPieces.Count; i++)
                {
                    Vector3 local = GridToLocal(_allPieces[i].GridPos);
                    min = Vector3.Min(min, local);
                    max = Vector3.Max(max, local);
                }

                // Half a cell each way turns cell centres into the ground the cases stand
                // on; the lane padding then keeps the belt they leave along in shot, which
                // is most of what a first level has to show.
                cameraFramer.Frame(transform, transform.TransformPoint((min + max) * 0.5f),
                                   (max.x - min.x) * 0.5f + cellSize * 0.5f + pad,
                                   (max.z - min.z) * 0.5f + cellSize * 0.5f + pad,
                                   immediate, include);
                return;
            }

            cameraFramer.Frame(transform, transform.position,
                               _width * 0.5f * cellSize + pad,
                               _height * 0.5f * cellSize + pad,
                               immediate, include);
        }

        /// <summary>
        /// Points at a piece that can leave right now. Returns false when none can, which
        /// on a solvable board only happens once it is empty.
        /// </summary>
        // The only mistake this game allows is tapping a piece that is walled in — a removal
        // never blocks anything, so no order of taps can go wrong. That makes the hint the
        // one booster that prevents a mistake rather than forgiving it.
        public bool ShowHint() => ShowHint(null);

        /// <summary>
        /// Points at the free case the priority rates highest. With colours on the board a
        /// free case is not automatically a good one: the one that boards the cart is.
        /// </summary>
        public bool ShowHint(Func<PuzzlePiece, int> priority)
        {
            ClearHint();

            var pick = PickFree(priority);
            if (pick == null) return false;

            _hinted = pick;
            pick.PlayHint(hintDuration);
            return true;
        }

        /// <summary>The free case the priority rates highest, ties broken at random. Null if none can move.</summary>
        public PuzzlePiece PickFree(Func<PuzzlePiece, int> priority)
        {
            PuzzlePiece pick = null;
            int best = int.MinValue, seen = 0;

            foreach (var piece in _allPieces)
            {
                if (!piece.CanEscape) continue;

                int score = priority != null ? priority(piece) : 0;
                if (score < best) continue;
                if (score > best) { best = score; seen = 0; }

                seen++;
                // Reservoir sampling: one pass, no list, and a second hint on the same board
                // is unlikely to point at the case the player has just been shown.
                if (UnityEngine.Random.Range(0, seen) == 0) pick = piece;
            }

            return pick;
        }

        /// <summary>Whether any case that can leave right now is this colour.</summary>
        public bool AnyFreeOfColour(int colour)
        {
            foreach (var piece in _allPieces)
                if (piece.CanEscape && piece.ColourIndex == colour) return true;
            return false;
        }

        /// <summary>Cases still sliding or hopping off the board, not yet handed to the rules.</summary>
        public int DepartingCount => _departing.Count;
        public void ClearHint()
        {
            if (_hinted != null) _hinted.StopHint();
            _hinted = null;
        }

        // Snaps the conveyor onto the rectangle pieces are about to be driven along, and
        // takes back the two numbers only the belt can answer: how high its surface is and
        // where its mouths are. Hand-set values for either are overwritten on purpose —
        // they were measurements of some earlier board, and a stale one is worse than none.
        public void FitBelt()
        {
            if (belt == null) belt = GetComponentInChildren<BeltRing>();
            if (belt == null) return;

            // Usable before a level is loaded, from editor tooling, where the run-time
            // field size has not been worked out yet.
            int w = _width > 0 ? _width : (fieldSize.x > 0 ? fieldSize.x : previewGrid.x);
            int h = _height > 0 ? _height : (fieldSize.y > 0 ? fieldSize.y : previewGrid.y);

            if (!belt.Fit(transform, w, h, cellSize, roadMargin)) return;

            // The route runs in board-local heights, and the board is only ever yawed, so
            // the drop from world to local is the board's own height.
            beltHeight = belt.SurfaceHeight - transform.position.y;

            var lanes = new Transform[belt.Outfeeds.Count];
            for (int i = 0; i < lanes.Length; i++) lanes[i] = belt.Outfeeds[i];
            outfeeds = lanes;
        }

        void SpawnPiece(Vector2Int pos, Direction dir, int variantIndex, int cells = 1, int colour = -1)
        {
            cells = Mathf.Max(1, cells);

            // A long piece trails backwards out of its front cell, so the tail has to be on
            // the board and empty. Dropped rather than nudged into place: sliding it forward
            // would quietly change the puzzle the level describes, and a level that cannot be
            // laid out as authored is one to fix rather than to guess at.
            if (!Fits(pos, dir, cells))
            {
                Debug.LogError($"Board: a {cells}-cell piece at {pos} facing {dir} does not fit; skipped.");
                return;
            }

            int variant = colour >= 0 ? VariantForColour(colour, variantIndex, cells)
                                      : PickVariant(pos, variantIndex, cells);
            var prefab = modelLibrary != null ? modelLibrary.GetVariant(variant) : null;
            if (prefab == null)
            {
                Debug.LogError($"Board: no model registered at variant index {variant}");
                return;
            }

            // Centred over all its cells, which for a single one is that cell.
            Vector3 worldPos = GridToWorld(pos, dir, cells);
            var go = Instantiate(prefab, worldPos, Quaternion.identity, transform);

            // Before the fit, not after: FitToCell reads the model's length off its Z and its
            // width off its X, so a model turned afterwards would already have been sized as
            // though its long side were the short one.
            if (modelLibrary != null) TurnModel(go, modelLibrary.GetYaw(variant));

            bool diagonal = DirectionUtil.IsDiagonal(dir);
            float thicken = modelLibrary != null ? modelLibrary.GetModelScale(variant).z : 1f;
            FitToCell(go, cells, diagonal ? (thicken > 0.01f ? thicken : 1f) : 0f);
            EnsureTapTarget(go, cells);
            if (diagonal) NarrowTapTarget(go);

            var piece = go.GetComponent<PuzzlePiece>();
            if (piece == null)
            {
                piece = go.AddComponent<PuzzlePiece>();

                // Every model in this theme arrives as a plain mesh with no PuzzlePiece of its
                // own, so the pose is decided here for all of them. They lie down on the belt,
                // as they always have -- but *which way* they lie cannot be one fixed rotation,
                // because it depends on which of the model's own axes is the thin one.
                piece.beltLieRotation = LieRotationFor(go);
            }

            // Set outside the block above: a themed prefab that brings its own PuzzlePiece
            // still wants the library's trim, since being out of scale with its neighbours is
            // the library's business and not the prefab's.
            if (modelLibrary != null)
            {
                piece.modelScale = modelLibrary.GetModelScale(variant);

                // Set before SetTint below, which is what reads it.
                piece.bodyMaterial = modelLibrary.GetBodyMaterial(variant);
            }
            // The blocked hop is not set here any more. It used to be scaled by the fit
            // factor, on the reasoning that a hop authored for one board is wrong on
            // another -- true, but the fit factor is how far the *prefab* had to be scaled,
            // not how big the piece ends up. Those agree only while a pack's models are all
            // drawn at one size, and this one's are not: the long bag's mesh is a tenth of
            // the cases', so its factor was ten times theirs and the nudge came out a leap
            // three cells high. The piece measures its own height instead.
            piece.Init(variant, pos, dir, cells);
            piece.Id = _nextPieceId++;

            if (colour >= 0)
            {
                // The colour is the rule now, not decoration, so it is painted even from a
                // library that brought no palette of its own.
                piece.ColourIndex = colour;
                piece.SetTint(ColourOf(colour));
            }
            else if (modelLibrary != null && modelLibrary.PaletteCount > 0)
            {
                piece.ColourIndex = PickColour(pos);
                piece.SetTint(modelLibrary.GetColour(piece.ColourIndex));
            }

            foreach (var cell in piece.OccupiedCells()) _occupied[cell] = piece;
            _allPieces.Add(piece);
        }

        [Header("Tapping a raw model")]
        [Tooltip("Share of a cell the tap box spans across the piece. Matches the figure " +
                 "SheepGame > Build Suitcase Piece bakes into the prefabs it makes.")]
        [Range(0.3f, 1.2f)] public float tapWidth = 0.86f;
        [Tooltip("Height of the tap box, as a share of a cell.")]
        [Range(0.2f, 2f)] public float tapHeight = 0.6f;

        // A model built by SheepGame > Build Suitcase Piece carries its own tap box, sized
        // when the prefab was made. A raw .fbx dropped straight into the library carries
        // none -- and a piece that cannot be tapped is a piece that cannot be played, which
        // is a quiet way to lose an afternoon.
        //
        // Sized in the root's own space: FitToCell has already scaled it, and a collider is
        // scaled along with everything else, so the world size wanted has to be divided back
        // out first. Length runs along the facing, which at identity is local Z.
        void EnsureTapTarget(GameObject go, int cells)
        {
            if (go.GetComponentInChildren<Collider>() != null) return;

            var scale = go.transform.localScale;
            float sx = Mathf.Abs(scale.x) < 0.0001f ? 1f : Mathf.Abs(scale.x);
            float sy = Mathf.Abs(scale.y) < 0.0001f ? 1f : Mathf.Abs(scale.y);
            float sz = Mathf.Abs(scale.z) < 0.0001f ? 1f : Mathf.Abs(scale.z);

            float width = tapWidth * cellSize;
            float length = tapWidth * cellSize * Mathf.Max(1, cells);
            float height = tapHeight * cellSize;

            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(width / sx, height / sy, length / sz);
            box.center = new Vector3(0f, height / sy * 0.5f, 0f);
        }

        // Turns a model under the piece's own root, which the board keeps for itself: the root
        // is what Direction rotates, so a correction baked there would be overwritten the
        // moment the piece is placed. Position is carried round with the rotation, so a model
        // whose pivot is off to one side swings with it instead of being left behind.
        static void TurnModel(GameObject go, float yaw)
        {
            if (Mathf.Approximately(yaw, 0f)) return;

            var turn = Quaternion.Euler(0f, yaw, 0f);
            for (int i = 0; i < go.transform.childCount; i++)
            {
                var child = go.transform.GetChild(i);
                child.localPosition = turn * child.localPosition;
                child.localRotation = turn * child.localRotation;
            }
        }

        // The rotation that lays a model on its largest face, worked out from what it
        // measures rather than assumed.
        //
        // The stock default tips 90 degrees about X, which is right only when the model's thin
        // axis is Z -- true of the upright cases, whose thickness runs front to back. The wide
        // flat luggage is thin across X instead, and the same rotation stood it on its end
        // instead of laying it down: the giveaway was its world height *growing* on boarding.
        //
        // So: whichever extent is smallest goes up, and a model already flatter than it is wide
        // is left alone.
        static Vector3 LieRotationFor(GameObject go)
        {
            if (!TryMeasure(go, out Bounds bounds)) return new Vector3(90f, 0f, 0f);

            Vector3 size = bounds.size;
            if (size.y <= size.x && size.y <= size.z) return Vector3.zero;     // already flat
            if (size.x <= size.z) return new Vector3(0f, 0f, 90f);            // thin across X
            return new Vector3(90f, 0f, 0f);                                  // thin across Z
        }

        static bool TryMeasure(GameObject go, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
            return any;
        }

        // Scales a freshly spawned piece so its footprint spans the share of a cell the
        // board asks for, and reports the factor used. Measured off the renderers rather
        // than trusted from the prefab: the same model arrives scaled by 1 from one pack
        // and by 9 from another, and only what it actually measures on the board matters.
        //
        // The footprint is the larger of the two ground axes because a piece faces four
        // ways: fit the shorter one and a piece turned side-on overruns its cell.
        //
        // A diagonal case is turned 45 degrees in its cell, so what has to fit is its
        // turned outline, (width + depth) / sqrt 2 each way -- with the depth the library's
        // trim will give it, since that is applied after this. diagonalDepthTrim is that
        // trim, and 0 for a piece that faces straight.
        float FitToCell(GameObject go, int cells = 1, float diagonalDepthTrim = 0f)
        {
            if (pieceFill <= 0f) return 1f;

            Bounds bounds = default;
            bool any = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }

            if (!any) return 1f;

            // Measured per axis rather than off the larger one, because the two are no longer
            // allowed the same room: a piece may be several cells long and is always one cell
            // wide. At identity a piece faces Up, which is +Z, so Z is its length and X its
            // width. For a single cell both allowances are equal and this comes out as exactly
            // the old "fit the larger axis into one cell".
            float along = bounds.size.z, across = bounds.size.x;
            if (along < 0.0001f || across < 0.0001f) return 1f;

            float factor = Mathf.Min(pieceFill * cellSize * Mathf.Max(1, cells) / along,
                                     pieceFill * cellSize / across);
            if (diagonalDepthTrim > 0f)
            {
                float depth = along * diagonalDepthTrim;
                factor = Mathf.Min(factor, pieceFill * cellSize * Mathf.Sqrt(2f) / (across + depth));
            }
            go.transform.localScale *= factor;
            return factor;
        }

        // Authored levels describe the puzzle — where a piece stands and which way it
        // faces — and say nothing about how it looks, so the level editor writes index 0
        // for every one of them. Spreading the library across the board is therefore done
        // here, from the cell position: deterministic, so replaying a level or shuffling
        // it leaves each piece the colour the player already learned it by.
        int PickVariant(Vector2Int pos, int authored, int cells = 1)
        {
            if (modelLibrary == null) return authored;

            // Every model of the right size is a candidate, taken from the whole library
            // rather than from the first modelVariety entries. The variety window says how
            // many *looks* a piece may have; it has nothing to say about which sizes exist,
            // and a long model that happens to sit past the window is still the only thing
            // that fits. Windowing first was a real bug: with variety 5 over a library whose
            // sixth entry is the long suitcase, every two-cell piece silently fell back to a
            // one-cell model.
            var candidates = new List<int>();
            for (int i = 0; i < modelLibrary.VariantCount; i++)
                if (modelLibrary.GetCells(i) == cells) candidates.Add(i);

            // Nothing of that size in the library at all: the authored index is the level's
            // own answer and is trusted over a model that would be the wrong shape.
            if (candidates.Count == 0) return authored;

            // Then trimmed to the allowance, so a board asked for one look still gets one.
            int variety = Mathf.Clamp(modelVariety, 1, candidates.Count);
            if (variety < candidates.Count) candidates.RemoveRange(variety, candidates.Count - variety);

            if (candidates.Count == 1) return candidates[0];

            unchecked
            {
                int h = pos.x * 73856093 ^ pos.y * 19349663;
                h ^= h >> 13;
                return candidates[(h & 0x7fffffff) % candidates.Count];
            }
        }

        /// <summary>The colour a palette index paints, from the library if it has one.</summary>
        public Color ColourOf(int index)
        {
            if (modelLibrary != null && modelLibrary.PaletteCount > index && index >= 0)
                return modelLibrary.GetColour(index);
            return PieceColours.Default[Mathf.Clamp(index, 0, PieceColours.Count - 1)];
        }

        // Each colour always wears the same model. Colour is what the player matches on, and a
        // shape that goes with it is a second way to tell two cases apart -- for a player who
        // cannot tell red from green, the only way. A long case has one model and keeps it.
        int VariantForColour(int colour, int authored, int cells)
        {
            if (modelLibrary == null) return authored;

            var candidates = new List<int>();
            for (int i = 0; i < modelLibrary.VariantCount; i++)
                if (modelLibrary.GetCells(i) == cells) candidates.Add(i);

            if (candidates.Count == 0) return authored;
            return candidates[Mathf.Abs(colour) % candidates.Count];
        }

        // Same idea as PickVariant and deliberately a different hash: with the constants
        // shared, a two-model, two-colour theme would only ever spawn two of the four
        // combinations, and the board would look like it was following a rule.
        int PickColour(Vector2Int pos)
        {
            unchecked
            {
                int h = pos.x * 40503949 ^ pos.y * 27644437;
                h ^= h >> 15;
                return (h & 0x7fffffff) % modelLibrary.PaletteCount;
            }
        }

        // Grid maths is done in the board's own space and converted on use, so dropping
        // the Board object anywhere in the scene moves the whole apron with it — and
        // rotating it turns the taxiway ring to match.
        Vector3 GridToLocal(Vector2Int pos)
        {
            float halfW = (_width - 1) * 0.5f;
            float halfH = (_height - 1) * 0.5f;
            return new Vector3((pos.x - halfW) * cellSize, 0f, (pos.y - halfH) * cellSize);
        }

        Vector3 GridToWorld(Vector2Int pos) => transform.TransformPoint(GridToLocal(pos));

        // Halfway along the run of cells the piece stands on: its front cell, less half a cell
        // for every cell of length beyond the first.
        Vector3 GridToWorld(Vector2Int front, Direction dir, int cells)
        {
            if (cells <= 1) return GridToWorld(front);

            Vector2Int back = -DirectionUtil.ToVector(dir);
            Vector3 local = GridToLocal(front)
                          + new Vector3(back.x, 0f, back.y) * (cellSize * (cells - 1) * 0.5f);
            return transform.TransformPoint(local);
        }

        Vector3 LocalToWorld(float x, float z) => transform.TransformPoint(new Vector3(x, 0f, z));

        Vector3 LocalToWorld(float x, float y, float z) => transform.TransformPoint(new Vector3(x, y, z));

        // The taxiway ring: a rectangle just outside the apron that pieces travel along
        // to reach the shared exit, which always sits on the front edge. All four are in
        // board-local space.
        public float RingLeft => -((FieldW - 1) * 0.5f * cellSize) - roadMargin;
        public float RingRight => ((FieldW - 1) * 0.5f * cellSize) + roadMargin;
        public float RingFront => -((FieldH - 1) * 0.5f * cellSize) - roadMargin;
        public float RingBack => ((FieldH - 1) * 0.5f * cellSize) + roadMargin;

        // Before a level has loaded (editor tooling) the field is whatever it is pinned to.
        int FieldW => _width > 0 ? _width : (fieldSize.x > 0 ? fieldSize.x : previewGrid.x);
        int FieldH => _height > 0 ? _height : (fieldSize.y > 0 ? fieldSize.y : previewGrid.y);

        public Vector3 ComputeGatePosition() => LocalToWorld(0f, beltHeight, RingFront);

        // Usable outside play mode (before _width/_height are populated by LoadLevel),
        // e.g. from editor setup tooling that needs to place the gate object.
        public Vector3 ComputeGatePosition(int gridWidth, int gridHeight)
        {
            float front = -((gridHeight - 1) * 0.5f * cellSize) - roadMargin;
            return LocalToWorld(0f, beltHeight, front);
        }

        public void ClearBoard()
        {
            // Dropped before the pieces are, or _hinted is left pointing at a destroyed one.
            ClearHint();

            foreach (var p in _allPieces)
                Retire(p);
            _allPieces.Clear();
            _occupied.Clear();

            // Pieces mid-departure have already left _allPieces, so they need clearing
            // separately or they would keep taxiing across the next level.
            StopAllCoroutines();
            foreach (var p in _departing)
                Retire(p);
            _departing.Clear();
            _slides.Clear();

            // Cases already handed to the belt belong to it, not to the board.
            if (checkIn != null) checkIn.Clear();
        }

        /// <summary>The camera the board is seen through.</summary>
        public Camera ViewCamera => raycastCamera != null ? raycastCamera : Camera.main;

        // Destroy only takes effect at the end of the frame, which leaves the outgoing
        // level's colliders live while the incoming one is already being built: a tap
        // landing in that window raycasts a piece the player has just cleared.
        // Deactivating first takes it out of physics and rendering straight away.
        static void Retire(PuzzlePiece piece)
        {
            if (piece == null) return;
            piece.gameObject.SetActive(false);
            Destroy(piece.gameObject);
        }

        bool InBounds(Vector2Int pos) => pos.x >= 0 && pos.x < _width && pos.y >= 0 && pos.y < _height;

        /// <summary>Whether a piece of this length can stand here facing this way.</summary>
        bool Fits(Vector2Int front, Direction dir, int cells)
        {
            Vector2Int back = -DirectionUtil.ToVector(dir);
            for (int i = 0; i < cells; i++)
            {
                Vector2Int cell = front + back * i;
                if (!InBounds(cell) || _occupied.ContainsKey(cell)) return false;
            }
            return true;
        }

        public bool HasClearPath(PuzzlePiece piece)
        {
            Vector2Int dir = DirectionUtil.ToVector(piece.Direction);
            Vector2Int pos = piece.GridPos + dir;
            while (InBounds(pos))
            {
                if (_occupied.ContainsKey(pos)) return false;
                pos += dir;
            }
            return true;
        }

        void RecomputeEscapable()
        {
            foreach (var p in _allPieces)
                p.SetEscapable(HasClearPath(p));
        }

        // A tap box turned 45 degrees with its case reaches into the four cells round it, so
        // a diagonal case's box is cut down to what fits its own cell turned: 1/sqrt 2.
        static void NarrowTapTarget(GameObject go)
        {
            if (go.GetComponentInChildren<Collider>() is BoxCollider box)
            {
                var size = box.size;
                box.size = new Vector3(size.x * 0.72f, size.y, size.z * 0.72f);
            }
        }

        // Where the finger went down, so the release can be judged against it. A tap is a
        // press and a release in the same place; anything else is somebody else's gesture.
        bool _pressLive;
        bool _speedHoldActive;
        Vector2 _pressPos;
        float _pressTime;

        // A press that was live when the board went away has no release worth waiting for.
        void OnDisable() => CancelPress();

        void OnApplicationFocus(bool focused)
        {
            if (!focused) CancelPress();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) CancelPress();
        }

        // A press is answered on release rather than on contact. On a phone the top and bottom
        // edges belong to iOS — a swipe down from the top right opens Control Centre, a swipe
        // up from the bottom leaves the app — and both of those begin as a finger landing on
        // the board. Acting on contact spent a tap, and sent a case away, every time the player
        // reached for the shade. Acting on release costs nothing a player can feel and gives
        // the gesture somewhere to go.
        void Update()
        {
            bool pressed = false, released = false, held = false;
            Vector2 position = Vector2.zero;

            if (Mouse.current != null && (Mouse.current.leftButton.isPressed ||
                                          Mouse.current.leftButton.wasReleasedThisFrame))
            {
                pressed = Mouse.current.leftButton.wasPressedThisFrame;
                released = Mouse.current.leftButton.wasReleasedThisFrame;
                held = Mouse.current.leftButton.isPressed;
                position = Mouse.current.position.ReadValue();
            }
            else if (Touchscreen.current != null)
            {
                var touch = Touchscreen.current.primaryTouch;
                pressed = touch.press.wasPressedThisFrame;
                released = touch.press.wasReleasedThisFrame;
                held = touch.press.isPressed;
                if (pressed || released || held) position = touch.position.ReadValue();
            }

            if (pressed) BeginPress(position);
            if (_pressLive && held) TickPress(position);
            if (released) EndPress(position);
        }

        void BeginPress(Vector2 position)
        {
            _pressLive = false;

            // The system has just handed control back — the touch that dismissed the shade or
            // the app switcher is still travelling, and it is not a move the player chose.
            if (DeviceRuntime.InputBlocked) return;

            // Began inside the strip iOS keeps for its own gestures. Whatever it turns into,
            // it is not a tap on a suitcase.
            if (DeviceRuntime.InSystemGestureEdge(position)) return;

            // A tap that landed on the HUD must not also reach the board underneath it —
            // otherwise pressing Shuffle sends whichever piece sits behind the button away.
            // Asked on the way down, while the pointer is still the one the UI is tracking.
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            _pressLive = true;
            _pressPos = position;
            _pressTime = Time.unscaledTime;

            // Warmed on the way down so the knock on the way up lands with the case, not a
            // beat behind it. The taptic engine idles, and waking it is what costs the time.
            Haptics.Prepare();
        }

        void TickPress(Vector2 position)
        {
            if (!WithinTapSlop(position))
            {
                CancelPress();
                return;
            }

            if (!_speedHoldActive && Time.unscaledTime - _pressTime >= speedHoldSeconds)
            {
                _speedHoldActive = true;
                SpeedHoldChanged?.Invoke(true);
            }
        }

        void EndPress(Vector2 position)
        {
            if (!_pressLive) return;

            bool wasSpeedHold = _speedHoldActive;
            _pressLive = false;
            SetSpeedHold(false);

            if (DeviceRuntime.InputBlocked) return;
            // A hold is its own gesture. Releasing it must not also spend a tap or move a case.
            if (wasSpeedHold) return;
            if (Time.unscaledTime - _pressTime > tapHoldSeconds) return;

            // Asked again on the way up. A panel can arrive between the two — the taps ran
            // out, or the system interrupted — and a release into whatever is now covering
            // the board must not reach through it.
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            // Screen.dpi is 0 on some desktops and simulators; 160 is the density Unity itself
            // falls back to, and on a mouse the slop hardly matters anyway.
            if (!WithinTapSlop(position)) return;

            var cam = raycastCamera != null ? raycastCamera : Camera.main;
            if (cam == null) return;

            Ray ray = cam.ScreenPointToRay(position);
            if (Physics.Raycast(ray, out var hit, 1000f))
            {
                var piece = hit.collider.GetComponentInParent<PuzzlePiece>();
                if (piece != null) HandlePieceClicked(piece);
            }
        }

        bool WithinTapSlop(Vector2 position)
        {
            // Screen.dpi is 0 on some desktops and simulators; 160 is the density Unity itself
            // falls back to, and on a mouse the slop hardly matters anyway.
            float dpi = Screen.dpi > 1f ? Screen.dpi : 160f;
            return (position - _pressPos).sqrMagnitude <= Mathf.Pow(tapSlopInches * dpi, 2f);
        }

        void CancelPress()
        {
            _pressLive = false;
            SetSpeedHold(false);
        }

        void SetSpeedHold(bool active)
        {
            if (_speedHoldActive == active) return;
            _speedHoldActive = active;
            SpeedHoldChanged?.Invoke(active);
        }

        public void HandlePieceClicked(PuzzlePiece piece)
        {
            _gameManager?.OnPieceClicked(piece);
        }

        public bool TryEscape(PuzzlePiece piece)
        {
            if (!_allPieces.Contains(piece) || !HasClearPath(piece)) return false;
            RemovePieceInternal(piece, slide: true);
            return true;
        }

        // A case taken by the booster is usually walled in, so it cannot slide anywhere without
        // driving through its neighbours. It hops out where it stands instead.
        public void ForceRemove(PuzzlePiece piece)
        {
            if (!_allPieces.Contains(piece)) return;
            RemovePieceInternal(piece, slide: false);
        }

        void RemovePieceInternal(PuzzlePiece piece, bool slide)
        {
            // The board has changed, so any hint standing on it is now about the old one —
            // and if this is the hinted piece, its swell has to come off before it departs.
            ClearHint();

            // The lane is read before the cells are freed, while the piece still says where it is.
            // A case hopping out in place still gets one, covering only its own cells, so that
            // nothing slides through it during the hop.
            var lane = LaneOf(piece);
            if (!slide)
            {
                lane.Lane = new List<Vector2Int>(piece.OccupiedCells());
                lane.InPlace = true;
                lane.Moving = true;
            }

            foreach (var cell in piece.OccupiedCells()) _occupied.Remove(cell);
            _allPieces.Remove(piece);
            _departing.Add(piece);

            if (rideBelt && checkIn != null) StartCoroutine(AnimateToCheckIn(piece, lane));
            else if (rideBelt && slide) StartCoroutine(AnimateExit(piece));
            else StartCoroutine(AnimateLeave(piece, lane));

            RecomputeEscapable();
            OnBoardChanged?.Invoke();

            if (_allPieces.Count == 0)
                OnBoardEmpty?.Invoke();
        }

        IEnumerator AnimateExit(PuzzlePiece piece)
        {
            piece.SetMoving(true);
            // Clear of the case's own height: at a third of a cell the burst goes off inside
            // the suitcase and the player sees a few sparks leak out of the seams.
            SheepGame.Fx.PieceEffects.Sparkle(piece.transform.position + Vector3.up * cellSize * 0.95f,
                                              piece.ThemeColour);

            // Leg 1: roll straight off the apron, in the facing direction, up to the lane
            // that wraps around the board — then jump the last stretch onto it.
            Direction dir = piece.Direction;
            Vector3 local = transform.InverseTransformPoint(piece.transform.position);
            Vector3 ringEntry = dir switch
            {
                Direction.Up => new Vector3(local.x, beltHeight, RingBack),
                Direction.Down => new Vector3(local.x, beltHeight, RingFront),
                Direction.Left => new Vector3(RingLeft, beltHeight, local.z),
                Direction.Right => new Vector3(RingRight, beltHeight, local.z),
                _ => local
            };

            // Which side of the ring this piece will come round, decided once so the route
            // and the lane it ends up in cannot disagree.
            bool leftSide = dir switch
            {
                Direction.Left => true,
                Direction.Right => false,
                Direction.Up => ringEntry.x <= 0f,
                _ => local.x <= 0f
            };

            Vector2Int step = DirectionUtil.ToVector(dir);
            Vector3 heading = new Vector3(step.x, 0f, step.y);

            // piece.transform.position is the centre of the whole footprint, not the front
            // cell -- "centred over all its cells, which for a single one is that cell" (see
            // SpawnPiece). So the piece's leading edge sits half its own length ahead of the
            // point this coroutine is actually moving, and a two-cell case's nose reaches a
            // full cell further forward than a one-cell case's for the same target. Left
            // alone, reach was measured to that point regardless of length, so a long piece's
            // nose was already over the ring — inside the belt's footprint — before the hop
            // that is supposed to carry it there had even started.
            //
            // extra is how much further back the centre has to stop to keep the nose at the
            // same distance from the ring a one-cell piece manages. It eats into the reach
            // budget rather than adding to it, so the total setback still respects the road
            // margin -- overrunning that would start the run-up back on the board itself.
            float extra = cellSize * (piece.Cells - 1) * 0.5f;
            float reach = Mathf.Clamp(hopDistance, 0f, Mathf.Max(0f, roadMargin * 0.9f - extra));
            Vector3 liftOff = ringEntry - heading * (reach + extra);
            liftOff.y = 0f;

            yield return MoveTo(piece, transform.TransformPoint(liftOff), rotateToFace: false);
            if (piece != null)
                yield return HopOntoBelt(piece, transform.TransformPoint(liftOff),
                                                transform.TransformPoint(ringEntry));

            // Leg 2: ride the lane around the perimeter to the mouth it leaves by.
            foreach (var corner in BuildRingRoute(dir, ringEntry, leftSide))
            {
                if (piece == null) break;
                yield return MoveTo(piece, transform.TransformPoint(corner), rotateToFace: true);
            }

            var outfeed = PickOutfeed(leftSide);
            bool useSpline = departurePath != null && departurePath.IsValid;
            Vector3 threshold = useSpline ? departurePath.StartPosition
                              : outfeed != null ? TurnOffFor(outfeed)
                              : exitPoint != null ? exitPoint.position
                                                  : ComputeGatePosition();
            if (piece != null)
                yield return MoveTo(piece, threshold, rotateToFace: true);

            // Leg 3 for a belt theme: straight into its mouth and gone.
            if (piece != null && !useSpline && outfeed != null)
            {
                yield return MoveTo(piece, outfeed.position, rotateToFace: true);
                if (piece != null)
                {
                    _departing.Remove(piece);
                    PieceArrived?.Invoke(piece, piece.transform.position);
                    Destroy(piece.gameObject);
                }
                yield break;
            }

            // Leg 3: drive the authored departure route, or fall back to a straight
            // roll between the two markers when no path has been laid out.
            if (piece != null && useSpline)
                yield return FollowDeparturePath(piece);
            else if (piece != null && runwayEnd != null)
                yield return TakeOffRoll(piece, threshold, runwayEnd.position);

            if (piece != null)
            {
                _departing.Remove(piece);
                PieceArrived?.Invoke(piece, piece.transform.position);
                Destroy(piece.gameObject);
            }
        }

        // ---- leaving onto the belt, to the check-in lane ------------------------------------

        // Slide to the edge of the board, hop onto the ring, and hand the case to the lane,
        // which rides it round and tells the board when it has arrived. The case stays in
        // _departing -- "on its way, not yet handed to the rules" -- until then.
        //
        // The slide keeps the lane-crossing rule of AnimateLeave: its cells stay claimed until
        // it is in the air, so nothing slides through a case waiting at the edge for a gap in
        // the traffic.
        IEnumerator AnimateToCheckIn(PuzzlePiece piece, Slide slide)
        {
            piece.SetMoving(true);
            _slides.Add(slide);

            Direction side;
            if (!slide.InPlace)
            {
                while (piece != null && Blocked(slide)) yield return null;
                if (piece == null) { _slides.Remove(slide); yield break; }

                slide.Moving = true;
                side = ExitSide(slide);
                SheepGame.Fx.PieceEffects.Sparkle(piece.transform.position + Vector3.up * cellSize * 0.95f,
                                                  piece.ThemeColour);

                // Up to the edge: its nose at the last cell's outer face. A diagonal step is a
                // cell and a half long, so distance is counted in steps, not cells.
                int ahead = slide.Lane.Count - slide.Cells;
                Vector2Int step = DirectionUtil.ToVector(slide.Dir);
                float stepLength = cellSize * step.magnitude;
                float distance = ahead * stepLength;

                Vector3 from = piece.transform.localPosition;
                Vector3 to = from + new Vector3(step.x, 0f, step.y) * (ahead * cellSize);
                float speed = Mathf.Max(0.5f, slideCellsPerSecond) * cellSize;
                float travelled = 0f;

                while (piece != null && travelled < distance)
                {
                    float ramp = Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(travelled / cellSize));
                    travelled = Mathf.Min(distance, travelled + speed * ramp * Time.deltaTime);
                    piece.transform.localPosition = Vector3.Lerp(from, to, travelled / distance);
                    slide.Progress = travelled / stepLength;
                    yield return null;
                }
                if (piece == null) { _slides.Remove(slide); yield break; }
            }
            else
            {
                // Taken by the booster, usually walled in: it jumps from where it stands to
                // the nearest stretch of belt.
                Vector3 l = transform.InverseTransformPoint(piece.transform.position);
                float toLeft = l.x - RingLeft, toRight = RingRight - l.x;
                float toFront = l.z - RingFront, toBack = RingBack - l.z;
                float best = Mathf.Min(toLeft, Mathf.Min(toRight, Mathf.Min(toFront, toBack)));
                side = best == toFront ? Direction.Down
                     : best == toLeft ? Direction.Left
                     : best == toRight ? Direction.Right
                                       : Direction.Up;
            }

            Vector3 local = transform.InverseTransformPoint(piece.transform.position);
            Vector3 landing = side switch
            {
                Direction.Up => new Vector3(local.x, beltHeight, RingBack),
                Direction.Down => new Vector3(local.x, beltHeight, RingFront),
                Direction.Left => new Vector3(RingLeft, beltHeight, local.z),
                _ => new Vector3(RingRight, beltHeight, local.z),
            };

            // A diagonal case carries on along its own heading to the belt rather than
            // squaring off to it, so the hop continues the slide instead of turning a corner.
            if (!slide.InPlace && DirectionUtil.IsDiagonal(slide.Dir))
                landing = AlongToRing(local, DirectionUtil.ToVector(slide.Dir), side);
            Vector3 landingWorld = transform.TransformPoint(landing);

            while (piece != null && !checkIn.LandingClear(landingWorld, piece)) yield return null;
            if (piece == null) { _slides.Remove(slide); yield break; }

            var route = checkIn.Route(side, landing);
            checkIn.Reserve(piece, route);

            Vector3 liftOff = piece.transform.position;
            // A booster case may be jumping half the board; give it the air time to match.
            float air = hopTime * Mathf.Clamp(Vector3.Distance(liftOff, landingWorld) / Mathf.Max(0.1f, roadMargin), 1f, 1.8f);
            yield return HopOntoBelt(piece, liftOff, landingWorld, air);

            _slides.Remove(slide);
            if (piece == null) yield break;

            checkIn.Ride(piece, arrived =>
            {
                if (arrived == null) return;
                _departing.Remove(arrived);
                PieceArrived?.Invoke(arrived, arrived.transform.position);
            });
        }

        // ---- leaving without the belt ---------------------------------------------------

        // Every cell the case covers and every cell between its front and the edge, in grid
        // coordinates.
        Slide LaneOf(PuzzlePiece piece)
        {
            var dir = DirectionUtil.ToVector(piece.Direction);
            var lane = new List<Vector2Int>(piece.OccupiedCells());
            for (var p = piece.GridPos + dir; InBounds(p); p += dir) lane.Add(p);

            return new Slide
            {
                Piece = piece,
                Dir = piece.Direction,
                Start = piece.GridPos,
                Cells = piece.Cells,
                Lane = lane,
            };
        }

        // Which stretch of belt a case lands on. Straight, the one it faces; diagonal, the edge
        // its last cell leaves through -- the top or bottom when it runs out of rows first.
        Direction ExitSide(Slide s)
        {
            if (!DirectionUtil.IsDiagonal(s.Dir)) return s.Dir;

            Vector2Int step = DirectionUtil.ToVector(s.Dir);
            Vector2Int past = s.Lane[s.Lane.Count - 1] + step;
            if (past.y < 0 || past.y >= _height) return step.y > 0 ? Direction.Up : Direction.Down;
            return step.x > 0 ? Direction.Right : Direction.Left;
        }

        // Where a heading from this point meets that side of the belt, kept within its ends.
        Vector3 AlongToRing(Vector3 local, Vector2Int step, Direction side)
        {
            float t = side switch
            {
                Direction.Up => (RingBack - local.z) / step.y,
                Direction.Down => (RingFront - local.z) / step.y,
                Direction.Left => (RingLeft - local.x) / step.x,
                _ => (RingRight - local.x) / step.x,
            };
            Vector3 p = local + new Vector3(step.x, 0f, step.y) * Mathf.Max(0f, t);
            return new Vector3(Mathf.Clamp(p.x, RingLeft, RingRight), beltHeight,
                               Mathf.Clamp(p.z, RingFront, RingBack));
        }

        // How far along its own direction a cell is from where the case started. The case's
        // tail is at -(cells-1), the first cell past the edge is the lane's length.
        static int Along(Slide s, Vector2Int cell)
        {
            var d = DirectionUtil.ToVector(s.Dir);
            var delta = cell - s.Start;
            return (delta.x * d.x + delta.y * d.y) / (d.x * d.x + d.y * d.y);
        }

        // Whether a moving case still has this cell in front of it, or is still on it.
        static bool StillCovers(Slide moving, Vector2Int cell) =>
            Along(moving, cell) >= Mathf.FloorToInt(moving.Progress) - moving.Cells;

        // A case following another down the same row the same way can never catch it -- it set
        // off later, at the same speed -- so once the leader is moving the follower need not
        // wait. Anything crossing does, and so does anything behind a case that has not set off.
        static bool Conflicts(Slide mine, Slide earlier)
        {
            if (earlier.Moving && !earlier.InPlace && mine.Dir == earlier.Dir)
            {
                // Same line: the offset between the two starts runs along the direction.
                Vector2Int d = DirectionUtil.ToVector(mine.Dir), gap = mine.Start - earlier.Start;
                if (gap.x * d.y - gap.y * d.x == 0)
                    return false;
            }

            foreach (var cell in mine.Lane)
                if (earlier.Lane.Contains(cell) && StillCovers(earlier, cell)) return true;
            return false;
        }

        // Only the cases that set off first can hold this one up. A case waiting its turn still
        // stands in its cells -- the board freed them the moment it was tapped, so nothing else
        // knows it is there -- and first come, first served means two can never wait on each other.
        bool Blocked(Slide me)
        {
            foreach (var other in _slides)
            {
                if (other == me) return false;
                if (Conflicts(me, other)) return true;
            }
            return false;
        }

        IEnumerator AnimateLeave(PuzzlePiece piece, Slide slide)
        {
            piece.SetMoving(true);

            _slides.Add(slide);

            if (!slide.InPlace)
            {
                // Held until nothing crossing its way is still on the move. Not long: a slide
                // across the whole board is about a second.
                while (piece != null && Blocked(slide)) yield return null;
                if (piece == null) { _slides.Remove(slide); yield break; }

                slide.Moving = true;
                SheepGame.Fx.PieceEffects.Sparkle(piece.transform.position + Vector3.up * cellSize * 0.95f,
                                                  piece.ThemeColour);
                SheepGame.Audio.AudioManager.PieceLeave();

                // Far enough that the whole case, tail and all, is past the last cell.
                int toEdge = slide.Lane.Count - slide.Cells + 1;
                float cells = toEdge + slide.Cells - 1;
                float distance = cells * cellSize;

                Vector2Int step = DirectionUtil.ToVector(slide.Dir);
                Vector3 from = piece.transform.localPosition;
                Vector3 to = from + new Vector3(step.x, 0f, step.y) * distance;

                float speed = Mathf.Max(0.5f, slideCellsPerSecond) * cellSize;
                float travelled = 0f;

                while (piece != null && travelled < distance)
                {
                    // Eased in over the first cell, so it sets off rather than teleporting to speed.
                    float ramp = Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(travelled / cellSize));
                    travelled = Mathf.Min(distance, travelled + speed * ramp * Time.deltaTime);
                    piece.transform.localPosition = Vector3.Lerp(from, to, travelled / distance);
                    slide.Progress = travelled / cellSize;
                    yield return null;
                }

                // Off the board: nothing on it can meet this case any more.
                _slides.Remove(slide);
            }

            if (piece == null) { _slides.Remove(slide); yield break; }

            // Handed over at the start of the hop, so whatever picks the case up on screen sets
            // off from where the player's eye already is.
            _departing.Remove(piece);
            PieceArrived?.Invoke(piece, piece.transform.position);

            Vector3 start = piece.transform.position;
            Vector3 startScale = piece.transform.localScale;
            Quaternion startRot = piece.transform.rotation;
            float t = 0f;

            while (piece != null && t < 1f)
            {
                t += Time.deltaTime / Mathf.Max(0.05f, popTime);
                float k = Mathf.Clamp01(t);

                piece.transform.position = start + Vector3.up * Mathf.Sin(k * Mathf.PI * 0.75f) * popHeight * cellSize;
                // Swells a touch first, so it reads as springing away rather than deflating.
                float s = k < 0.25f ? Mathf.Lerp(1f, 1.15f, k / 0.25f) : Mathf.Lerp(1.15f, 0f, (k - 0.25f) / 0.75f);
                piece.transform.localScale = startScale * s;
                piece.transform.rotation = startRot * Quaternion.Euler(0f, 240f * k * k, 0f);
                yield return null;
            }

            _slides.Remove(slide);
            if (piece != null) Destroy(piece.gameObject);
        }

        // Drives the authored spline: taxi pace up to its rollStart, then throttle up,
        // then lift the nose and climb away. Distance is measured along the curve so
        // speed stays honest through corners.
        IEnumerator FollowDeparturePath(PuzzlePiece piece)
        {
            var path = departurePath;
            path.Rebuild();

            float length = path.Length;
            if (length < 0.01f) yield break;

            Quaternion facing = Quaternion.LookRotation(path.DirectionAt(0f), Vector3.up);
            while (piece != null && Quaternion.Angle(piece.transform.rotation, facing) > 1f)
            {
                piece.transform.rotation = Quaternion.RotateTowards(piece.transform.rotation, facing, turnSpeed * Time.deltaTime);
                yield return null;
            }

            bool rolling = false;
            float travelled = 0f;

            while (piece != null && travelled < length)
            {
                float t = travelled / length;

                if (!rolling && t >= path.rollStart)
                {
                    rolling = true;
                    piece.PlayState("TakeOff");
                }

                float speed = t < path.rollStart
                    ? taxiSpeed
                    : Mathf.Lerp(taxiSpeed, takeoffSpeed, Mathf.InverseLerp(path.rollStart, 1f, t));
                travelled += speed * Time.deltaTime;

                float climbT = Mathf.InverseLerp(path.climbStart, 1f, t);
                piece.transform.position = path.PositionAt(travelled) + Vector3.up * path.ClimbOffset(t);
                piece.transform.rotation = Quaternion.LookRotation(path.DirectionAt(travelled), Vector3.up)
                                         * Quaternion.Euler(-path.climbPitch * climbT, 0f, 0f);
                yield return null;
            }
        }

        // Accelerates from taxi speed to rotation speed along the runway centreline,
        // lifting the nose and climbing away once past the rotation point.
        IEnumerator TakeOffRoll(PuzzlePiece piece, Vector3 from, Vector3 to)
        {
            Vector3 ground = new Vector3(to.x - from.x, 0f, to.z - from.z);
            float length = ground.magnitude;
            if (length < 0.01f) yield break;

            Vector3 heading = ground / length;
            Quaternion facing = Quaternion.LookRotation(heading, Vector3.up);

            // Swing onto the centreline before releasing the brakes.
            while (piece != null && Quaternion.Angle(piece.transform.rotation, facing) > 1f)
            {
                piece.transform.rotation = Quaternion.RotateTowards(piece.transform.rotation, facing, turnSpeed * Time.deltaTime);
                yield return null;
            }

            piece.PlayState("TakeOff");

            float travelled = 0f;
            while (piece != null && travelled < length)
            {
                float t = travelled / length;
                travelled += Mathf.Lerp(taxiSpeed, takeoffSpeed, t) * Time.deltaTime;

                float climbT = Mathf.InverseLerp(rotatePoint, 1f, t);
                float height = climbHeight * climbT * climbT;

                piece.transform.position = from + heading * travelled + Vector3.up * height;
                piece.transform.rotation = facing * Quaternion.Euler(-climbPitch * climbT, 0f, 0f);
                yield return null;
            }
        }

        // The jump onto the conveyor. Board arcs the piece through the air; the piece plays
        // its own crouch-leap-flop against the same clock, so a theme with nothing to flop
        // simply sails across in whatever pose it already had.
        IEnumerator HopOntoBelt(PuzzlePiece piece, Vector3 from, Vector3 to, float duration = -1f)
        {
            float time = duration > 0f ? duration : hopTime;
            piece.PlayBeltBoarding(time);
            // At the leap itself, not the tap that caused it -- the tap already has its own
            // click (see GameManager.OnPieceClicked), and this one has to sit under it.
            SheepGame.Audio.AudioManager.PieceLeave();

            // Anticipation: the first slice of the clock holds the piece still while it
            // gathers itself, so the leap has something to read as a leap from.
            const float crouch = 0.18f;

            float t = 0f;
            while (piece != null && t < 1f)
            {
                t += Time.deltaTime / Mathf.Max(0.05f, time);
                float travel = Mathf.InverseLerp(crouch, 1f, Mathf.Clamp01(t));
                travel = travel * travel * (3f - 2f * travel);

                Vector3 p = Vector3.Lerp(from, to, travel);
                p.y += Mathf.Sin(travel * Mathf.PI) * hopArc;
                piece.transform.position = p;
                yield return null;
            }

            if (piece != null)
            {
                piece.transform.position = to;
                SheepGame.Fx.PieceEffects.Landing(to, piece.ThemeColour);
                SheepGame.Audio.AudioManager.PieceLand();
            }
        }

        // Bags leave by the mouth on the side they came round, which keeps a two-mouthed
        // claim belt fed from both instead of funnelling everything through the middle.
        Transform PickOutfeed(bool leftSide)
        {
            if (outfeeds == null || outfeeds.Length == 0) return null;

            Transform best = null;
            float bestX = 0f;
            foreach (var candidate in outfeeds)
            {
                if (candidate == null) continue;
                float x = transform.InverseTransformPoint(candidate.position).x;
                if (best == null || (leftSide ? x < bestX : x > bestX))
                {
                    best = candidate;
                    bestX = x;
                }
            }
            return best;
        }

        // Where a lane leaves the ring: straight back from its mouth, so moving the mouth
        // moves its turn-off with it and the two never drift apart.
        Vector3 TurnOffFor(Transform outfeed) =>
            LocalToWorld(transform.InverseTransformPoint(outfeed.position).x, beltHeight, RingFront);

        // Board-local corners along the perimeter lane, from where a piece gets on round to
        // the front edge. The mouth itself is a scene Transform and is appended by the
        // caller in world space.
        IEnumerable<Vector3> BuildRingRoute(Direction exitDir, Vector3 ringEntry, bool leftSide)
        {
            switch (exitDir)
            {
                case Direction.Down:
                    break;

                case Direction.Left:
                    yield return new Vector3(RingLeft, beltHeight, RingFront);
                    break;

                case Direction.Right:
                    yield return new Vector3(RingRight, beltHeight, RingFront);
                    break;

                case Direction.Up:
                    float sideX = leftSide ? RingLeft : RingRight;
                    yield return new Vector3(sideX, beltHeight, RingBack);
                    yield return new Vector3(sideX, beltHeight, RingFront);
                    break;
            }
        }

        IEnumerator MoveTo(PuzzlePiece piece, Vector3 target, bool rotateToFace)
        {
            while (piece != null && Vector3.Distance(piece.transform.position, target) > 0.05f)
            {
                Vector3 toTarget = target - piece.transform.position;
                if (rotateToFace && toTarget.sqrMagnitude > 0.0001f)
                {
                    var want = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
                    piece.transform.rotation = Quaternion.RotateTowards(piece.transform.rotation, want, turnSpeed * Time.deltaTime);
                }

                piece.transform.position = Vector3.MoveTowards(piece.transform.position, target, taxiSpeed * Time.deltaTime);
                yield return null;
            }
        }

        // Re-rolls facings but leaves every piece where it stands, so a hand-drawn
        // silhouette survives the booster. The solver picks facings that keep the board
        // clearable, which scattering positions at random could not promise.
        // Draws the parking grid and the taxiway ring the departure spline should meet.
        // Outside play mode the level is not loaded yet, so a preview size stands in.
        void OnDrawGizmos()
        {
            int w = Application.isPlaying && _width > 0 ? _width : previewGrid.x;
            int h = Application.isPlaying && _height > 0 ? _height : previewGrid.y;
            if (w < 1 || h < 1) return;

            float halfW = (w - 1) * 0.5f * cellSize;
            float halfH = (h - 1) * 0.5f * cellSize;

            Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                {
                    Vector3 c = transform.TransformPoint(new Vector3(x * cellSize - halfW, 0f, y * cellSize - halfH));
                    Gizmos.DrawWireCube(c, new Vector3(cellSize * 0.9f, 0.1f, cellSize * 0.9f));
                }

            float left = -halfW - roadMargin, right = halfW + roadMargin;
            float front = -halfH - roadMargin, back = halfH + roadMargin;

            Gizmos.color = new Color(0.2f, 1f, 0.6f, 0.9f);
            Vector3 a = LocalToWorld(left, front), b = LocalToWorld(right, front);
            Vector3 c2 = LocalToWorld(right, back), d = LocalToWorld(left, back);
            Gizmos.DrawLine(a, b); Gizmos.DrawLine(b, c2); Gizmos.DrawLine(c2, d); Gizmos.DrawLine(d, a);

            // Where the perimeter route hands over to the departure spline.
            if (departurePath != null && departurePath.IsValid)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawLine(LocalToWorld(0f, front), departurePath.StartPosition);
            }
        }

        public void ShuffleRemaining()
        {
            ClearHint();
            if (_allPieces.Count == 0) return;

            // Shapes, not single cells: the old version handed every piece's lone GridPos to
            // the single-cell solver overload, which knows nothing of a longer piece's tail.
            // It could reassign a domino a direction whose new front cell was fine on its
            // own but whose *tail* — the cells behind it, which the plain-cell solver never
            // represents — ran straight through another piece, so two long cases could come
            // out standing inside one another. The board's own colour is not shuffled (only
            // direction reads as random here, colour being decorative), so direction is the
            // whole of what this does and it has to be right for every piece, long or short.
            var shapes = new List<LevelSolver.PieceShape>(_allPieces.Count);
            var owner = new Dictionary<Vector2Int, PuzzlePiece>();
            foreach (var piece in _allPieces)
            {
                var occupied = new List<Vector2Int>(piece.OccupiedCells());
                shapes.Add(new LevelSolver.PieceShape(occupied.ToArray()));
                foreach (var c in occupied) owner[c] = piece;
            }

            int seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
            if (!LevelSolver.TryAssignDirections(shapes, _width, _height, seed, out var assigned))
                return;

            foreach (var entry in assigned)
            {
                if (!owner.TryGetValue(entry.position, out var piece)) continue;

                // A shuffled facing does not always keep the same cell as the front — a
                // domino's two directions along its own axis lead from opposite ends of it —
                // and ReassignPlacement only ever turns a piece in place, so the transform
                // has to be re-centred here or the piece would render off its own footprint.
                piece.transform.position = GridToWorld(entry.position, entry.direction, entry.Cells);
                piece.ReassignPlacement(entry.position, entry.direction);
            }

            RecomputeEscapable();
        }
    }
}
