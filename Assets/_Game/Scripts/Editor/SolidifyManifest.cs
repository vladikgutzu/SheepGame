using System.Collections.Generic;
using UnityEngine;

namespace SheepGame.EditorTools
{
    // Records which prefab meshes were replaced, and by what, so the change can be redone
    // after the source art is re-imported and undone without hunting for the original.
    //
    // In its own file because Unity will not bind a ScriptableObject asset to a class
    // declared beside another one: the asset is created, loads with a null script, and
    // forgets everything the next time the domain reloads.
    public class SolidifyManifest : ScriptableObject
    {
        [System.Serializable]
        public class Entry
        {
            public GameObject prefab;
            public string childPath;
            public Mesh original;
            public Mesh generated;
        }

        public List<Entry> entries = new List<Entry>();
    }
}
