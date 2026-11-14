using System;
using System.Collections.Generic;

namespace SheepGame.Core.Store
{
    public enum PurchaseStatus
    {
        Success,
        /// <summary>The player backed out. Not an error; say nothing.</summary>
        Cancelled,
        /// <summary>Payment declined, network gone, store unavailable. Worth a message.</summary>
        Failed,
        /// <summary>A non-consumable this account already owns. Grant it rather than charge again.</summary>
        AlreadyOwned,
        /// <summary>Asked before the store was ready.</summary>
        NotReady
    }

    public readonly struct PurchaseResult
    {
        public readonly string ProductId;
        public readonly PurchaseStatus Status;
        public readonly string TransactionId;
        public readonly string Message;
        /// <summary>The store's receipt for a completed purchase, when it hands one over.</summary>
        public readonly string Receipt;

        public PurchaseResult(string productId, PurchaseStatus status,
                              string transactionId = null, string message = null, string receipt = null)
        {
            ProductId = productId;
            Status = status;
            TransactionId = transactionId;
            Message = message;
            Receipt = receipt;
        }

        /// <summary>The player is entitled to the product, whether they just paid or already owned it.</summary>
        // Deliberately not the test for whether to hand the product's contents over: an
        // AlreadyOwned result must re-apply the entitlement and nothing else, or the coins
        // and boosters bundled with a non-consumable are paid out again on every tap. That
        // bug shipped into a play-test here. StorePurchaser branches on Status for that reason.
        public bool Owned => Status == PurchaseStatus.Success || Status == PurchaseStatus.AlreadyOwned;
    }

    /// <summary>
    /// Everything the game needs from a store. The local implementation and a real
    /// billing SDK sit behind this and nothing else changes when they are swapped.
    /// </summary>
    // Callback-shaped rather than async/await on purpose: Google Play Billing and Unity
    // IAP both hand results back through callbacks on the main thread, so matching that
    // shape makes the swap a one-class edit instead of a rewrite of every call site.
    public interface IStoreService
    {
        bool IsReady { get; }

        IReadOnlyList<ShopProduct> Products { get; }

        /// <summary>Fetches the catalog and any purchases this account already owns.</summary>
        void Initialize(ShopCatalog catalog, Action<bool> onReady);

        /// <summary>
        /// Starts a purchase. The callback always fires exactly once, including for a
        /// cancel — a flow that only handles success leaves the UI stuck on a spinner
        /// the first time someone taps outside the payment sheet.
        /// </summary>
        void Purchase(string productId, Action<PurchaseResult> onComplete);

        /// <summary>Re-reports non-consumables this account owns. Required by the App Store.</summary>
        void Restore(Action<IReadOnlyList<string>> onRestored);

        /// <summary>Localised price to show, or the catalog fallback before the store answers.</summary>
        string PriceLabel(string productId);

        /// <summary>
        /// Whether purchases through this store are tests rather than money. Revenue is only
        /// reported for a store that says no.
        /// </summary>
        bool IsSandbox { get; }

        /// <summary>
        /// The store's own price for a product: an ISO 4217 currency code and the localised
        /// amount. False before the store has answered, or for a store with no real prices.
        /// </summary>
        bool TryGetPrice(string productId, out string currencyCode, out decimal localizedPrice);

        /// <summary>
        /// Whether the store still holds a completed transaction the game never granted.
        /// Called at startup to finish a purchase interrupted by a crash or a kill.
        /// </summary>
        bool WasCompleted(string productId, string transactionId);
    }
}
