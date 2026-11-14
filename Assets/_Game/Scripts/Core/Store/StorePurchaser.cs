using System;
using System.Collections.Generic;
using SheepGame.Core.Meta;
using UnityEngine;

namespace SheepGame.Core.Store
{
    /// <summary>
    /// The one place a purchase turns into coins and boosters. Owns the store service,
    /// journals purchases so a crash cannot swallow one, and grants what was bought.
    /// </summary>
    // Swapping in a real billing SDK means replacing the one line in CreateService and
    // nothing else: the journal, the granting and every call site stay as they are.
    public class StorePurchaser : MonoBehaviour
    {
        public ShopCatalog catalog;

        [Header("Local store simulation")]
        [Tooltip("Seconds the fake store takes to answer, so the UI is built against a " +
                 "store that is not instant — because no real one is.")]
        public float minDelay = 0.35f;
        public float maxDelay = 0.8f;
        [Tooltip("QA dial: chance the player is treated as having backed out. Ship at 0.")]
        [Range(0f, 1f)] public float cancelChance;
        [Tooltip("QA dial: chance the payment is treated as declined. Ship at 0.")]
        [Range(0f, 1f)] public float failChance;

        IStoreService _service;
        bool _busy;

        public IStoreService Service => _service;
        public bool IsReady => _service != null && _service.IsReady;
        /// <summary>True while a purchase is in flight; the shop disables its buttons on it.</summary>
        public bool Busy => _busy;

        /// <summary>Fires for every finished purchase, cancels and failures included.</summary>
        public event Action<PurchaseResult> PurchaseCompleted;
        public event Action StoreReadyChanged;

        void Awake()
        {
            _service = CreateService();
            _service.Initialize(catalog, ready =>
            {
                if (ready) RecoverInterruptedPurchase();
                StoreReadyChanged?.Invoke();
            });
        }

        // The single seam between this game and a billing SDK.
        IStoreService CreateService() =>
            new LocalStoreService(this, minDelay, maxDelay, cancelChance, failChance);

        public string PriceLabel(string productId) =>
            _service != null ? _service.PriceLabel(productId) : string.Empty;

        public void Buy(string productId, Action<PurchaseResult> onComplete = null)
        {
            Telemetry.PurchaseStarted(productId);

            if (_service == null || !_service.IsReady)
            {
                Finish(new PurchaseResult(productId, PurchaseStatus.NotReady,
                    message: "The store is not ready yet."), onComplete);
                return;
            }

            if (_busy)
            {
                Finish(new PurchaseResult(productId, PurchaseStatus.Failed,
                    message: "Another purchase is already in progress."), onComplete);
                return;
            }

            var product = catalog != null ? catalog.Find(productId) : null;
            if (product == null)
            {
                Finish(new PurchaseResult(productId, PurchaseStatus.Failed,
                    message: $"Unknown product '{productId}'."), onComplete);
                return;
            }

            _busy = true;

            // Journalled before the store is asked, so if the app dies during the payment
            // sheet the next launch knows to go and check whether it went through.
            GameState.Data.pendingPurchase = new PurchaseJournalEntry
            {
                productId = productId,
                transactionId = null,
                startedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
            GameState.FlushNow();

            _service.Purchase(productId, result =>
            {
                _busy = false;

                switch (result.Status)
                {
                    case PurchaseStatus.Success:
                        Grant(product, result.TransactionId);
                        break;

                    // Owned already means the coins and boosters bundled with it were handed
                    // over at the original purchase. Running the full grant here would pay
                    // them out again for free, every time the product is tapped — so only
                    // the entitlement, which is idempotent, is re-applied.
                    case PurchaseStatus.AlreadyOwned:
                        ReapplyEntitlement(product);
                        break;

                    default:
                        ClearPending();
                        break;
                }

                Finish(result, onComplete);
            });
        }

        /// <summary>Re-grants non-consumables this account already owns. Required by the App Store.</summary>
        public void RestorePurchases(Action<int> onRestored = null)
        {
            if (_service == null || !_service.IsReady) { onRestored?.Invoke(0); return; }

            _service.Restore(owned =>
            {
                int granted = 0;
                foreach (string productId in owned)
                {
                    var product = catalog != null ? catalog.Find(productId) : null;
                    if (product == null || string.IsNullOrEmpty(product.entitlement)) continue;
                    if (GameState.HasEntitlement(product.entitlement)) continue;

                    GameState.GrantEntitlement(product.entitlement);
                    granted++;
                }

                if (granted > 0) GameState.FlushNow();
                onRestored?.Invoke(granted);
            });
        }

        // Everything the purchase hands over, plus clearing the journal, goes into one
        // save write. Either the player has the goods and the journal is clear, or neither
        // happened — there is no state where we charged and delivered nothing.
        void Grant(ShopProduct product, string transactionId)
        {
            if (product.coins > 0) GameState.AddCoins(product.coins);

            if (product.items != null)
                foreach (var item in product.items)
                    GameState.GrantItem(item.id, item.count);

            // Granting an entitlement is idempotent, so a re-grant after a recovery or a
            // restore adds nothing and cannot go wrong.
            if (!string.IsNullOrEmpty(product.entitlement))
                GameState.GrantEntitlement(product.entitlement);

            GameState.Data.pendingPurchase = null;
            GameState.MarkDirty();
            GameState.FlushNow();

            Debug.Log($"[Store] Granted '{product.id}' (transaction {transactionId}).");
        }

        // Re-asserts ownership without paying the product's contents out again. This is the
        // whole difference between "you own this" and "you just bought this".
        void ReapplyEntitlement(ShopProduct product)
        {
            if (!string.IsNullOrEmpty(product.entitlement))
                GameState.GrantEntitlement(product.entitlement);

            ClearPending();
        }

        void ClearPending()
        {
            if (GameState.Data.pendingPurchase == null) return;
            GameState.Data.pendingPurchase = null;
            GameState.MarkDirty();
            GameState.FlushNow();
        }

        // A purchase that was in flight when the app died: the store's own receipts are
        // the second opinion that says whether the player actually paid.
        void RecoverInterruptedPurchase()
        {
            var pending = GameState.Data.pendingPurchase;
            if (pending == null || string.IsNullOrEmpty(pending.productId)) return;

            var product = catalog != null ? catalog.Find(pending.productId) : null;
            if (product == null) { ClearPending(); return; }

            if (_service.WasCompleted(pending.productId, pending.transactionId))
            {
                Debug.Log($"[Store] Finishing interrupted purchase of '{pending.productId}'.");
                Grant(product, pending.transactionId);
                var recovered = new PurchaseResult(pending.productId, PurchaseStatus.Success, pending.transactionId);
                // Revenue like any other purchase; Telemetry deduplicates by transaction id.
                Telemetry.PurchaseFinished(recovered, _service, productKnown: true);
                PurchaseCompleted?.Invoke(recovered);
            }
            else
            {
                // Never paid for: the sheet was open and the app went away.
                ClearPending();
            }
        }

        void Finish(PurchaseResult result, Action<PurchaseResult> onComplete)
        {
            bool known = catalog != null && catalog.Find(result.ProductId) != null;
            Telemetry.PurchaseFinished(result, _service, known);
            onComplete?.Invoke(result);
            PurchaseCompleted?.Invoke(result);
        }

        /// <summary>Wipes the simulated purchase history. Development only.</summary>
        public void ClearLocalReceipts()
        {
            (_service as LocalStoreService)?.ClearReceipts();
        }
    }
}
