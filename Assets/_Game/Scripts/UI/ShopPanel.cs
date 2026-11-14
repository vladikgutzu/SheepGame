using System;
using System.Collections;
using System.Collections.Generic;
using SheepGame.Core.Meta;
using SheepGame.Core.Store;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SheepGame.Core
{
    /// <summary>
    /// The shop. Fills itself from the catalog, buys through StorePurchaser and says
    /// what happened.
    /// </summary>
    public class ShopPanel : MonoBehaviour
    {
        // The card's footer, measured up from its bottom edge. Here rather than in the builder
        // for the same reason the row metrics are in ShopRowView: only the runtime knows
        // whether there is a status line to make room for.
        public const float FooterMargin = 30f;   // the gap between blocks, HUD-wide
        public const float RestoreHeight = 100f;
        public const float StatusHeight = 48f;
        public const float StatusGap = 26f;

        /// <summary>Where the list's bottom edge sits with no status line under it.</summary>
        public const float ListBottomPlain = FooterMargin + RestoreHeight + FooterMargin;

        /// <summary>And with one. The difference is the band that used to be reserved always.</summary>
        public const float ListBottomWithStatus =
            FooterMargin + RestoreHeight + StatusGap + StatusHeight + StatusGap;

        /// <summary>Centre of the restore button, up from the card's bottom edge.</summary>
        public const float RestoreCentre = FooterMargin + RestoreHeight * 0.5f;

        /// <summary>Centre of the status line, when it is there.</summary>
        public const float StatusCentre = FooterMargin + RestoreHeight + StatusGap + StatusHeight * 0.5f;

        public StorePurchaser purchaser;
        [Tooltip("Provides the three optional rewarded booster offers shown above paid products.")]
        public AdsManager adsManager;
        [Tooltip("Used only to reuse the exact Hint, Shuffle and Remove artwork already on the HUD.")]
        public UIBindings uiBindings;

        [Header("Parts")]
        [Tooltip("The overlay switched on and off. Usually this object.")]
        public GameObject root;
        [Tooltip("Rows are cloned into this.")]
        public RectTransform listParent;
        [Tooltip("Inactive row cloned per product, built by SheepGame > Build Game UI.")]
        public ShopRowView rowTemplate;
        [Tooltip("One line of feedback under the list: bought, cancelled, declined.")]
        public TMP_Text statusLabel;
        [Tooltip("The scroll viewport. Its bottom edge moves down over the status line's band " +
                 "whenever there is no status line to put in it.")]
        public RectTransform scrollArea;
        public Button closeButton;
        public Button restoreButton;

        [Header("Wording")]
        public string purchasedMessage = "Purchase complete. Enjoy!";
        public string cancelledMessage = "Purchase cancelled.";
        public string failedMessage = "Purchase could not be completed.";
        public string notReadyMessage = "The store is not available right now.";
        public string restoredFormat = "Restored {0} purchase(s).";
        public string nothingToRestoreMessage = "Nothing to restore.";
        public string workingMessage = "Working...";
        public string rewardedLoadingMessage = "Loading video...";
        public string rewardedWorkingMessage = "Playing video...";
        public string rewardedUnavailableMessage = "Video isn't ready yet. Try again shortly.";
        public string rewardedNotEarnedMessage = "Watch the full video to earn the booster.";
        public string rewardedGrantedFormat = "+1 {0} added!";

        /// <summary>A row on screen and the product it was built from.</summary>
        // Paired explicitly because the two lists stopped matching the moment the shop began
        // leaving products out. RefreshRows used to walk them by index, which was only ever
        // right while every product got a row: skip one and every row after it is refreshed
        // against its neighbour's product — the wrong price, the wrong Owned chip, and a Buy
        // button wired to something else.
        readonly struct Row
        {
            public readonly ShopRowView View;
            public readonly ShopProduct Product;

            public Row(ShopRowView view, ShopProduct product)
            {
                View = view;
                Product = product;
            }
        }

        readonly List<Row> _rows = new List<Row>();
        readonly List<RewardRow> _rewardRows = new List<RewardRow>();
        bool _rewardBusy;
        bool _rewardEarned;
        bool _audioWasPaused;
        Coroutine _rewardWaitRoutine;

        readonly struct RewardRow
        {
            public readonly ShopRowView View;
            public readonly string ItemId;
            public RewardRow(ShopRowView view, string itemId) { View = view; ItemId = itemId; }
        }

        public bool IsOpen => root != null && root.activeSelf;

        void Awake()
        {
            if (root == null) root = gameObject;
            if (adsManager == null) adsManager = FindFirstObjectByType<AdsManager>(FindObjectsInactive.Include);
            if (uiBindings == null) uiBindings = FindFirstObjectByType<UIBindings>(FindObjectsInactive.Include);
            // Kept in the scene rather than in a prefab so the builder can style it, but
            // it must never be visible or counted as a row.
            if (rowTemplate != null) rowTemplate.gameObject.SetActive(false);

            if (closeButton != null) closeButton.onClick.AddListener(Close);
            if (restoreButton != null) restoreButton.onClick.AddListener(Restore);
        }

        void OnEnable()
        {
            GameState.Changed += RefreshRows;
            if (purchaser != null) purchaser.StoreReadyChanged += Rebuild;
            if (adsManager != null) adsManager.RewardedAvailabilityChanged += HandleRewardedAvailability;
        }

        void OnDisable()
        {
            CancelRewardWait();
            GameState.Changed -= RefreshRows;
            if (purchaser != null) purchaser.StoreReadyChanged -= Rebuild;
            if (adsManager != null) adsManager.RewardedAvailabilityChanged -= HandleRewardedAvailability;
        }

        public void Open()
        {
            if (root == null) return;
            root.SetActive(true);
            SetStatus(string.Empty);
            Rebuild();
        }

        public void Close()
        {
            if (root != null) root.SetActive(false);
            SheepGame.Audio.AudioManager.PanelClose();
        }

        public void Toggle()
        {
            if (IsOpen) Close(); else Open();
        }

        void Rebuild()
        {
            if (rowTemplate == null || listParent == null || purchaser == null) return;

            foreach (var row in _rows)
            {
                if (row.View == null) continue;
                // Unparented before it is destroyed: Destroy is deferred to the end of the
                // frame, so a rebuild in the same frame as the last one would otherwise leave
                // the layout group counting both sets of rows.
                row.View.transform.SetParent(null, false);
                Destroy(row.View.gameObject);
            }
            _rows.Clear();

            foreach (var reward in _rewardRows)
            {
                if (reward.View == null) continue;
                reward.View.transform.SetParent(null, false);
                Destroy(reward.View.gameObject);
            }
            _rewardRows.Clear();

            AddRewardRow(ItemIds.Hint, "Free Hint", "Watch a video to get 1 Hint");
            AddRewardRow(ItemIds.Shuffle, "Free Shuffle", "Watch a video to get 1 Shuffle");
            AddRewardRow(ItemIds.Remove, "Free Remove", "Watch a video to get 1 Remove");

            var catalog = purchaser.catalog;
            if (catalog == null) return;

            foreach (var product in catalog.products)
            {
                if (product == null || string.IsNullOrEmpty(product.id)) continue;
                if (!ShouldList(product)) continue;

                var row = Instantiate(rowTemplate, listParent);
                row.gameObject.SetActive(true);
                row.name = "Row_" + product.id;
                Fill(row, product);
                _rows.Add(new Row(row, product));
            }
        }

        void AddRewardRow(string itemId, string title, string description)
        {
            if (adsManager == null || !adsManager.IsRewardedConfigured(itemId)) return;
            var row = Instantiate(rowTemplate, listParent);
            row.gameObject.SetActive(true);
            row.name = "Reward_" + itemId;
            row.SetBadge(true);
            if (row.badgeLabel != null) row.badgeLabel.text = "FREE";
            if (row.title != null) row.title.text = title;
            if (row.description != null) row.description.text = description;
            if (row.ownedTag != null) row.ownedTag.SetActive(false);
            if (row.price != null)
            {
                row.price.gameObject.SetActive(true);
                row.price.text = "WATCH";
            }
            ApplyRewardArtwork(row, itemId);
            if (row.buyButton != null)
            {
                row.buyButton.onClick.RemoveAllListeners();
                row.buyButton.onClick.AddListener(() => WatchRewarded(itemId));
                // Keep the offer tappable while an ad is loading. A disabled WATCH button
                // gives no feedback at all on a slow or no-fill connection; tapping now asks
                // for another safe load attempt and explains the state on the status line.
                row.buyButton.interactable = !_rewardBusy;
            }
            _rewardRows.Add(new RewardRow(row, itemId));
        }

        void ApplyRewardArtwork(ShopRowView row, string itemId)
        {
            Button source = itemId == ItemIds.Hint ? uiBindings?.hintButton
                          : itemId == ItemIds.Shuffle ? uiBindings?.shuffleButton
                          : uiBindings?.removeButton;
            if (source == null || row.coinIcon == null) return;
            foreach (var image in source.GetComponentsInChildren<Image>(true))
            {
                if (image.sprite == null || image.name.IndexOf("icon", StringComparison.OrdinalIgnoreCase) < 0) continue;
                var target = row.coinIcon.GetComponent<Image>();
                if (target != null)
                {
                    target.sprite = image.sprite;
                    target.type = Image.Type.Simple;
                    target.preserveAspect = true;
                    target.color = Color.white;
                }
                break;
            }
        }

        void WatchRewarded(string itemId)
        {
            if (_rewardBusy || adsManager == null) return;
            if (!adsManager.IsRewardedReady(itemId))
            {
                // A mobile rewarded ad often needs a few seconds after SDK init or after the
                // previous impression. Treat WATCH as an intent to watch: keep the request
                // alive briefly and show as soon as it becomes ready instead of rejecting the
                // first tap while the network is still answering.
                _rewardBusy = true;
                adsManager.RequestRewarded(itemId);
                SetStatus(rewardedLoadingMessage);
                SetRowsInteractable(false);
                _rewardWaitRoutine = StartCoroutine(WaitForRewarded(itemId));
                return;
            }

            BeginRewarded(itemId);
        }

        IEnumerator WaitForRewarded(string itemId)
        {
            const float waitSeconds = 15f;
            float deadline = Time.realtimeSinceStartup + waitSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (adsManager == null || !adsManager.IsRewardedConfigured(itemId)) break;
                if (adsManager.IsRewardedReady(itemId))
                {
                    _rewardWaitRoutine = null;
                    BeginRewarded(itemId);
                    yield break;
                }
                yield return null;
            }

            _rewardWaitRoutine = null;
            _rewardBusy = false;
            SetStatus(rewardedUnavailableMessage);
            SetRowsInteractable(true);
        }

        void BeginRewarded(string itemId)
        {
            _rewardBusy = true;
            _rewardEarned = false;
            _audioWasPaused = AudioListener.pause;
            AudioListener.pause = true;
            SetStatus(rewardedWorkingMessage);
            SetRowsInteractable(false);

            bool shown = adsManager.ShowRewardedBooster(itemId,
                () =>
                {
                    _rewardEarned = true;
                    SetStatus(string.Format(rewardedGrantedFormat, RewardName(itemId)));
                    RefreshRows();
                },
                () =>
                {
                    AudioListener.pause = _audioWasPaused;
                    _rewardBusy = false;
                    if (!_rewardEarned) SetStatus(rewardedNotEarnedMessage);
                    SetRowsInteractable(true);
                });

            if (shown) return;
            AudioListener.pause = _audioWasPaused;
            _rewardBusy = false;
            SetStatus(rewardedUnavailableMessage);
            SetRowsInteractable(true);
        }

        void CancelRewardWait()
        {
            if (_rewardWaitRoutine != null)
            {
                StopCoroutine(_rewardWaitRoutine);
                _rewardWaitRoutine = null;
            }
            _rewardBusy = false;
        }

        static string RewardName(string itemId) => itemId == ItemIds.Hint ? "Hint"
            : itemId == ItemIds.Shuffle ? "Shuffle" : "Remove";

        void HandleRewardedAvailability(string _) => RefreshRewardRows();

        void RefreshRewardRows()
        {
            foreach (var reward in _rewardRows)
            {
                if (reward.View == null || reward.View.buyButton == null) continue;
                reward.View.buyButton.interactable = !_rewardBusy && adsManager != null;
            }
        }

        void Fill(ShopRowView row, ShopProduct product)
        {
            row.ApplyArtwork(product.id);
            if (row.title != null) row.title.text = product.title;
            if (row.description != null) row.description.text = product.description;

            // Through the row rather than straight at the object: a row without a badge gives
            // the badge's band back and shortens, instead of standing a head taller than its
            // contents need with nothing in the gap.
            bool hasBadge = !string.IsNullOrWhiteSpace(product.badge);
            row.SetBadge(hasBadge);
            if (hasBadge && row.badgeLabel != null) row.badgeLabel.text = product.badge;

            ApplyOwnership(row, product);

            if (row.buyButton != null)
            {
                row.buyButton.onClick.RemoveAllListeners();
                row.buyButton.onClick.AddListener(() => Buy(product));
            }
        }

        void ApplyOwnership(ShopRowView row, ShopProduct product)
        {
            bool owned = product.IsOwned;

            if (row.ownedTag != null) row.ownedTag.SetActive(owned);
            if (row.price != null)
            {
                row.price.gameObject.SetActive(!owned);
                // The live store's own localised price wins as soon as it has one; the
                // catalog's label is only ever a stand-in before that.
                if (!owned) row.price.text = purchaser.PriceLabel(product.id);
            }

            // With ShouldList dropping owned products there is no longer a path that reaches
            // here with owned true: granting an entitlement always marks the save dirty, which
            // rebuilds the list before anything is drawn. Kept as the guard for the day
            // ShouldList changes its mind — the price and the Buy button are then already
            // right, and only the shelf has to be reconsidered.
            if (row.buyButton != null) row.buyButton.interactable = owned == false && !purchaser.Busy;
        }

        /// <summary>
        /// Whether a product belongs on the shelf at all. A non-consumable that has been
        /// bought does not: it can never be bought again.
        /// </summary>
        // It used to stay, greyed out, as a receipt. That reasoning held while the shop was
        // the only place a purchase could be confirmed — but Restore already reports what it
        // found on the status line, so the receipt was costing the top slot of the shelf, and
        // its badge, to a row that could never be sold again.
        static bool ShouldList(ShopProduct product) => !product.IsOwned;

        void RefreshRows()
        {
            // Ownership changing is not a refresh, it is a rebuild: the row leaves the shelf
            // entirely. Counted rather than compared row by row so it reads both ways — a
            // product that stops being owned comes back, which is what SheepGame > Debug >
            // Reset Player Save does, and a shop left open across it would otherwise go on
            // hiding a pack the player no longer owns.
            var catalog = purchaser != null ? purchaser.catalog : null;
            if (catalog == null) return;

            int listable = 0;
            foreach (var product in catalog.products)
                if (product != null && !string.IsNullOrEmpty(product.id) && ShouldList(product))
                    listable++;

            if (listable != _rows.Count)
            {
                Rebuild();
                return;
            }

            foreach (var row in _rows)
                if (row.View != null) ApplyOwnership(row.View, row.Product);
        }

        void Buy(ShopProduct product)
        {
            if (purchaser == null || purchaser.Busy) return;

            SetStatus(workingMessage);
            SetRowsInteractable(false);

            purchaser.Buy(product.id, result =>
            {
                SetRowsInteractable(true);

                switch (result.Status)
                {
                    case PurchaseStatus.Success:
                    case PurchaseStatus.AlreadyOwned:
                        SetStatus(purchasedMessage);
                        break;
                    case PurchaseStatus.Cancelled:
                        // Backing out is not an error and must not be dressed as one.
                        SetStatus(cancelledMessage);
                        break;
                    case PurchaseStatus.NotReady:
                        SetStatus(notReadyMessage);
                        break;
                    default:
                        SetStatus(string.IsNullOrEmpty(result.Message) ? failedMessage : result.Message);
                        break;
                }

                RefreshRows();
            });
        }

        void Restore()
        {
            if (purchaser == null) return;

            SetStatus(workingMessage);
            purchaser.RestorePurchases(count =>
            {
                SetStatus(count > 0 ? string.Format(restoredFormat, count) : nothingToRestoreMessage);
                RefreshRows();
            });
        }

        void SetRowsInteractable(bool interactable)
        {
            foreach (var row in _rows)
                if (row.View != null && row.View.buyButton != null)
                    row.View.buyButton.interactable = interactable;

            RefreshRewardRows();

            if (interactable) RefreshRows();
        }

        void SetStatus(string message)
        {
            bool has = !string.IsNullOrEmpty(message);

            if (statusLabel != null)
            {
                statusLabel.text = message;
                statusLabel.gameObject.SetActive(has);
            }

            ApplyFooter(has);
        }

        /// <summary>
        /// Gives the status line its band only while there is a status line. The list's bottom
        /// edge is what moves; nothing else does.
        /// </summary>
        // The band is 70 units and the label is showing for a second or two after a purchase,
        // so reserving it permanently put a hand's width of nothing between the last product
        // and the restore button for the whole of the rest of the time.
        //
        // The list's bottom edge is the right thing to move because it is the only edge here
        // with nothing anchored to it: the content is anchored to the top of the scroll rect,
        // so growing the viewport downwards reveals more of the list rather than shifting what
        // is already on screen. Moving the restore button instead would slide a control out
        // from under a finger that is on its way to it.
        void ApplyFooter(bool statusShown)
        {
            if (scrollArea == null) return;

            var offset = scrollArea.offsetMin;
            offset.y = statusShown ? ListBottomWithStatus : ListBottomPlain;
            scrollArea.offsetMin = offset;
        }
    }
}
