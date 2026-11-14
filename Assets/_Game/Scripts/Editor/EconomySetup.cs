using System.Collections.Generic;
using System.IO;
using SheepGame.Core;
using SheepGame.Core.Meta;
using SheepGame.Core.Store;
using UnityEditor;
using UnityEngine;

namespace SheepGame.EditorTools
{
    // Creates the two assets the economy runs on and puts a store in the scene, so a scene
    // that has never seen any of this comes up working rather than with a shop full of
    // nothing. Everything here is create-if-missing: it never overwrites tuned numbers or a
    // catalog someone has edited.
    public static class EconomySetup
    {
        const string DataDir = "Assets/_Game/Data";
        const string EconomyPath = DataDir + "/EconomyConfig.asset";
        const string CatalogPath = DataDir + "/ShopCatalog.asset";

        [MenuItem("SheepGame/Create Economy Assets")]
        public static void CreateAssets()
        {
            var gm = Object.FindFirstObjectByType<GameManager>(FindObjectsInactive.Include);
            if (gm == null)
            {
                EnsureEconomyConfig();
                EnsureCatalog();
                Debug.Log("Created the economy assets. No GameManager in the scene to wire them to.");
                return;
            }

            EnsureEconomy(gm);
            Debug.Log("Economy assets are in place and wired.");
        }

        /// <summary>
        /// Makes sure the config, the catalog and a StorePurchaser all exist and are
        /// pointed at each other. Returns the purchaser the shop UI should talk to.
        /// </summary>
        public static StorePurchaser EnsureEconomy(GameManager gm)
        {
            var economy = EnsureEconomyConfig();
            var catalog = EnsureCatalog();

            if (gm != null && gm.economy == null)
            {
                Undo.RecordObject(gm, "Create Economy Assets");
                gm.economy = economy;
                EditorUtility.SetDirty(gm);
            }

            var purchaser = Object.FindFirstObjectByType<StorePurchaser>(FindObjectsInactive.Include);
            if (purchaser == null && gm != null)
                purchaser = Undo.AddComponent<StorePurchaser>(gm.gameObject);

            if (purchaser != null && purchaser.catalog == null)
            {
                Undo.RecordObject(purchaser, "Create Economy Assets");
                purchaser.catalog = catalog;
                EditorUtility.SetDirty(purchaser);
            }

            return purchaser;
        }

        static EconomyConfig EnsureEconomyConfig()
        {
            var existing = AssetDatabase.LoadAssetAtPath<EconomyConfig>(EconomyPath);
            if (existing != null) return existing;

            EnsureFolder();
            var config = ScriptableObject.CreateInstance<EconomyConfig>();
            AssetDatabase.CreateAsset(config, EconomyPath);
            AssetDatabase.SaveAssets();
            return config;
        }

        // The opening catalog. Prices are the usual mobile ladder — a cheap top-up, a
        // middle pack that is the one most people buy, and a large one that is the best
        // value per coin — plus a one-time starter that only makes sense early.
        static ShopCatalog EnsureCatalog()
        {
            var existing = AssetDatabase.LoadAssetAtPath<ShopCatalog>(CatalogPath);
            if (existing != null) return existing;

            EnsureFolder();
            var catalog = ScriptableObject.CreateInstance<ShopCatalog>();

            catalog.products = new List<ShopProduct>
            {
                new ShopProduct
                {
                    id = "starter.pack",
                    kind = ProductKind.NonConsumable,
                    title = "Starter pack",
                    description = "1,000 coins and a booster of each",
                    fallbackPriceLabel = "$4.99",
                    badge = "One time",
                    coins = 1000,
                    entitlement = "starter_pack",
                    items = new List<CountEntry>
                    {
                        new CountEntry { id = ItemIds.Hint, count = 5 },
                        new CountEntry { id = ItemIds.Shuffle, count = 3 },
                        new CountEntry { id = ItemIds.Remove, count = 3 },
                        new CountEntry { id = ItemIds.ExtraTaps, count = 3 }
                    }
                },
                new ShopProduct
                {
                    id = "coins.small",
                    title = "Pile of coins",
                    description = "500 coins",
                    fallbackPriceLabel = "$0.99",
                    coins = 500
                },
                new ShopProduct
                {
                    id = "coins.medium",
                    title = "Bag of coins",
                    description = "1,400 coins",
                    fallbackPriceLabel = "$2.99",
                    badge = "Popular",
                    coins = 1400
                },
                new ShopProduct
                {
                    id = "coins.large",
                    title = "Case of coins",
                    description = "5,000 coins",
                    fallbackPriceLabel = "$7.99",
                    badge = "Best value",
                    coins = 5000
                },
                new ShopProduct
                {
                    id = "boosters.pack",
                    title = "Booster pack",
                    description = "8 hints, 5 shuffles, 5 removes",
                    fallbackPriceLabel = "$2.99",
                    items = new List<CountEntry>
                    {
                        new CountEntry { id = ItemIds.Hint, count = 8 },
                        new CountEntry { id = ItemIds.Shuffle, count = 5 },
                        new CountEntry { id = ItemIds.Remove, count = 5 }
                    }
                },
                new ShopProduct
                {
                    id = "taps.pack",
                    // Still the booster.extra_taps item underneath, so saves keep what they
                    // hold; since the queue replaced the tap limit it is a free continue.
                    title = "Rescue pack",
                    description = "10 free continues: +1 space or +time",
                    fallbackPriceLabel = "$1.99",
                    items = new List<CountEntry>
                    {
                        new CountEntry { id = ItemIds.ExtraTaps, count = 10 }
                    }
                }
            };

            AssetDatabase.CreateAsset(catalog, CatalogPath);
            AssetDatabase.SaveAssets();
            return catalog;
        }

        static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder(DataDir))
                AssetDatabase.CreateFolder("Assets/_Game", "Data");
        }

        // ---- development ------------------------------------------------------------

        // Testing the first-launch grants, the tap budget or a purchase means starting from
        // nothing, and the save is a file outside the project that no amount of reimporting
        // clears.
        [MenuItem("SheepGame/Debug/Reset Player Save")]
        public static void ResetSave()
        {
            if (!EditorUtility.DisplayDialog("Reset player save",
                    "Delete progress, coins and boosters on this machine? Purchases are kept.",
                    "Delete", "Cancel"))
                return;

            SaveSystem.Delete();
            Debug.Log("Player save deleted. It is rebuilt on the next play.");
        }

        [MenuItem("SheepGame/Debug/Clear Store Receipts")]
        public static void ClearReceipts()
        {
            string path = Path.Combine(Application.persistentDataPath, "localstore.json");
            if (File.Exists(path)) File.Delete(path);
            Debug.Log("Local store receipts cleared; non-consumables can be bought again.");
        }

        [MenuItem("SheepGame/Debug/Open Save Folder")]
        public static void OpenSaveFolder() =>
            EditorUtility.RevealInFinder(Application.persistentDataPath + "/");
    }
}
