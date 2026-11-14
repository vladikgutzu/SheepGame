using UnityEngine;

namespace SheepGame.Fx
{
    // Slides a texture along a belt surface so the conveyor is moving whether or not the
    // player is.
    //
    // It has to be a surface of its own rather than the pack's belt modules: those are
    // mapped into a shared palette atlas, where the UVs point at particular swatches, so
    // scrolling them would walk the whole model through the palette and change its colours
    // instead of moving anything.
    //
    // Driven through a MaterialPropertyBlock, not `renderer.material` — that would
    // instantiate a copy per renderer at run time, and in the editor it would leak one
    // every time the inspector repainted.
    [ExecuteAlways]
    [RequireComponent(typeof(Renderer))]
    public class ScrollingBelt : MonoBehaviour
    {
        [Tooltip("Units per second the tread appears to travel along +V. Negative runs it back.")]
        public float speed = 0.35f;

        [Tooltip("Property to slide. _BaseMap for URP Lit.")]
        public string textureProperty = "_BaseMap";

        Renderer _renderer;
        MaterialPropertyBlock _block;
        int _id;
        float _offset;

        void OnEnable()
        {
            _renderer = GetComponent<Renderer>();
            _block = new MaterialPropertyBlock();
            _id = Shader.PropertyToID(textureProperty + "_ST");
        }

        void Update()
        {
            if (_renderer == null) return;

            // Subtracted, not added. A texture offset shifts the sampling coordinate, so
            // raising it slides the pattern towards *lower* V — the mesh was built with V
            // running the way the luggage goes and the belt came out running backwards,
            // which is only visible in motion and not in any screenshot. Positive speed now
            // means what it reads as: the tread travels along +V.
            _offset -= speed * (Application.isPlaying ? Time.unscaledDeltaTime : 0.016f);
            _offset -= Mathf.Floor(_offset);

            var scale = _renderer.sharedMaterial != null
                ? _renderer.sharedMaterial.GetTextureScale(textureProperty)
                : Vector2.one;

            _renderer.GetPropertyBlock(_block);
            _block.SetVector(_id, new Vector4(scale.x, scale.y, 0f, _offset));
            _renderer.SetPropertyBlock(_block);
        }
    }
}
