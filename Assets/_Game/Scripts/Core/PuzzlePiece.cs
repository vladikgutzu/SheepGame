using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SheepGame.Core
{
    // Values match the "animation" int the Suriyun rigs read (TypeA/B/C all share the
    // numbering). Packs without that parameter — the Toon Planes controllers — simply
    // ignore every one of these.
    public enum PieceMotion
    {
        Idle = 0,
        Walk = 1,
        Run = 2,
        Jump = 3,
        Eat = 4,
        Rest = 5,
        Flinch = 7,
    }

    public class PuzzlePiece : MonoBehaviour
    {
        public int VariantIndex { get; private set; }

        /// <summary>Unique on its board, so the cart and the queue can tell cases of one colour apart.</summary>
        public int Id { get; set; }

        /// <summary>Index into the palette. The colour rules match on this, not on the tint.</summary>
        public int ColourIndex { get; set; } = -1;

        /// <summary>Cells covered, laid along the way the piece faces. Usually one.</summary>
        public int Cells { get; private set; } = 1;

        /// <summary>The cell the piece leaves from. A longer piece trails back from here.</summary>
        // The front rather than a corner, so the question "is the way out clear" starts from
        // the same place whatever the size: one step along the facing, exactly as it always did.
        public Vector2Int GridPos { get; private set; }
        public Direction Direction { get; private set; }
        public bool CanEscape { get; private set; }

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        const string MotionParam = "animation";

        Animator _animator;
        bool _hasMotionParam;
        bool _travelling;
        Coroutine _blockedRoutine;
        Renderer[] _renderers;
        Color[] _baseColors;
        int[] _colorIds;
        MaterialPropertyBlock _block;

        [Header("Animation")]
        [Tooltip("Gait used while a piece is travelling off the board.")]
        public PieceMotion travelMotion = PieceMotion.Run;
        [Tooltip("Played when a piece is tapped but has no clear path out. Only reaches rigs " +
                 "that expose the motion parameter; everything else hops instead.")]
        public PieceMotion blockedMotion = PieceMotion.Jump;
        [Tooltip("How high a piece with no animator hops when it is tapped but walled in, " +
                 "as a share of its own standing height. A share and not a distance because " +
                 "a pack's models are not drawn to one scale: the board fits each one to " +
                 "the cell, so a mesh built small is scaled up ten times more than its " +
                 "neighbour, and a hop given in the prefab's units came out ten times " +
                 "higher on it. How far a bag jumps is a fact about the bag the player " +
                 "sees, so it is measured off that.")]
        [Range(0f, 1f)] public float blockedHopShare = 0.24f;
        [Tooltip("Seconds that hop takes.")]
        public float blockedHopTime = 0.36f;
        [Tooltip("Let waiting pieces occasionally break idle to eat, rest or hop.")]
        public bool idleVariety = true;
        [Tooltip("Seconds between one piece's idle breaks, picked per break.")]
        public Vector2 idleBreakInterval = new Vector2(5f, 20f);

        [Header("Body")]
        [Tooltip("Name of the child renderer that is the piece's shell — the part that " +
                 "takes the level's colour and that the direction badge sits on. Leave " +
                 "empty to treat the whole model as the body, which is right for a single " +
                 "mesh but not for a model with a raised handle: measuring that in would " +
                 "float the badge above the handle instead of resting it on the lid.")]
        public string bodyRenderer = "";
        [Tooltip("Name of the material on that renderer to colour. A shell usually shares " +
                 "its renderer with trim and fittings that must keep their own colours; " +
                 "empty tints every slot.")]
        public string bodyMaterial = "";

        [Header("Boarding the belt")]
        [Tooltip("Flop onto the conveyor when the piece climbs on, instead of riding it " +
                 "standing up. Suits luggage; leave off for anything that walks or flies.")]
        public bool lieOnBelt = true;
        [Tooltip("Rotation, in the piece's own axes, that lays the model down. The default " +
                 "tips it forward onto its face, which puts its thinnest side up and its " +
                 "length along the direction of travel.")]
        public Vector3 beltLieRotation = new Vector3(90f, 0f, 0f);

        [Header("Model")]
        [Tooltip("Share of its built size the model keeps, in the piece's own axes: X " +
                 "across, Y up, Z along the way it faces. The board's fit matches a model's " +
                 "footprint to its cells and leaves the rest alone, so a model drawn taller " +
                 "or fatter than the others in its pack is out of scale on the same grid. " +
                 "Set from PieceModelLibrary.variantModelScale.")]
        public Vector3 modelScale = Vector3.one;

        [Header("Hint")]
        [Tooltip("How far the piece swells while the hint is pointing at it. It has to read " +
                 "at phone size across a board of seventy cells, and nothing smaller than " +
                 "about a tenth does.")]
        [Range(1f, 1.4f)] public float hintScale = 1.14f;
        [Tooltip("Seconds of one swell-and-settle cycle.")]
        public float hintCycle = 0.8f;

        [Header("Direction Indicator")]
        [Tooltip("Arrow badge on the piece's top face showing which way it leaves. Most " +
                 "themes' models already read a facing (a head, a nose, a bow); turn this " +
                 "off for those and leave it on for shapes — like a suitcase — that look " +
                 "the same from every side.")]
        public bool showDirectionArrow = true;
        [Tooltip("How much of the top face the badge covers. Much below 0.45 it stops " +
                 "reading at phone size; much above 0.7 it hides the luggage it is sitting " +
                 "on and starts overlapping the piece behind.")]
        // Cut from 0.72 once the cases were pushed to full saturation: at that size the
        // badge covered most of the colour the piece is identified by, and a board of them
        // read as a field of arrows with luggage somewhere underneath.
        [Range(0.3f, 1.1f)] public float arrowFit = 0.55f;
        [Tooltip("Gap above the piece's highest point, as a fraction of its height. Just " +
                 "enough to clear a raised handle: the badge has to look stuck to this lid, " +
                 "and anything floating is read as hovering over the piece behind instead.")]
        [Range(0f, 0.3f)] public float arrowHover = 0.02f;
        [Tooltip("Dark border around the badge, as a fraction of its width. Without one the " +
                 "arrow disappears against whichever luggage colour it lands on.")]
        [Range(0f, 0.3f)] public float arrowOutline = 0.12f;
        [Tooltip("Leave empty to use a signage-yellow unlit material shared by every piece.")]
        public Material directionArrowMaterial;
        [Tooltip("Leave empty for the shared near-black border.")]
        public Material directionArrowOutlineMaterial;

        DirectionArrow _arrow;
        Color _tint;
        bool _tinted;
        Coroutine _hintRoutine;
        Vector3 _hintBaseScale;

        Transform _model;
        Quaternion _modelRest;
        Vector3 _modelRestScale;
        Vector3 _modelRestPos;
        int _upAxis = 1;
        Bounds _modelRestBounds;
        bool _hasRestBounds;
        Coroutine _boarding;
        Vector3 _hopRest;
        bool _hopping;
        // Worked out once the model has been fitted and scaled, which is the only moment it
        // is the size it will be played at.
        float _hopHeight;

        public void Init(int variantIndex, Vector2Int gridPos, Direction direction, int cells = 1)
        {
            VariantIndex = variantIndex;
            Cells = Mathf.Max(1, cells);
            _animator = GetComponentInChildren<Animator>();
            _hasMotionParam = HasParameter(MotionParam);

            CacheRenderers();
            CacheModel();
            _hopHeight = blockedHopShare * StandingHeight();
            if (showDirectionArrow) BuildDirectionArrow();

            ReassignPlacement(gridPos, direction);
            SetEscapable(true);
            SetMoving(false);

            DesyncIdle();
            if (idleVariety && _hasMotionParam) StartCoroutine(IdleLife());
        }

        // Measured before ReassignPlacement rotates the piece, while it still sits at the
        // identity rotation Instantiate gave it — Direction.Up, in DirectionUtil's terms —
        // so the renderer bounds' world X/Z extents are exactly the footprint the arrow
        // should span, with no rotation math to get backwards.
        void BuildDirectionArrow()
        {
            if (!TryBodyBounds(out Bounds bounds)) return;

            // The badge goes on the top face, not on the floor of the cell. A steeply
            // tilted camera hides the ground behind a piece for roughly height/tan(pitch)
            // — for the luggage that is about 5 units against a 3-unit gap between bags —
            // so a floor arrow is only ever seen on pieces facing the camera. A lid is
            // visible whichever way the piece points.
            //
            // (A floor decal is also unreliable for a second reason: the suitcase model's
            // own base sits *below* the apron's visible surface, so anything placed at
            // "the model's bottom" renders underneath the opaque floor and is never seen.)
            float scaleY = Safe(transform.lossyScale.y);
            Vector3 localCentre = transform.InverseTransformPoint(bounds.center);

            float hoverWorldY = bounds.max.y + Mathf.Max(0.02f, bounds.size.y * arrowHover);
            float localHoverY = (hoverWorldY - transform.position.y) / scaleY;

            // Length runs along the piece's own +Z, the axis the arrow points down, and
            // width across it. Two shapes of luggage pull this in opposite directions: a
            // lid deeper than it is wide would take a badge longer than the piece, and a
            // trolley case's lid — barely two units deep — would take a stub too short to
            // read as an arrow. So the length gets a floor set by the lid's width and the
            // width a ceiling set by the length: on a shallow lid the badge overhangs a
            // little, which it can afford, floating above the piece and pointing off the
            // edge it is leaving by.
            float length = Mathf.Max(bounds.size.z * arrowFit, bounds.size.x * arrowFit * 0.75f);
            float width = Mathf.Min(bounds.size.x * arrowFit, length * 0.95f);

            var go = new GameObject("DirectionArrow");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(localCentre.x, localHoverY, localCentre.z);
            go.transform.localRotation = Quaternion.identity;

            _arrow = go.AddComponent<DirectionArrow>();
            _arrow.Build(length, width, width * arrowOutline, transform.lossyScale,
                         directionArrowMaterial, directionArrowOutlineMaterial);
        }

        static float Safe(float s) => Mathf.Abs(s) > 0.0001f ? s : 1f;

        // The shell's own bounds where the theme names one, everything otherwise. Named
        // separately from the piece as a whole because a trolley case's extended handle is
        // half its height and nothing should be measured off it.
        // How tall the piece stands, in world units. The shell rather than the whole model,
        // for the same reason the badge is placed on it: a raised handle is not height the
        // bag would hop by.
        float StandingHeight() => TryBodyBounds(out Bounds b) ? b.size.y : 0f;

        bool TryBodyBounds(out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            bool named = !string.IsNullOrEmpty(bodyRenderer);

            for (int pass = 0; pass < 2; pass++)
            {
                foreach (var r in _renderers)
                {
                    if (r == null) continue;
                    if (pass == 0 && named && !NameMatches(bodyRenderer, r.gameObject.name)) continue;

                    if (!any) { bounds = r.bounds; any = true; }
                    else bounds.Encapsulate(r.bounds);
                }

                // A body that was named but not found falls back to the whole piece rather
                // than to nothing, so a typo costs a badly placed badge and not a missing one.
                if (any || !named) break;
                named = false;
            }

            return any;
        }

        // Imported material and object names pick up suffixes — " (Instance)", ".001" off a
        // Blender duplicate — that a theme's configuration should not have to spell out.
        static bool NameMatches(string wanted, string actual) =>
            actual == wanted || actual.StartsWith(wanted, System.StringComparison.Ordinal);

        // The level's colour for this piece. Decoration, so Board hands it down from the
        // library's palette rather than the piece choosing: two runs of the same level then
        // paint the same bag the same colour, and the player can go on recognising it.
        public void SetTint(Color color)
        {
            _tint = color;
            _tinted = true;
            ApplyTint();
        }

        void ApplyTint()
        {
            if (!_tinted || _renderers == null) return;

            for (int i = 0; i < _renderers.Length; i++)
            {
                var r = _renderers[i];
                if (r == null) continue;
                if (!string.IsNullOrEmpty(bodyRenderer) &&
                    !NameMatches(bodyRenderer, r.gameObject.name)) continue;

                var mats = r.sharedMaterials;
                bool paintedAny = false;

                for (int slot = 0; slot < mats.Length; slot++)
                {
                    if (mats[slot] == null) continue;
                    if (!string.IsNullOrEmpty(bodyMaterial) &&
                        !NameMatches(bodyMaterial, mats[slot].name)) continue;

                    int id = mats[slot].HasProperty(BaseColorId) ? BaseColorId
                           : mats[slot].HasProperty(ColorId) ? ColorId : 0;
                    if (id == 0) continue;

                    // Per material slot, not per renderer: the shell shares its renderer
                    // with the trim and the lock, which keep the colours they were built
                    // with. Still a property block, so the whole board stays one material.
                    //
                    // Handed over exactly as authored. An earlier version converted to
                    // .linear here on the theory that a property block bypasses the gamma
                    // conversion a material's own colour gets; measured against the models'
                    // built-in colours it does not. Setting the palette to the very colours
                    // the meshes already wear should reproduce the untinted board, and with
                    // the conversion in place it did not: the middle of each channel was
                    // crushed -- the green case went #0C9C0C to #0C540C, the blue one
                    // #0C849C to #0C0C6C -- which is sRGB folded in twice.
                    r.GetPropertyBlock(_block, slot);
                    _block.SetColor(id, _tint);
                    r.SetPropertyBlock(_block, slot);
                    paintedAny = true;
                }

                // Keeps the blocked-piece debug shading, and ThemeColour, reading the
                // colour the piece actually wears rather than the one its shell material
                // shipped with -- and reading it from whichever slot took the tint, not
                // from slot zero specifically.
                //
                // That distinction was a real bug: CaseShellSplitter appends the shell
                // material after whatever the model already had, so a split model's shell
                // is never slot zero. It cost nothing on the five cases, each of which
                // carries the shell on a renderer of its own -- slot zero there by
                // circumstance -- and stayed silent because the other slot's stand-in
                // colour happened to lose the saturation contest ThemeColour runs. The
                // two-cell bag has no such renderer: its shell is slot one on its only
                // renderer, the grey slot zero it never touched has more saturation than
                // white does, and ThemeColour returned that grey for every long bag on the
                // board -- an untinted tap burst, on the one piece not still on the belt.
                if (paintedAny) _baseColors[i] = _tint;
            }
        }

        // A board full of one species shares a single clip, so every animal starts on the
        // same frame and breathes in lockstep — a rank of clones rather than a crowd.
        // Entering the loop at a random point, at a slightly different rate, scatters them
        // and keeps them scattered.
        void DesyncIdle()
        {
            if (_animator == null || _animator.runtimeAnimatorController == null) return;

            _animator.speed = Random.Range(0.85f, 1.15f);

            int idle = Animator.StringToHash("Idle");
            if (_animator.HasState(0, idle))
                _animator.Play(idle, 0, Random.value);
        }

        // Without this the pen is a grid of animals doing exactly one thing, which reads
        // as scenery. Breaks are per piece and randomly spaced, so the board always has a
        // few animals doing something and never all at once.
        IEnumerator IdleLife()
        {
            var breaks = new[] { PieceMotion.Eat, PieceMotion.Rest, PieceMotion.Jump };

            while (true)
            {
                yield return new WaitForSeconds(Random.Range(idleBreakInterval.x, idleBreakInterval.y));
                if (_travelling) continue;

                SetMotion(breaks[Random.Range(0, breaks.Length)]);
                yield return new WaitForSeconds(Random.Range(1.5f, 3f));
                if (!_travelling) SetMotion(PieceMotion.Idle);
            }
        }

        // The pose is applied to the model child, never to this object: Board rotates the
        // root to steer along the belt, so a flop baked into the root would be straightened
        // out again by the next corner.
        // The pose and the squash are applied to one transform, so "the model" has to be one
        // transform. A prefab built for this game is: a single Mesh child under the root. A raw
        // .fbx usually is not -- the wide luggage arrives as twenty loose parts, body, corners,
        // handle tubes and all -- and with them loose only the first ever moved. The rest stayed
        // standing on the belt while the body lay down beside them, which is the case coming
        // apart rather than lying down.
        void CacheModel()
        {
            _model = null;

            int found = 0;
            Transform first = null;
            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                if (!HasRenderer(child)) continue;
                if (first == null) first = child;
                found++;
            }

            if (found == 0) return;

            // One part is already one thing to move; several are gathered into one so they move
            // together. Done here rather than by hand on each model, because the models are
            // whatever an art pack ships and this is the only place that cares.
            _model = found == 1 ? first : GatherParts();

            _modelRest = _model.localRotation;
            _upAxis = AxisOf(Vector3.up);

            ApplyModelScale();

            _modelRestScale = _model.localScale;
            _modelRestPos = _model.localPosition;
            _hasRestBounds = TryLocalBounds(out _modelRestBounds);
        }

        // Which of the model's own axes points a given way in the piece's space.
        //
        // An imported mesh nearly always carries its Z-up correction on the very transform the
        // board drives -- this pack's cases are turned (0, 270, 90) and the wide one (270, 0,
        // 0) -- so the model's local Y is not the piece's up, and scaling y squeezes the case
        // front to back instead of flattening it. Every correction here is a right angle, so
        // one of the model's axes always lines up and no shear can come of this.
        int AxisOf(Vector3 pieceAxis)
        {
            Vector3 v = Quaternion.Inverse(_modelRest) * pieceAxis;
            float x = Mathf.Abs(v.x), y = Mathf.Abs(v.y), z = Mathf.Abs(v.z);
            return x > y && x > z ? 0 : y >= z ? 1 : 2;
        }

        // Trimmed about the model's own origin, which sits at the luggage's base, so what
        // comes off comes off the top and the piece is left standing on the floor.
        void ApplyModelScale()
        {
            Vector3 built = _model.localScale;
            built[AxisOf(Vector3.right)] *= Trim(modelScale.x);
            built[_upAxis] *= Trim(modelScale.y);
            built[AxisOf(Vector3.forward)] *= Trim(modelScale.z);
            _model.localScale = built;
        }

        // A field left at zero means "as built", not "flatten it out of existence".
        static float Trim(float share) => share > 0.01f ? share : 1f;

        static bool HasRenderer(Transform t) =>
            t.GetComponent<Renderer>() != null || t.GetComponentInChildren<Renderer>() != null;

        Transform GatherParts()
        {
            var wrapper = new GameObject("~Model").transform;
            wrapper.SetParent(transform, false);

            // Walked backwards: reparenting mutates the child list being indexed.
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child == wrapper || !HasRenderer(child)) continue;

                // World pose kept, so gathering the parts moves nothing.
                child.SetParent(wrapper, true);
            }

            return wrapper;
        }

        // The model's extents in the piece's own space. Renderer.bounds is a world-space box
        // and would have to be re-fitted after every rotation, which is exactly the number
        // being corrected for here, so the mesh's own box is transformed corner by corner.
        bool TryLocalBounds(out Bounds bounds)
        {
            bounds = default;
            bool any = false;

            foreach (var r in _model.GetComponentsInChildren<Renderer>())
            {
                if (r == null) continue;

                Bounds local = r.localBounds;
                Vector3 c = local.center, e = local.extents;

                for (int i = 0; i < 8; i++)
                {
                    var corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x,
                                                 (i & 2) == 0 ? -e.y : e.y,
                                                 (i & 4) == 0 ? -e.z : e.z);
                    Vector3 point = transform.InverseTransformPoint(r.transform.TransformPoint(corner));
                    if (!any) { bounds = new Bounds(point, Vector3.zero); any = true; }
                    else bounds.Encapsulate(point);
                }
            }

            return any;
        }

        // Where the model has to sit for the belt pose to read as the case tipping over.
        //
        // Rotating the model alone swings it around the piece's origin, which sits at the
        // luggage's feet — so a case told to lie down instead pivoted off sideways and came to
        // rest higher than it started, standing on a corner. Two corrections put it right: the
        // turn is taken about the model's own centre, and then the whole thing is dropped until
        // its lowest point is back where its base was, on the belt.
        Vector3 LiePosition(Quaternion turn)
        {
            if (!_hasRestBounds) return _modelRestPos;

            Vector3 centre = _modelRestBounds.center;
            Vector3 pos = centre - turn * (centre - _modelRestPos);

            Vector3 e = _modelRestBounds.extents;
            float lowest = float.MaxValue;

            for (int i = 0; i < 8; i++)
            {
                var corner = centre + new Vector3((i & 1) == 0 ? -e.x : e.x,
                                                  (i & 2) == 0 ? -e.y : e.y,
                                                  (i & 4) == 0 ? -e.z : e.z);
                float y = (centre + turn * (corner - centre)).y;
                if (y < lowest) lowest = y;
            }

            pos.y += _modelRestBounds.min.y - lowest;
            return pos;
        }

        // Volume is held roughly constant — squashing one axis fattens the other two — or
        // the piece reads as shrinking rather than as being compressed.
        void ApplySquash(float squash)
        {
            if (_model == null) return;

            float fatten = 1f / Mathf.Sqrt(Mathf.Max(0.01f, squash));
            Vector3 scale = _modelRestScale * fatten;
            scale[_upAxis] = _modelRestScale[_upAxis] * squash;
            _model.localScale = scale;
        }

        // Squash, leap, flop, squash: the four poses a cartoon hop is made of, run against
        // the same clock Board uses to arc the piece up onto the conveyor. Board owns where
        // the piece is, this owns what it looks like getting there.
        public void PlayBeltBoarding(float duration)
        {
            if (_model == null) return;
            if (_boarding != null) StopCoroutine(_boarding);
            _boarding = StartCoroutine(BeltBoarding(duration));
        }

        // lieOnBelt decides the pose only. The squash and stretch belong to the hop itself,
        // which every theme does — a wheeled case that rides the belt standing up should
        // still crouch and spring rather than glide up rigid.
        IEnumerator BeltBoarding(float duration)
        {
            Quaternion turn = lieOnBelt ? Quaternion.Euler(beltLieRotation) : Quaternion.identity;
            Quaternion flat = turn * _modelRest;
            Vector3 flatPos = LiePosition(turn);
            float t = 0f;

            while (t < 1f)
            {
                t += Time.deltaTime / Mathf.Max(0.05f, duration);
                float k = Mathf.Clamp01(t);

                ApplySquash(k < 0.18f ? Mathf.Lerp(1f, 0.72f, k / 0.18f)
                          : k < 0.34f ? Mathf.Lerp(0.72f, 1.16f, (k - 0.18f) / 0.16f)
                          : k < 0.84f ? Mathf.Lerp(1.16f, 1.04f, (k - 0.34f) / 0.5f)
                          : k < 0.92f ? Mathf.Lerp(1.04f, 0.76f, (k - 0.84f) / 0.08f)
                                      : Mathf.Lerp(0.76f, 1f, (k - 0.92f) / 0.08f));

                // Tips over during the airborne stretch and is already down when it lands,
                // so the landing squash reads as the bag slapping onto the belt.
                float lie = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.22f, 0.84f, k));
                _model.localRotation = Quaternion.Slerp(_modelRest, flat, lie);
                _model.localPosition = Vector3.Lerp(_modelRestPos, flatPos, lie);

                yield return null;
            }

            _model.localScale = _modelRestScale;
            _model.localRotation = flat;
            _model.localPosition = flatPos;
            _boarding = null;
        }

        // Tapping a piece that is walled in has to answer somehow, or the tap reads as the
        // game having missed it. A rig plays its blocked animation; anything else — luggage,
        // which has no animator at all — hops on the spot instead, so every theme answers.
        public void PlayBlockedNudge()
        {
            if (_travelling) return;

            SheepGame.Fx.PieceEffects.Blocked(transform.position, ThemeColour);
            SheepGame.Audio.AudioManager.BlockedTap();

            if (_blockedRoutine != null)
            {
                // Reset before restarting, or a flurry of taps stacks partial hops and
                // walks the piece off its cell.
                StopCoroutine(_blockedRoutine);
                EndHop();
            }

            _blockedRoutine = _hasMotionParam ? StartCoroutine(BlockedNudge())
                                              : StartCoroutine(BlockedHop());
        }

        // The hint swells the piece where it stands rather than drawing a marker over it: a
        // marker would have to be placed in world space above a board seen at 56 degrees,
        // which is the same problem the direction badge had, and it would cover the very
        // arrow the player is being taught to read.
        public void PlayHint(float duration)
        {
            if (_travelling) return;

            StopHint();
            _hintBaseScale = transform.localScale;
            _hintRoutine = StartCoroutine(HintPulse(duration));
        }

        public void StopHint()
        {
            if (_hintRoutine == null) return;

            StopCoroutine(_hintRoutine);
            _hintRoutine = null;
            // Restored from the scale captured when the hint started, never from a constant:
            // Board.FitToCell scales the root per cell size, so 1 is not this piece's size.
            transform.localScale = _hintBaseScale;
        }

        IEnumerator HintPulse(float duration)
        {
            float t = 0f;
            while (t < duration)
            {
                // Unscaled, so a hint shown while a panel is up keeps breathing.
                t += Time.unscaledDeltaTime;
                float wave = 0.5f - 0.5f * Mathf.Cos(t / Mathf.Max(0.05f, hintCycle) * Mathf.PI * 2f);
                transform.localScale = _hintBaseScale * Mathf.Lerp(1f, hintScale, wave);
                yield return null;
            }

            transform.localScale = _hintBaseScale;
            _hintRoutine = null;
        }

        IEnumerator BlockedNudge()
        {
            SetMotion(blockedMotion);
            yield return new WaitForSeconds(0.9f);
            if (!_travelling) SetMotion(PieceMotion.Idle);
            _blockedRoutine = null;
        }

        // Strains, springs, drops back: the bag tries to set off, finds its neighbour in
        // the way and lands where it started.
        IEnumerator BlockedHop()
        {
            _hopRest = transform.localPosition;
            _hopping = true;

            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / Mathf.Max(0.05f, blockedHopTime);
                float k = Mathf.Clamp01(t);

                ApplySquash(k < 0.22f ? Mathf.Lerp(1f, 0.78f, k / 0.22f)
                          : k < 0.36f ? Mathf.Lerp(0.78f, 1.14f, (k - 0.22f) / 0.14f)
                          : k < 0.82f ? 1.05f
                          : k < 0.9f ? Mathf.Lerp(1.05f, 0.84f, (k - 0.82f) / 0.08f)
                                     : Mathf.Lerp(0.84f, 1f, (k - 0.9f) / 0.1f));

                // Airborne only after the crouch, and back down before the landing squash.
                float lift = k < 0.22f
                    ? 0f
                    : Mathf.Sin(Mathf.InverseLerp(0.22f, 0.86f, k) * Mathf.PI) * _hopHeight;
                transform.localPosition = _hopRest + Vector3.up * lift;

                yield return null;
            }

            EndHop();
            _blockedRoutine = null;
        }

        void EndHop()
        {
            if (_hopping) transform.localPosition = _hopRest;
            _hopping = false;
            ApplySquash(1f);
        }

        // Tinting goes through a property block rather than Renderer.material: every
        // piece in the Toon pack shares one atlas material, and touching .material
        // would instantiate a copy per renderer and break batching for the whole board.
        void CacheRenderers()
        {
            _block = new MaterialPropertyBlock();
            _renderers = GetComponentsInChildren<Renderer>();
            _baseColors = new Color[_renderers.Length];
            _colorIds = new int[_renderers.Length];

            for (int i = 0; i < _renderers.Length; i++)
            {
                var mat = _renderers[i].sharedMaterial;
                if (mat != null && mat.HasProperty(BaseColorId)) _colorIds[i] = BaseColorId;
                else if (mat != null && mat.HasProperty(ColorId)) _colorIds[i] = ColorId;
                else _colorIds[i] = 0;

                _baseColors[i] = _colorIds[i] != 0 ? mat.GetColor(_colorIds[i]) : Color.white;
            }
        }

        // The colour this piece reads as, for effects that have to be seen against a
        // near-white floor. Taken from the most saturated material on it, which on the
        // suitcases is the shell — the rest of a case is palette-mapped trim, wheels and
        // handle, and averaging those gives a grey that disappears into the terminal.
        public Color ThemeColour
        {
            get
            {
                if (_themeColour.HasValue) return _themeColour.Value;
                if (_renderers == null) CacheRenderers();

                var best = Color.white;
                float bestSaturation = -1f;
                for (int i = 0; i < _renderers.Length; i++)
                {
                    if (_colorIds[i] == 0) continue;
                    Color.RGBToHSV(_baseColors[i], out _, out float saturation, out float value);
                    if (saturation * value <= bestSaturation) continue;
                    bestSaturation = saturation * value;
                    best = _baseColors[i];
                }

                _themeColour = best;
                return best;
            }
        }

        Color? _themeColour;

        /// <summary>Every cell this piece stands on, front first.</summary>
        public IEnumerable<Vector2Int> OccupiedCells()
        {
            Vector2Int back = -DirectionUtil.ToVector(Direction);
            for (int i = 0; i < Cells; i++) yield return GridPos + back * i;
        }

        public void ReassignPlacement(Vector2Int gridPos, Direction direction)
        {
            GridPos = gridPos;
            Direction = direction;
            // Local, not world: pieces are parented to the Board, so a rotated board
            // turns its planes with it.
            transform.localRotation = Quaternion.Euler(0f, DirectionUtil.ToYRotation(direction), 0f);
        }

        // Working out which pieces have a clear run to the edge is the puzzle, so a
        // blocked piece looks exactly like a free one. The flag is still tracked because
        // Board needs it to decide what a tap does.
        //
        // blockedDim shades blocked pieces for debugging a level's solvability; leave it
        // at zero for play, or the board reads as muddy and gives its own answer away.
        [Range(0f, 1f)] public float blockedDim = 0f;

        public void SetEscapable(bool canEscape)
        {
            CanEscape = canEscape;
            if (_renderers == null || blockedDim <= 0f) return;

            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_colorIds[i] == 0 || _renderers[i] == null) continue;
                Color c = canEscape ? _baseColors[i] : Color.Lerp(_baseColors[i], Color.black, blockedDim);

                _renderers[i].GetPropertyBlock(_block);
                _block.SetColor(_colorIds[i], c);
                _renderers[i].SetPropertyBlock(_block);
            }
        }

        // The animal rigs drive locomotion through an "animation" int parameter; the
        // Toon Planes controllers expose no parameters at all, so this is a no-op there
        // and the departure animation is driven by PlayState instead.
        //
        // Board only says whether a piece is under way; which gait that looks like is the
        // piece's business, so a theme can walk its animals or scramble them without the
        // board knowing the difference.
        public void SetMoving(bool moving)
        {
            _travelling = moving;

            // A hop still in the air when the piece is finally freed would keep writing its
            // own position over the departure, and drop it back on its old cell at the end.
            if (moving && _blockedRoutine != null)
            {
                StopCoroutine(_blockedRoutine);
                EndHop();
                _blockedRoutine = null;
            }

            SetMotion(moving ? travelMotion : PieceMotion.Idle);
            // The arrow answers "which way will this go"; once it's already going, the
            // question is answered and the marker underfoot is just clutter following it
            // down the belt.
            if (_arrow != null) _arrow.gameObject.SetActive(!moving);
        }

        public void SetMotion(PieceMotion motion)
        {
            if (_hasMotionParam) _animator.SetInteger(MotionParam, (int)motion);
        }

        // Toon Planes controllers name their states "<PrefabName>_TakeOff" / "_Landing"
        // and wire no transitions, so departures are triggered by playing the state
        // directly. Resolving the prefix from the controller keeps this working for any
        // aircraft in the pack without per-prefab configuration.
        public bool PlayState(string suffix)
        {
            if (_animator == null || _animator.runtimeAnimatorController == null) return false;

            int hash = Animator.StringToHash($"{_animator.runtimeAnimatorController.name}_{suffix}");
            if (!_animator.HasState(0, hash)) return false;

            _animator.Play(hash, 0, 0f);
            return true;
        }

        bool HasParameter(string paramName)
        {
            if (_animator == null || _animator.runtimeAnimatorController == null) return false;
            foreach (var p in _animator.parameters)
                if (p.name == paramName) return true;
            return false;
        }
    }
}
