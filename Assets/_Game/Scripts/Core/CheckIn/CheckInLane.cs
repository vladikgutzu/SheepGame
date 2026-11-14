using System;
using System.Collections.Generic;
using UnityEngine;

namespace SheepGame.Core
{
    /// <summary>
    /// Where a case goes once it has left the board: round the ring, down one arm of the
    /// junction, onto the baggage sorter -- up the spine into the scanner if it wears the
    /// terminal's colour, or out along the sorter into a bay until a cart of its colour pulls in.
    /// </summary>
    // Laid out like Bus Jam's car park (the user's reference, 2026-09-14) and built from belts
    // (see CheckInLaneBuilder): the board fills the bottom of the screen; across the top a sorter
    // belt feeds a row of bays, a collector belt behind them runs to the scanner, and the carts
    // still to come are lined up beside it. A case in a bay leaves from that bay; nothing waits
    // behind anything, and nothing jumps.
    //
    // One MonoBehaviour moves every case, rather than a coroutine per case, because the cases
    // have to keep their distance from one another and that is a question about all of them at
    // once. Each frame they are moved front first, so a follower sees where the case in front
    // of it has already got to.
    //
    // On the ring, "front" is how far a case still has to go to the platform. All routes end at
    // the same point, so that distance is comparable between them, and a case only ever gives
    // way to one nearer the end -- a strict order, so two can never wait on each other at the
    // merge. Past the platform entry the same rule runs on distance travelled from it, and on
    // the last stretch into the terminal on distance still to go.
    //
    // The rules are untouched. They hear about a case when it reaches the platform
    // (Board.PieceArrived) and decide on the spot; this only shows it, with one piece of timing
    // of its own: a case bound for cart N holds until the terminal wears N's colour, and the
    // terminal changes colour when the last case of the cart before has physically gone in.
    public class CheckInLane : MonoBehaviour
    {
        [Header("Route")]
        public Board board;
        [Tooltip("Points from where the left half of the ring leaves its straight, round the junction's " +
                 "turn and along the arm, in travel order.")]
        public Transform[] leftFeed;
        [Tooltip("The same for the right half.")]
        public Transform[] rightFeed;
        [Tooltip("Where the arms have merged and the platform begins. A case arriving here is handed to the rules.")]
        public Transform entry;
        [Tooltip("The middle of the aisle across the platform, between the places and the terminal.")]
        public Transform road;
        [Tooltip("The terminal's opening.")]
        public Transform mouth;

        [Header("Waiting places")]
        [Tooltip("The places, in the order they are filled: nearest the middle first.")]
        public Transform[] places;
        [Tooltip("Marks coloured by each place's state, the same number per place, in place order.")]
        public Renderer[] placeMarks;
        public Color placeFree = new Color(0.45f, 0.92f, 0.62f);
        public Color placeTaken = new Color(1f, 0.72f, 0.36f);
        [Tooltip("Every usable place lights this when the last one is taken: one case from losing.")]
        public Color placeFull = new Color(1f, 0.42f, 0.42f);
        public Color placeLocked = new Color(0.50f, 0.56f, 0.62f);

        [Header("Sorter loop")]
        [Tooltip("The loop's belt and island, which lengthens to hold the bays.")]
        public CheckInLoop loop;
        [Tooltip("One bay per place, index-matched with places. Only as many as the level has places are " +
                 "shown, in a row centred on the loop.")]
        public GameObject[] columns;
        [Tooltip("Distance between two bays, centre to centre.")]
        public float bayPitch = 4.3f;
        [Tooltip("Seconds the row takes to make room for a new bay, and the bay to rise.")]
        public float growTime = 0.35f;

        [Header("Motion")]
        [Tooltip("Speed round the ring, in units per second.")]
        public float rideSpeed = 30f;
        [Tooltip("Speed across the platform.")]
        public float laneSpeed = 24f;
        [Tooltip("Clear space kept between two cases.")]
        public float gap = 0.6f;
        [Tooltip("Corners of every route are rounded to this radius.")]
        public float cornerRadius = 2.4f;
        [Tooltip("Seconds a case takes to disappear into the terminal.")]
        public float enterTime = 0.2f;
        [Tooltip("How far past the opening it travels while it does.")]
        public float enterDepth = 3.5f;
        [Tooltip("Pause with every seat lit before the terminal takes the next colour.")]
        public float fullHold = 0.12f;
        [Tooltip("How far short of the collector a case waits when its cart has not come up yet: half the collector's width and a little.")]
        public float collectorClearance = 2.6f;
        [Tooltip("Largest a waiting case is drawn, as a share of its place's length.")]
        public float placeLength = 5.2f;

        [Header("Terminal and framing")]
        public CheckInTerminal terminal;
        [Tooltip("Ground points the camera must keep in shot besides the board: the platform and the terminal.")]
        public Transform[] frameInclude;

        enum Stage { Boarding, Riding, Hold, ToPlace, Parked, ToTerminal, Entering }

        sealed class Case
        {
            public PuzzlePiece Piece;
            public int Id;
            public Stage Stage;

