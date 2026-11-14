using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SheepGame.Core.Store
{
    /// <summary>
    /// A store that runs entirely on the device: no billing SDK, no network, no money.
    /// Purchases succeed, and the receipts are kept in a file of their own.
    /// </summary>
    // It deliberately behaves like a real store rather than like a cheat button: it takes
    // a moment to answer, it can be made to fail or be cancelled, it refuses a second
    // concurrent purchase, and it keeps its receipts somewhere the game's own save cannot
    // reach. Code written against a store that always instantly succeeds breaks on the day
    // the real one is connected, and it breaks in front of paying players.
    public class LocalStoreService : IStoreService
    {
        // Receipts live in their own file, not in SaveData, because they stand in for the
        // platform's records. Wiping the player's progress must not wipe what they bought,
        // and the recovery path at startup has to have a second opinion to consult.
        const string ReceiptFile = "localstore.json";

        [Serializable]
        class Receipt
        {
            public string productId;
            public string transactionId;
            public long purchasedUnix;
        }

        [Serializable]
        class ReceiptBook
        {
            public List<Receipt> receipts = new List<Receipt>();
        }

        readonly MonoBehaviour _runner;
        readonly float _minDelay;
        readonly float _maxDelay;
        readonly float _cancelChance;
        readonly float _failChance;

        ShopCatalog _catalog;
        ReceiptBook _book = new ReceiptBook();
        bool _ready;
        bool _busy;

        public bool IsReady => _ready;

        public IReadOnlyList<ShopProduct> Products =>
            _catalog != null ? _catalog.products : new List<ShopProduct>();

        /// <param name="runner">Behaviour used to run the simulated store latency.</param>
        /// <param name="cancelChance">0..1. Raise it in QA to exercise the cancel path.</param>
        /// <param name="failChance">0..1. Raise it in QA to exercise the failure path.</param>
        public LocalStoreService(MonoBehaviour runner, float minDelay = 0.35f, float maxDelay = 0.8f,
                                 float cancelChance = 0f, float failChance = 0f)
        {
            _runner = runner;
            _minDelay = Mathf.Max(0f, minDelay);
            _maxDelay = Mathf.Max(_minDelay, maxDelay);
            _cancelChance = Mathf.Clamp01(cancelChance);
            _failChance = Mathf.Clamp01(failChance);
        }

        public void Initialize(ShopCatalog catalog, Action<bool> onReady)
        {
            _catalog = catalog;
            LoadReceipts();

            if (_catalog == null)
            {
                Debug.LogError("[LocalStore] No catalog assigned; the shop will be empty.");
                _ready = false;
                onReady?.Invoke(false);
                return;
            }

            if (!_catalog.Validate(out string problem))
                Debug.LogError($"[LocalStore] Catalog problem: {problem}");

            _ready = true;
            onReady?.Invoke(true);
        }

        public void Purchase(string productId, Action<PurchaseResult> onComplete)
        {
            if (!_ready)
            {
                onComplete?.Invoke(new PurchaseResult(productId, PurchaseStatus.NotReady,
                    message: "The store is not ready yet."));
                return;
            }

            var product = _catalog.Find(productId);
            if (product == null)
            {
                onComplete?.Invoke(new PurchaseResult(productId, PurchaseStatus.Failed,
                    message: $"Unknown product '{productId}'."));
                return;
            }

            // A real store rejects a second purchase while its sheet is up, and a shop UI
            // that lets two through double-charges the moment the network is slow.
            if (_busy)
            {
                onComplete?.Invoke(new PurchaseResult(productId, PurchaseStatus.Failed,
                    message: "Another purchase is already in progress."));
                return;
            }

            // A non-consumable this account already holds is reported as owned rather than
            // charged again, which is what a real store does with a restored purchase.
            if (product.kind == ProductKind.NonConsumable)
            {
                var owned = FindReceipt(productId);
                if (owned != null)
                {
                    onComplete?.Invoke(new PurchaseResult(productId, PurchaseStatus.AlreadyOwned,
                        owned.transactionId));
                    return;
                }
            }

            _busy = true;

            if (_runner == null)
            {
                // No behaviour to run the delay on: answer immediately rather than never.
                Complete(product, onComplete);
                return;
            }

            _runner.StartCoroutine(PurchaseRoutine(product, onComplete));
        }

        IEnumerator PurchaseRoutine(ShopProduct product, Action<PurchaseResult> onComplete)
        {
            // Unscaled: the shop is usually opened over a paused game.
            yield return new WaitForSecondsRealtime(UnityEngine.Random.Range(_minDelay, _maxDelay));

            float roll = UnityEngine.Random.value;
            if (roll < _cancelChance)
            {
                _busy = false;
                onComplete?.Invoke(new PurchaseResult(product.id, PurchaseStatus.Cancelled));
                yield break;
            }

            if (roll < _cancelChance + _failChance)
            {
                _busy = false;
                onComplete?.Invoke(new PurchaseResult(product.id, PurchaseStatus.Failed,
                    message: "Payment could not be completed."));
                yield break;
            }

            Complete(product, onComplete);
        }

        void Complete(ShopProduct product, Action<PurchaseResult> onComplete)
        {
            var receipt = new Receipt
            {
                productId = product.id,
                transactionId = Guid.NewGuid().ToString("N"),
                purchasedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            // The receipt is written before the callback fires, so a kill in between leaves
            // evidence the purchase happened and StorePurchaser can finish it next launch.
            _book.receipts.Add(receipt);
            SaveReceipts();

            _busy = false;
            onComplete?.Invoke(new PurchaseResult(product.id, PurchaseStatus.Success, receipt.transactionId));
        }

        public void Restore(Action<IReadOnlyList<string>> onRestored)
        {
            var owned = new List<string>();
            foreach (var receipt in _book.receipts)
            {
                var product = _catalog != null ? _catalog.Find(receipt.productId) : null;
                if (product == null || product.kind != ProductKind.NonConsumable) continue;
                if (!owned.Contains(product.id)) owned.Add(product.id);
            }
            onRestored?.Invoke(owned);
        }

        public string PriceLabel(string productId)
        {
            var product = _catalog != null ? _catalog.Find(productId) : null;
            // A local store has no localised prices to report, so the catalog's own label
            // is the answer here. A real service returns the store's string instead.
            return product != null ? product.fallbackPriceLabel : string.Empty;
        }

        // Nothing bought here costs anything, so nothing bought here is revenue.
        public bool IsSandbox => true;

        public bool TryGetPrice(string productId, out string currencyCode, out decimal localizedPrice)
        {
            currencyCode = null;
            localizedPrice = 0m;
            return false;
        }

        Receipt FindReceipt(string productId)
        {
            // Newest first: a consumable bought several times has several receipts, and the
            // last one is the one a caller asking about it means.
            for (int i = _book.receipts.Count - 1; i >= 0; i--)
                if (_book.receipts[i].productId == productId) return _book.receipts[i];
            return null;
        }

        public bool WasCompleted(string productId, string transactionId)
        {
            foreach (var receipt in _book.receipts)
                if (receipt.productId == productId &&
                    (string.IsNullOrEmpty(transactionId) || receipt.transactionId == transactionId))
                    return true;
            return false;
        }

        /// <summary>Throws away the simulated purchase history. Development only.</summary>
        public void ClearReceipts()
        {
            _book = new ReceiptBook();
            SaveReceipts();
        }

        // ---- receipt file -------------------------------------------------------

        static string ReceiptPath => Path.Combine(Application.persistentDataPath, ReceiptFile);

        void LoadReceipts()
        {
            try
            {
                if (!File.Exists(ReceiptPath)) { _book = new ReceiptBook(); return; }
                _book = JsonUtility.FromJson<ReceiptBook>(File.ReadAllText(ReceiptPath)) ?? new ReceiptBook();
                if (_book.receipts == null) _book.receipts = new List<Receipt>();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LocalStore] Receipts unreadable, starting empty: {e.Message}");
                _book = new ReceiptBook();
            }
        }

        void SaveReceipts()
        {
            try
            {
                File.WriteAllText(ReceiptPath, JsonUtility.ToJson(_book, true));
            }
            catch (Exception e)
            {
                Debug.LogError($"[LocalStore] Could not write receipts: {e.Message}");
            }
        }
    }
}
