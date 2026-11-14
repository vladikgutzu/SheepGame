using System.Collections.Generic;
using UnityEngine;

namespace SheepGame.EditorTools
{
    // Remembers what each prefab looked like before its shell was split off, so the change
    // can be undone and so a re-run always starts from the imported art.
    //
    // In its own file because Unity will not bind a ScriptableObject asset to a class
    // declared beside another one: the asset is created, loads with a null script, and
    // forgets everything the next time the domain reloads.
    public class CaseShellManifest : ScriptableObject
    {
        [System.Serializable]
        public class Entry
        {
            public GameObject prefab;
            public string childPath;
            public Mesh original;
            public Mesh generated;
            public Material[] originalMaterials;
            public Material shellMaterial;
        }

        public List<Entry> entries = new List<Entry>();
    }
}