            public List<Vector3> Path;
            public float[] Cum;
            public float S;
            public float Speed;
            public Action<PuzzlePiece> Arrived;
            // Whether the path it is on began at the platform entry: those keep clear of cases
            // still arriving there.
            public bool FromEntry;
            // How far along its path a case bound for a later cart may come before it waits.
            public float WaitAt;

            public bool Assigned;
            public bool ToTerminalWanted;
            public int Cart = -1;
            public bool Overflow;
            public int Place = -1;

            public float HalfRaw;
            public float Fit = 1f;
            public float ScaleNow = 1f;
            public Vector3 BaseScale;

            public Vector3 From, To;
            public float T, Duration;

            public float Length => Cum[Cum.Length - 1];
            public float Half => HalfRaw * ScaleNow;
        }

        readonly List<Case> _cases = new List<Case>();
        readonly Dictionary<int, Case> _byId = new Dictionary<int, Case>();
        int[] _placeOwner = new int[0];

        ColourMatchRules _rules;
        readonly List<Color> _carts = new List<Color>();
        int _capacity = 3;
        int _visualCart;
        int _entered;
        bool _pendingSwitch;
        float _switchAt;
        float _readyAt;
        float _nextEnterAt;
        int _markState = -1;

        /// <summary>No case is still on its way, and the terminal is not mid-change.</summary>
        public bool Idle => _cases.Count == 0 && !_pendingSwitch && (terminal == null || !terminal.Changing);

        public int Count => _cases.Count;

        /// <summary>How many cases can physically wait. The queue must never be allowed more.</summary>
        public int PlaceCount => places != null ? places.Length : 0;

        int _shownPlaces = -1;
        float[] _columnFrom = new float[0], _columnTo = new float[0];
        bool[] _columnRising = new bool[0];
        float _loopFrom, _loopTo;
        readonly List<float> _slotXs = new List<float>();
        float _layoutStart = -1f;
        float _nextSide = 1f;

        /// <summary>
        /// Shows a bay for each of this many places, in a row centred on the loop, and sizes the loop
        /// to hold them. In play the row slides apart to make room and a new bay rises out of the
        /// island, unless <paramref name="instant"/>.
        /// </summary>
        // Centred for any count: the bays sit at (i - (n-1)/2) pitches from the middle, and the loop's
        // ends are half a pitch past the outer bays. With an odd count the middle bay stands over the
        // entry, which is why a case the terminal wants now goes round the loop rather than up the middle.
        public void ShowPlaces(int count, bool instant)
        {
            int n = Mathf.Clamp(count, 0, PlaceCount);
            if (n == _shownPlaces && !instant) return;
            _shownPlaces = n;

            int total = columns != null ? columns.Length : 0;
            if (_columnFrom.Length != total)
            {
                _columnFrom = new float[total];
                _columnTo = new float[total];
                _columnRising = new bool[total];
            }

            float centre = loop != null ? loop.centreX : 0f;
            for (int i = 0; i < total; i++)
            {
                var column = columns[i];
                if (column == null) continue;
                bool visible = i < n;
                float current = column.transform.localPosition.x;
                float target = visible ? centre + (i - (n - 1) * 0.5f) * bayPitch : current;
                bool appearing = visible && !column.activeSelf;
                column.SetActive(visible);

                if (appearing) current = target;
                _columnFrom[i] = instant ? target : current;
                _columnTo[i] = target;
                _columnRising[i] = appearing && !instant && Application.isPlaying;
            }

            float loopTarget = Mathf.Max(1, n) * bayPitch * 0.5f;
            _loopFrom = instant || loop == null || loop.HalfLength <= 0f ? loopTarget : loop.HalfLength;
            _loopTo = loopTarget;
            _layoutStart = Time.time;
            _markState = -1;

            TickLayout(instant);

            // The loop's length is part of the shot: re-frame for it, gliding when it grew in play.
            if (instant && Application.isPlaying && board != null && board.checkIn == this) board.RefitFrame(immediate: true);
        }

        void TickLayout(bool instant = false)
        {
            if (_layoutStart < 0f) return;
            float k = instant ? 1f : Mathf.Clamp01((Time.time - _layoutStart) / Mathf.Max(0.01f, growTime));
            float e = k * k * (3f - 2f * k);

            for (int i = 0; i < _columnTo.Length; i++)
            {
                var column = columns[i];
                if (column == null || !column.activeSelf) continue;
                var p = column.transform.localPosition;
                p.x = Mathf.Lerp(_columnFrom[i], _columnTo[i], e);
                column.transform.localPosition = p;

                // A new bay rises with a little overshoot, so it pops up rather than slides.
                float y = !_columnRising[i] || k >= 1f ? 1f : Mathf.Sin(k * Mathf.PI * 0.62f) / Mathf.Sin(Mathf.PI * 0.62f);
                column.transform.localScale = new Vector3(1f, Mathf.Max(0.01f, y), 1f);
            }

            if (loop != null)
            {
                // The plinth's slots follow the bays as they slide; a rising bay's slot opens at once.
                _slotXs.Clear();
                for (int i = 0; i < _columnTo.Length; i++)
                    if (columns[i] != null && columns[i].activeSelf) _slotXs.Add(columns[i].transform.localPosition.x);
                loop.SetLayout(Mathf.Lerp(_loopFrom, _loopTo, e), _slotXs);
            }

            if (k < 1f) return;
            _layoutStart = -1f;
            for (int i = 0; i < _columnRising.Length; i++) _columnRising[i] = false;
            if (!instant && Application.isPlaying && board != null && board.checkIn == this) board.RefitFrame(immediate: false);
        }

