using System;
using System.Collections.Generic;
using SheepGame.Core.Meta;
using UnityEngine;

namespace SheepGame.Core.Store
{
    public enum ProductKind
    {
        /// <summary>Can be bought again and again. Coins, boosters.</summary>
        Consumable,
        /// <summary>Bought once and owned forever. Starter pack, cosmetics.</summary>
        NonConsumable
    }

    /// <summary>
    /// One thing the shop sells: what it costs, and exactly what it hands over.
    /// </summary>
    // What a product grants is data, not code, so adding a coin pack is an edit to an
    // asset rather than a new branch in a switch that some other branch will forget.
    [Serializable]
    public class ShopProduct
    {
        [Tooltip("Store product id. Must match the id registered with Google Play / App " +
                 "Store exactly, because that is the only thing tying the two together.")]
        public string id;

        public ProductKind kind = ProductKind.Consumable;

        [Header("Presentation")]
        public string title;
        public string description;
        [Tooltip("Shown until a real store reports its own localised price. A live store " +
                 "always overwrites this — never show it once IStoreService.IsReady is true.")]
        public string fallbackPriceLabel = "$0.99";
        [Tooltip("Corner flag: BEST VALUE, POPULAR, and so on. Empty for none.")]
        public string badge;

        [Header("What the player gets")]
        public int coins;
        public List<CountEntry> items = new List<CountEntry>();
        [Tooltip("Granted for a NonConsumable, and restored by Restore Purchases. " +
                 "Leave empty for a consumable.")]
        public string entitlement;

        public bool IsOwned =>
            kind == ProductKind.NonConsumable &&
            !string.IsNullOrEmpty(entitlement) &&
            GameState.HasEntitlement(entitlement);
    }

    /// <summary>Everything the shop sells, in display order.</summary>
    [CreateAssetMenu(fileName = "ShopCatalog", menuName = "SheepGame/Shop Catalog")]
    public class ShopCatalog : ScriptableObject
    {
        public List<ShopProduct> products = new List<ShopProduct>();

        public ShopProduct Find(string productId)
        {
            for (int i = 0; i < products.Count; i++)
                if (products[i] != null && products[i].id == productId) return products[i];
            return null;
        }

        /// <summary>
        /// Catches the two catalog mistakes that only show up as a broken purchase:
        /// a duplicate id, and a non-consumable with nothing to remember it by.
        /// </summary>
        public bool Validate(out string problem)
        {
            var seen = new HashSet<string>();
            for (int i = 0; i < products.Count; i++)
            {
                var p = products[i];
                if (p == null || string.IsNullOrWhiteSpace(p.id))
                {
                    problem = $"Product {i} has no id.";
                    return false;
                }
                if (!seen.Add(p.id))
                {
                    problem = $"Duplicate product id '{p.id}'.";
                    return false;
                }
                if (p.kind == ProductKind.NonConsumable && string.IsNullOrWhiteSpace(p.entitlement))
                {
                    problem = $"'{p.id}' is non-consumable but grants no entitlement, so " +
                              "owning it could never be remembered or restored.";
                    return false;
                }
            }

            problem = null;
            return true;
        }
    }
}