        // ---- set up per level -----------------------------------------------------------

        /// <summary>
        /// A new round. Colours are the carts in order; rules may be null, in which case every
        /// case simply goes in.
        /// </summary>
        public void Begin(ColourMatchRules rules, IList<Color> carts, int cartCapacity)
        {
            Unsubscribe();
            _rules = rules;
            _carts.Clear();
            if (carts != null) _carts.AddRange(carts);
            _capacity = Mathf.Max(1, cartCapacity);
            _visualCart = 0;
            _entered = 0;
            _pendingSwitch = false;
            _readyAt = 0f;
            ResetPlaces();
            ShowPlaces(_rules != null ? rules.QueueCapacity : PlaceCount, instant: true);

            if (_rules != null)
            {
                _rules.BoardedCart += OnBoarded;
                _rules.Queued += OnQueued;
                _rules.LeftQueueForCart += OnLeftQueue;
                _rules.Overflowed += OnOverflow;
            }

            if (terminal != null)
            {
                if (_carts.Count > 0) terminal.Show(_carts[0], instant: true);
                else terminal.ShowIdle(instant: true);
                terminal.SetSeats(0);
                terminal.SetUpcoming(_carts, 1);
            }

            RefreshMarks();
        }

        /// <summary>Takes every case off the belt. Called as the board clears.</summary>
        public void Clear()
        {
            foreach (var c in _cases)
                if (c.Piece != null) Destroy(c.Piece.gameObject);
            _cases.Clear();
            _byId.Clear();
            _pendingSwitch = false;
            ResetPlaces();
        }

        void ResetPlaces()
        {
            int n = places != null ? places.Length : 0;
            if (_placeOwner.Length != n) _placeOwner = new int[n];
            for (int i = 0; i < n; i++) _placeOwner[i] = -1;
            _markState = -1;
        }

        void OnDestroy() => Unsubscribe();

        void Unsubscribe()
        {
            if (_rules == null) return;
            _rules.BoardedCart -= OnBoarded;
            _rules.Queued -= OnQueued;
            _rules.LeftQueueForCart -= OnLeftQueue;
            _rules.Overflowed -= OnOverflow;
            _rules = null;
        }

        // ---- the rules' decisions -------------------------------------------------------

        void OnBoarded(ColourMatchRules.Bag bag, int cart, int seat)
        {
            if (!_byId.TryGetValue(bag.Id, out var c)) return;
            c.Assigned = true;
            c.ToTerminalWanted = true;
            c.Cart = cart;
            c.Overflow = false;
        }

        void OnQueued(ColourMatchRules.Bag bag, int slot)
        {
            if (!_byId.TryGetValue(bag.Id, out var c)) return;
            c.Assigned = true;
            c.ToTerminalWanted = false;
            c.Overflow = false;
            if (c.Place < 0) c.Place = ClaimPlace(c.Id);

            // A case that overflowed was held at the entry; a continue has just made room. With no
            // bay free yet it stays in Hold, which asks again every frame.
            if (c.Stage == Stage.Hold && c.Place >= 0) StartToPlace(c);
        }

        void OnLeftQueue(ColourMatchRules.Bag bag, int cart, int seat)
        {
            if (!_byId.TryGetValue(bag.Id, out var c)) return;
            c.ToTerminalWanted = true;
            c.Cart = cart;
        }

        void OnOverflow(ColourMatchRules.Bag bag)
        {
            if (!_byId.TryGetValue(bag.Id, out var c)) return;
            c.Assigned = true;
            c.Overflow = true;
        }

        // The free place nearest the middle of the row, among the places that are shown.
        int ClaimPlace(int id)
        {
            int shown = _shownPlaces >= 0 ? Mathf.Min(_shownPlaces, _placeOwner.Length) : _placeOwner.Length;
            float centre = loop != null ? loop.centreX : 0f;
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < shown; i++)
            {
                if (_placeOwner[i] >= 0) continue;
                float x = i < _columnTo.Length ? _columnTo[i] : centre;
                // Ties broken towards the right, so the row fills evenly rather than one side first.
                float distance = Mathf.Abs(x - centre) - (x > centre ? 0.01f : 0f);
                if (distance < bestDistance) { bestDistance = distance; best = i; }
            }
            if (best >= 0) _placeOwner[best] = id;
            return best;
        }

        void ReleasePlace(Case c)
        {
            if (c.Place >= 0 && c.Place < _placeOwner.Length && _placeOwner[c.Place] == c.Id) _placeOwner[c.Place] = -1;
            c.Place = -1;
        }

        // ---- what Board asks ------------------------------------------------------------

        // Which edge of the board the junction is on. The platform lies beyond it.
        bool JunctionAtBack => entry != null && board.transform.InverseTransformPoint(entry.position).z > 0f;

        /// <summary>
        /// The way from a point on the ring (board-local, at belt height) to the platform entry,
        /// in world space. <paramref name="side"/> is the edge of the board the case left by.
        /// </summary>
        public List<Vector3> Route(Direction side, Vector3 landingLocal)
        {
            var b = board;
            float h = b.beltHeight;
            bool back = JunctionAtBack;
            float edge = back ? b.RingBack : b.RingFront;
            float far = back ? b.RingFront : b.RingBack;
            var toward = back ? Direction.Up : Direction.Down;
            float mergeX = entry != null ? b.transform.InverseTransformPoint(entry.position).x : 0f;

            var local = new List<Vector3> { new Vector3(landingLocal.x, h, landingLocal.z) };
            bool left;

            if (side == Direction.Left)
            {
                left = true;
                local.Add(new Vector3(b.RingLeft, h, edge));
            }
            else if (side == Direction.Right)
            {
                left = false;
                local.Add(new Vector3(b.RingRight, h, edge));
            }
            else if (side == toward)
            {
                left = landingLocal.x < mergeX;
            }
            else
            {
                // Left the board on the far edge: round whichever side is nearer.
                left = landingLocal.x < mergeX;
                float sideX = left ? b.RingLeft : b.RingRight;
                local.Add(new Vector3(sideX, h, far));
                local.Add(new Vector3(sideX, h, edge));
            }

            // The junction's own points, skipping any the case has already passed: a case that
            // leaves by the junction's edge right over the junction lands part of the way round it.
            var feed = left ? leftFeed : rightFeed;
            float fromX = local[local.Count - 1].x;
            if (feed != null)
            {
                foreach (var t in feed)
                {
                    if (t == null) continue;
                    var p = b.transform.InverseTransformPoint(t.position);
                    p.y = h;
                    // Each arm's points only ever move towards the middle, so "passed" is a
                    // question about x alone.
                    bool passed = side == toward && (left ? p.x < fromX - 0.1f : p.x > fromX + 0.1f);
                    if (passed) continue;
                    local.Add(p);
                }
            }

            if (entry != null)
            {
                var e = b.transform.InverseTransformPoint(entry.position);
                e.y = h;
                if ((local[local.Count - 1] - e).sqrMagnitude > 0.01f) local.Add(e);
            }

            var rounded = Round(local, cornerRadius);
            for (int i = 0; i < rounded.Count; i++) rounded[i] = b.transform.TransformPoint(rounded[i]);
            return rounded;
        }

        /// <summary>Whether a case may land on the belt here without landing on another.</summary>
        public bool LandingClear(Vector3 world, PuzzlePiece piece)
        {
            float half = MeasureHalf(piece, longest: true);
            foreach (var c in _cases)
            {
                if (c.Stage == Stage.Entering) continue;
                float need = half + c.Half + gap;
                if (Flat(Position(c) - world).sqrMagnitude < need * need) return false;
            }
            return true;
        }

        /// <summary>
        /// Holds a place on the belt for a case about to hop onto it, so that nothing behind
        /// drives into the spot while it is in the air.
        /// </summary>
        public void Reserve(PuzzlePiece piece, List<Vector3> path)
        {
            if (piece == null || path == null || path.Count < 2) return;
            var c = new Case
            {
                Piece = piece,
                Id = piece.Id,
                Stage = Stage.Boarding,
                Path = path,
                Cum = Cumulative(path),
                // Still standing up; lying down, its height becomes its length.
                HalfRaw = MeasureHalf(piece, longest: true),
                BaseScale = piece.transform.localScale,
            };
            _cases.Add(c);
            _byId[c.Id] = c;
        }

        /// <summary>The case has landed. It rides from here; <paramref name="arrived"/> fires at the platform.</summary>
        public void Ride(PuzzlePiece piece, Action<PuzzlePiece> arrived)
        {
            if (piece == null || !_byId.TryGetValue(piece.Id, out var c)) { arrived?.Invoke(piece); return; }
            c.Arrived = arrived;
            c.Stage = Stage.Riding;
            // Measured again now it is lying down: its length along the belt is what counts.
            c.HalfRaw = MeasureHalf(piece);
            c.BaseScale = piece.transform.localScale;
            c.Fit = Mathf.Min(1f, placeLength / Mathf.Max(0.01f, c.HalfRaw * 2f));
            c.Speed = rideSpeed * 0.35f;
        }

        /// <summary>
        /// Ground corners of everything past the junction as it stands now -- the loop only as long
        /// as the places shown, the terminal and the carts beside it -- for the camera to keep in shot.
        /// </summary>
        // Measured off what is showing rather than from fixed markers: framed for the longest loop,
        // a level with five places was shot from far enough back to leave a band of bare floor
        // under the board.
        public IEnumerable<Vector3> FramePoints()
        {
            Bounds bounds = default;
            bool any = false;
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }

            if (!any)
            {
                if (frameInclude == null) yield break;
                foreach (var t in frameInclude)
                    if (t != null) yield return t.position;
                yield break;
            }

            float y = entry != null ? entry.position.y : bounds.center.y;
            yield return new Vector3(bounds.min.x, y, bounds.min.z);
            yield return new Vector3(bounds.min.x, y, bounds.max.z);
            yield return new Vector3(bounds.max.x, y, bounds.min.z);
            yield return new Vector3(bounds.max.x, y, bounds.max.z);
        }

        // ---- every frame ----------------------------------------------------------------

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            _cases.RemoveAll(c => c.Piece == null && Forget(c));
            _cases.Sort((a, b) =>
            {
                int byRank = Rank(a).CompareTo(Rank(b));
                return byRank != 0 ? byRank : a.Id.CompareTo(b.Id);
            });

            TickTerminal();

            // A continue buys a place: the loop grows a column for it.
            if (_rules != null) ShowPlaces(_rules.QueueCapacity, instant: false);
            TickLayout();

            // A pass over a copy: arriving hands a case to the rules, and the rules may end
            // the round, which can clear the belt under the loop.
            var snapshot = _cases.ToArray();
            foreach (var c in snapshot)
            {
                if (c.Piece == null) continue;
                switch (c.Stage)
                {
                    case Stage.Riding: TickRide(c, dt); break;
                    case Stage.Hold: TickHold(c, dt); break;
                    case Stage.ToPlace: TickToPlace(c, dt); break;
                    case Stage.Parked: TickParked(c, dt); break;
                    case Stage.ToTerminal: TickToTerminal(c, dt); break;
                    case Stage.Entering: TickEnter(c, dt); break;
                }
            }

            RefreshMarks();
        }

        bool Forget(Case c)
        {
            _byId.Remove(c.Id);
            ReleasePlace(c);
            return true;
        }

        // Distance still to go before the platform entry; past it, how far a case has come from
        // the entry, negated. Cases in a place or on their way from one are far from the entry
        // and out of the way of anything arriving.
        float Rank(Case c)
        {
            switch (c.Stage)
            {
                case Stage.Boarding:
                case Stage.Riding: return c.Length - c.S;
                case Stage.Hold: return 0f;
                case Stage.ToPlace:
                case Stage.ToTerminal: return c.FromEntry ? -c.S : -1000f;
                default: return -1000f;
            }
        }

        // The spacing rule shared by everything that follows a path: give way to any case
        // nearer the end of the shared stretch, by the belt between them.
        // Only a case somewhere in front counts: one the path has turned away from -- a case waiting
        // on the spine while this one goes sideways to a bay -- is near, and nearer the end, but
        // not in the way, and counting it held the sideways case still for good.
        float Room(Case c, Vector3 at, Vector3 forward, float advance)
        {
            float myRank = Rank(c);
            foreach (var o in _cases)
            {
                if (o == c || o.Piece == null || o.Stage == Stage.Entering || o.Stage == Stage.Parked) continue;
                float rank = Rank(o);
                if (rank > myRank || (rank == myRank && o.Id > c.Id)) continue;

                float need = c.Half + o.Half + gap;
                float between = myRank - rank;
                var offset = Flat(Position(o) - at);
                if (offset.magnitude > between + need + 1f) continue;
                if (offset.sqrMagnitude > 0.01f && Vector3.Dot(offset.normalized, forward) < 0.25f) continue;

                float room = between - need;
                if (room < advance) advance = Mathf.Max(0f, room);
            }
            return advance;
        }

        void TickRide(Case c, float dt)
        {
            c.Speed = Mathf.MoveTowards(c.Speed, rideSpeed, rideSpeed * 3f * dt);
            float advance = Room(c, PointAt(c, c.S, out Vector3 heading), heading, c.Speed * dt);
            if (advance < c.Speed * dt) c.Speed = advance / dt;
            c.S += advance;

            if (c.S < c.Length)
            {
                Vector3 p = PointAt(c, c.S, out Vector3 f);
                Place(c, p, f, dt);
                return;
            }

            c.S = c.Length;

            // The rules decide here, synchronously, through Board.PieceArrived.
            var arrived = c.Arrived;
            c.Arrived = null;
            arrived?.Invoke(c.Piece);

            // Nobody claimed it -- no rules this round, or the level is being skipped -- so it
            // simply goes in.
            if (!c.Assigned)
            {
                c.Assigned = true;
                c.ToTerminalWanted = true;
                c.Cart = -1;
            }

            if (c.Overflow) { c.Stage = Stage.Hold; return; }
            if (c.ToTerminalWanted) { StartToTerminal(c, fromPlace: false); return; }
            if (c.Place >= 0) { StartToPlace(c); return; }
            c.Stage = Stage.Hold;
        }

        // Waiting at the entry with nowhere to go: a case that overflowed -- or one queued while
        // every place was still held by a case on its way out, which asks again every frame.
        void TickHold(Case c, float dt)
        {
            if (!c.Overflow && c.Assigned)
            {
                if (c.ToTerminalWanted) { StartToTerminal(c, fromPlace: false); return; }
                if (c.Place < 0) c.Place = ClaimPlace(c.Id);
                if (c.Place >= 0) { StartToPlace(c); return; }
            }

            Vector3 p = entry.position;
            if (c.Overflow)
                p += Vector3.Cross(Vector3.up, Flat(road.position - entry.position).normalized) * Mathf.Sin(Time.time * 40f) * 0.18f;
            c.Piece.transform.position = p;
        }

        void StartToPlace(Case c)
        {
            // The place keeps its own height: a stand is a little above the platform.
            var place = places[c.Place].position;
            var start = entry.position;
            // Out along the near edge of the platform, then up into the place: straight across
            // would clip the places nearer the middle.
            var path = new List<Vector3> { start, start + Across(place - start), place };
            SetPath(c, Round(path, cornerRadius), Stage.ToPlace, fromEntry: true);
        }

        void StartToTerminal(Case c, bool fromPlace)
        {
            var start = c.Piece.transform.position;
            if (!fromPlace) start.y = entry.position.y;
            var roadPoint = road.position;
            roadPoint.y = start.y;
            var mouthPoint = mouth.position;
            mouthPoint.y = start.y;

            if (!fromPlace && loop != null)
            {
                // Round the loop: out along the sorter, through the end, back along the collector
                // and in. The sides take turns, so two cases in a row do not queue behind each other.
                float side = _nextSide;
                _nextSide = -_nextSide;
                float y = loop.transform.InverseTransformPoint(entry.position).y;
                var around = loop.CentreLine(side, y);
                for (int i = 0; i < around.Count; i++) around[i] = loop.transform.TransformPoint(around[i]);
                around.Add(mouthPoint);
                SetPath(c, Round(around, cornerRadius), Stage.ToTerminal, fromEntry: true);
                // Not wanted yet, it waits at the far end of the loop, clear of every bay.
                c.WaitAt = loop.HalfLength + Mathf.PI * loop.Radius * 0.5f;
                return;
            }

            var path = new List<Vector3> { start };
            if (fromPlace)
            {
                // Up out of the bay onto the collector, along it to the middle, and in.
                path.Add(start + Along(roadPoint - start));
            }
            path.Add(roadPoint);
            path.Add(mouthPoint);
            SetPath(c, Round(path, cornerRadius), Stage.ToTerminal, fromEntry: !fromPlace);
            c.WaitAt = fromPlace ? c.Length : Mathf.Max(0f, Flat(roadPoint - start).magnitude - collectorClearance - c.Half);
        }

        // The part of a platform offset that runs across the board, and the part that runs along it.
        Vector3 Across(Vector3 v)
        {
            var right = Flat(board.transform.right).normalized;
            return right * Vector3.Dot(v, right);
        }

        Vector3 Along(Vector3 v)
        {
            var forward = Flat(board.transform.forward).normalized;
            return forward * Vector3.Dot(v, forward);
        }

        void SetPath(Case c, List<Vector3> path, Stage stage, bool fromEntry)
        {
            c.Path = path;
            c.Cum = Cumulative(path);
            c.S = 0f;
            c.Stage = stage;
            c.FromEntry = fromEntry;
        }

        void TickToPlace(Case c, float dt)
        {
            ShrinkToFit(c, dt);
            float advance = Room(c, PointAt(c, c.S, out Vector3 heading), heading, laneSpeed * dt);
            c.S = Mathf.Min(c.Length, c.S + advance);
            Vector3 p = PointAt(c, c.S, out Vector3 f);
            Place(c, p, f, dt);
            if (c.S >= c.Length - 0.01f) c.Stage = Stage.Parked;
        }

        void TickParked(Case c, float dt)
        {
            ShrinkToFit(c, dt);
            // The row slides when a bay is added; a waiting case moves with its bay.
            if (c.Place >= 0 && c.Place < places.Length && places[c.Place] != null)
                c.Piece.transform.position = Vector3.MoveTowards(c.Piece.transform.position, places[c.Place].position, laneSpeed * dt);
            if (!c.ToTerminalWanted || !TerminalReady(c.Cart)) return;
            ReleasePlace(c);
            StartToTerminal(c, fromPlace: true);
        }

        // The last stretch into the terminal is shared by everything bound for it, from the entry
        // and from every place, so here the order is distance still to go.
        void TickToTerminal(Case c, float dt)
        {
            ShrinkToFit(c, dt);
            bool ready = TerminalReady(c.Cart);
            float remaining = c.Length - c.S;
            float advance = laneSpeed * dt;

            if (c.FromEntry) advance = Room(c, PointAt(c, c.S, out Vector3 heading), heading, advance);

            Vector3 at = PointAt(c, c.S, out Vector3 along);
            foreach (var o in _cases)
            {
                if (o == c || o.Stage != Stage.ToTerminal || o.Piece == null) continue;
                float theirs = o.Length - o.S;
                if (theirs > remaining || (theirs == remaining && o.Id > c.Id)) continue;
                float need = c.Half + o.Half + gap;
                float between = remaining - theirs;
                var offset = Flat(Position(o) - at);
                if (offset.magnitude > between + need + 1f) continue;
                // In front only: a case waiting on the spine is beside a case coming up out of a
                // bay, not ahead of it, and counting it held both still.
                if (offset.sqrMagnitude > 0.01f && Vector3.Dot(offset.normalized, along) < 0.25f) continue;
                advance = Mathf.Min(advance, Mathf.Max(0f, between - need));
            }

            // Not wanted yet, a case from the entry waits on the spine short of the collector, not at
            // the scanner: waiting in the last stretch it blocked the cases of the cart before, which
            // were coming off their bays along the collector, and the terminal waited for them.
            float stop = ready ? c.Length
                       : c.FromEntry ? Mathf.Min(c.Length - 0.8f, c.WaitAt)
                                     : c.Length - 0.8f;
            if (c.S + advance > stop) advance = Mathf.Max(0f, stop - c.S);
            c.S += advance;

            if (ready && c.S >= c.Length - 0.05f && Time.time >= _nextEnterAt)
            {
                BeginEnter(c);
                return;
            }

            Vector3 p = PointAt(c, Mathf.Max(0f, c.S), out Vector3 f);
            Place(c, p, f, dt);
        }

        void ShrinkToFit(Case c, float dt)
        {
            c.ScaleNow = Mathf.MoveTowards(c.ScaleNow, c.Fit, dt * 2.5f);
            c.Piece.transform.localScale = c.BaseScale * c.ScaleNow;
        }

        void BeginEnter(Case c)
        {
            c.Stage = Stage.Entering;
            PointAt(c, c.Length, out Vector3 forward);
            c.From = c.Path[c.Path.Count - 1];
            c.To = c.From + forward * enterDepth;
            c.T = 0f;
            c.Duration = enterTime;
            _nextEnterAt = Time.time + 0.06f;
        }

        void TickEnter(Case c, float dt)
        {
            c.T += dt / Mathf.Max(0.05f, c.Duration);
            float k = Mathf.Clamp01(c.T);
            c.Piece.transform.position = Vector3.Lerp(c.From, c.To, k);
            c.Piece.transform.localScale = c.BaseScale * c.ScaleNow * Mathf.Lerp(1f, 0.15f, k * k);

            if (k < 1f) return;

            SheepGame.Fx.PieceEffects.Sparkle(c.From + Vector3.up * 3f, c.Piece.ThemeColour);
            SheepGame.Audio.AudioManager.PieceLand();
            OnEntered(c.Cart);

            _cases.Remove(c);
            _byId.Remove(c.Id);
            Destroy(c.Piece.gameObject);
        }

        // ---- the terminal ---------------------------------------------------------------

        bool TerminalReady(int cart) =>
            cart < 0 || (cart == _visualCart && !_pendingSwitch && Time.time >= _readyAt);

        void OnEntered(int cart)
        {
            if (terminal != null) terminal.Punch();
            if (cart < 0 || cart != _visualCart) return;

            _entered++;
            if (terminal != null) terminal.SetSeats(_entered);

            if (_entered >= _capacity)
            {
                _pendingSwitch = true;
                _switchAt = Time.time + fullHold;
            }
        }

        void TickTerminal()
        {
            if (!_pendingSwitch || Time.time < _switchAt) return;

            _pendingSwitch = false;
            _visualCart++;
            _entered = 0;

            if (terminal != null)
            {
                if (_visualCart < _carts.Count) terminal.Show(_carts[_visualCart], instant: false);
                else terminal.ShowIdle(instant: false);
                terminal.SetSeats(0);
                terminal.SetUpcoming(_carts, _visualCart + 1);
                terminal.Punch(1f);
                // Cases may set off for the new colour halfway through the change: by the time
                // they arrive the terminal is wearing it.
                _readyAt = Time.time + terminal.changeTime * 0.5f;
            }
        }

        void RefreshMarks()
        {
            if (placeMarks == null || placeMarks.Length == 0) return;

            int capacity = _rules != null ? _rules.QueueCapacity : placeMarks.Length;
            int taken = 0;
            foreach (int owner in _placeOwner) if (owner >= 0) taken++;
            bool full = _rules != null && _rules.QueueFull;

            int state = capacity * 10000 + (full ? 5000 : 0);
            for (int i = 0; i < _placeOwner.Length; i++) if (_placeOwner[i] >= 0) state += 1 << i;
            if (state == _markState) return;
            _markState = state;

            var block = new MaterialPropertyBlock();
            // A place may have several marks -- a bay lights both of its rails.
            int perPlace = Mathf.Max(1, placeMarks.Length / Mathf.Max(1, _placeOwner.Length));
            for (int m = 0; m < placeMarks.Length; m++)
            {
                var mark = placeMarks[m];
                if (mark == null) continue;
                int i = m / perPlace;
                bool owned = i < _placeOwner.Length && _placeOwner[i] >= 0;
                Color colour = i >= capacity ? placeLocked
                             : full ? placeFull
                             : owned ? placeTaken
                                     : placeFree;
                mark.GetPropertyBlock(block);
                block.SetColor("_BaseColor", colour);
                block.SetColor("_EmissionColor", i >= capacity ? Color.black : colour * (full ? 0.6f : 0.25f));
                mark.SetPropertyBlock(block);
            }
        }

        // ---- geometry -------------------------------------------------------------------

        Vector3 Position(Case c)
        {
            switch (c.Stage)
            {
                case Stage.Boarding: return c.Path[0];
                case Stage.Hold: return entry.position;
                case Stage.Riding:
                case Stage.ToPlace:
                case Stage.ToTerminal: return PointAt(c, c.S, out _);
                default: return c.Piece != null ? c.Piece.transform.position : Vector3.zero;
            }
        }

        static Vector3 PointAt(Case c, float s, out Vector3 forward)
        {
            var path = c.Path;
            var cum = c.Cum;
            int n = path.Count;
            if (n < 2) { forward = Vector3.forward; return path[0]; }
            s = Mathf.Clamp(s, 0f, cum[n - 1]);

            int i = 1;
            while (i < n - 1 && cum[i] < s) i++;
            float segment = cum[i] - cum[i - 1];
            float t = segment > 0.0001f ? (s - cum[i - 1]) / segment : 1f;

            forward = Flat(path[i] - path[i - 1]);
            forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
            return Vector3.Lerp(path[i - 1], path[i], t);
        }

        static float[] Cumulative(List<Vector3> path)
        {
            var cum = new float[path.Count];
            for (int i = 1; i < path.Count; i++) cum[i] = cum[i - 1] + Vector3.Distance(path[i - 1], path[i]);
            return cum;
        }

        static void Place(Case c, Vector3 position, Vector3 forward, float dt)
        {
            var t = c.Piece.transform;
            t.position = position;
            if (forward.sqrMagnitude < 0.0001f) return;
            t.rotation = Quaternion.RotateTowards(t.rotation, Quaternion.LookRotation(forward, Vector3.up), 540f * dt);
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        // Half the case's length along the way it faces, measured off its meshes in its own
        // frame -- a world bounding box grows as the case turns and would over-space a case
        // on every corner.
        static float MeasureHalf(PuzzlePiece piece, bool longest = false)
        {
            if (piece == null) return 1f;
            var root = piece.transform;
            if (longest)
            {
                // Before it lies down: the largest of its three sizes is what it will be along the belt.
                var b = default(Bounds);
                bool any = false;
                foreach (var r in piece.GetComponentsInChildren<Renderer>())
                {
                    if (!any) { b = r.bounds; any = true; }
                    else b.Encapsulate(r.bounds);
                }
                return any ? Mathf.Max(b.extents.x, Mathf.Max(b.extents.y, b.extents.z)) : 1.7f;
            }

            float min = float.MaxValue, max = float.MinValue;
            foreach (var r in piece.GetComponentsInChildren<MeshRenderer>())
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var bounds = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3((i & 1) == 0 ? bounds.min.x : bounds.max.x,
                                             (i & 2) == 0 ? bounds.min.y : bounds.max.y,
                                             (i & 4) == 0 ? bounds.min.z : bounds.max.z);
                    float z = root.InverseTransformPoint(r.transform.TransformPoint(corner)).z;
                    min = Mathf.Min(min, z);
                    max = Mathf.Max(max, z);
                }
            }

            if (min > max) return 1.7f;
            return (max - min) * 0.5f * Mathf.Abs(root.lossyScale.z);
        }

        // Fillets every corner of a polyline, so a case turns through a curve instead of
        // cutting to a point and pivoting on it.
        static List<Vector3> Round(List<Vector3> points, float radius)
        {
            if (points.Count < 3 || radius <= 0f) return new List<Vector3>(points);

            var result = new List<Vector3> { points[0] };
            for (int i = 1; i < points.Count - 1; i++)
            {
                Vector3 a = points[i - 1], p = points[i], b = points[i + 1];
                Vector3 inDir = p - a, outDir = b - p;
                float inLen = inDir.magnitude, outLen = outDir.magnitude;
                if (inLen < 0.001f || outLen < 0.001f) continue;
                inDir /= inLen;
                outDir /= outLen;

                if (Vector3.Dot(inDir, outDir) > 0.985f) { result.Add(p); continue; }

                float r = Mathf.Min(radius, inLen * 0.45f, outLen * 0.45f);
                Vector3 start = p - inDir * r;
                Vector3 end = p + outDir * r;
                const int steps = 6;
                for (int s = 0; s <= steps; s++)
                {
                    float t = s / (float)steps;
                    // Quadratic Bezier through the corner: close enough to an arc at this size.
                    result.Add((1 - t) * (1 - t) * start + 2 * (1 - t) * t * p + t * t * end);
                }
            }
            result.Add(points[points.Count - 1]);
            return result;
        }
    }
}
