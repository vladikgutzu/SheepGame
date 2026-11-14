using System.Collections.Generic;
using System.IO;
using SheepGame.Core;
using SheepGame.Core.Store;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace SheepGame.EditorTools
{
    // The HUD is generated rather than hand-built for the same reason the apron is: the game
    // has been re-themed four times and the canvas was rebuilt by hand every time. Running
    // this replaces the whole canvas from scratch and re-wires GameManager and UIBindings,
    // so a scene that has drifted can always be brought back to the current design.
    //
    // Anything the player is meant to read is left as plain text on the objects, so wording
    // can be edited in the inspector without coming back here.
    public static partial class GameUIBuilder
    {
        const string CanvasName = "UICanvas";
        const string FontDir = "Assets/_Game/UI/Fonts";
        const string FontSource = "Assets/FlatKit/Demos/[Demo] Mugs/Fonts/Oswald-Regular.ttf";

        // Reference resolution: the game is portrait phone first.
        static readonly Vector2 Reference = new Vector2(1080f, 1920f);

        // A hyper-casual HUD is stickers: a solid shape, one dark outline all the way round,
        // a darker lip under it for thickness, and nothing translucent. The old palette was
        // built the other way — dark chips at 82% over the scene — which reads as an
        // overlay, and went muddy over a near-white terminal floor.
        static readonly Color Ink = Hex("23324A");        // outlines and text
        static readonly Color Panel = Color.white;
        static readonly Color Card = Hex("FFFFFF");
        static readonly Color Amber = Hex("FFC635");
        static readonly Color Blue = Hex("34B4FF");
        static readonly Color Coral = Hex("FF6B5A");
        static readonly Color Grape = Hex("9B6BFF");
        static readonly Color Slate = Hex("5B7292");
        static readonly Color Muted = Hex("7E8CA0");

        // How thick the outline is, in UI units, and how far the lip shows below a button.
        const float Outline = 7f;
        const float Lip = 12f;
        // Buttons sit on a darker copy of their own colour rather than a translucent black,
        // so the edge reads the same on the bright board and on the white win card.
        static Color Darken(Color c, float amount = 0.66f) =>
            new Color(c.r * amount, c.g * amount, c.b * amount, 1f);

        [MenuItem("SheepGame/Build Game UI")]
        public static void Build()
        {
            var gm = Object.FindFirstObjectByType<GameManager>(FindObjectsInactive.Include);
            if (gm == null)
            {
                EditorUtility.DisplayDialog("Build Game UI", "No GameManager in the open scene.", "OK");
                return;
            }

            var booster = Object.FindFirstObjectByType<BoosterManager>(FindObjectsInactive.Include);
            var font = EnsureFont();

            // The shop has nothing to sell without a catalog and nothing to sell it with
            // without a purchaser, and the tap budget comes out of the economy asset. A HUD
            // built over none of those is a screen of buttons that quietly do nothing.
            var purchaser = EconomySetup.EnsureEconomy(gm);

            EnsureEventSystem();
            var canvas = EnsureCanvas(gm);
            ClearChildren(canvas.transform);

            var safe = Node("SafeArea", canvas.transform);
            Stretch(safe);
            safe.AddComponent<SafeAreaFitter>();

            var levelText = BuildLevelCard(safe.transform, font, out var progress, out var remainingText,
                                           out var tapsText, out var tapsMeter);

            // The top row holds exactly one button on the left. Measured: the level card
            // starts at 300 and a 128 disc at 96 ends at 160, which leaves 140 units — and
            // anything under 121 units is below 44pt on an iPhone, which is the smallest
            // thing Apple will call a target. So the slot is one button, and it is this one:
            // restart is a choice the player makes, pausing is one the world makes for them.
            // Restart is a tap deeper, on the panel this opens.
            var pauseButton = BuildIconButton(safe.transform, "PauseButton", SkinIcon(UISpriteFactory.IconPause, LayerLabSkin.IconPause), Slate, 128f, 60f, "Pause", font);
            Place(pauseButton.transform.parent.gameObject, new Vector2(0f, 1f), new Vector2(96f, -96f), new Vector2(128f, 128f));

            var coinsButton = BuildCoinPill(safe.transform, font);
            var timerPill = BuildTimerPill(safe.transform, font, gm);

            BuildBoosters(safe.transform, font, out var hintButton, out var shuffle, out var remove,
                          out var armedRing, out var armedHint);

            var win = BuildWinPanel(canvas.transform, font, out var next, out var replay, out var rewardText);
            var lose = BuildLosePanel(canvas.transform, font);
            var shop = BuildShopPanel(canvas.transform, font, purchaser);

            // Built last because it needs the shop, then moved to sit directly above the HUD:
            // a UI child draws over its siblings, so the win, lose and shop panels have to
            // come after the menu or they would open behind it.
            var menu = BuildMainMenu(canvas.transform, font, safe, gm, shop);
            menu.transform.SetSiblingIndex(1);

            // Settings sits above the menu, and privacy above settings. Keeping both as
            // disabled full-screen roots means an invisible modal can never keep a raycast.
            var privacy = BuildPrivacyChoicesPanel(canvas.transform, font);
            var settings = BuildSettingsPanel(canvas.transform, font, privacy);
            privacy.transform.SetSiblingIndex(settings.transform.GetSiblingIndex() + 1);
            menu.settingsPanel = settings;
            menu.privacyPanel = privacy;
            EditorUtility.SetDirty(menu);

            // Under the pause panel: a system interruption during an announcement has to be
            // answered before the announcement.
            var banner = BuildModeBanner(canvas.transform, font);

            // Last, and therefore on top of everything: an interruption from the system has to
            // be answered before whatever it interrupted.
            var pause = BuildPausePanel(canvas.transform, font, gm, menu);

            tapsMeter.gameManager = gm;

            // Wiring. Both components' references are rewritten wholesale — a half-updated
            // canvas pointing at destroyed objects is the failure mode this tool exists to end.
            Undo.RecordObject(gm, "Build Game UI");
            gm.levelLabel = levelText;
            gm.remainingLabel = null;   // the card no longer carries a piece counter
            gm.tapsLabel = tapsText;
            gm.rewardLabel = rewardText;
            gm.continuePriceLabel = lose.ContinuePrice;
            gm.continuePriceIcon = null;
            if (lose.ContinuePrice != null)
            {
                var coin = lose.ContinuePrice.transform.parent.Find("Coin");
                if (coin != null) gm.continuePriceIcon = coin.gameObject;
            }
            gm.shufflePriceLabel = lose.ShufflePrice;
            gm.continueAmountLabel = lose.ContinueLabel;
            gm.shuffleAmountLabel = lose.ShuffleLabel;
            gm.progressBar = progress;
            gm.modeBanner = banner;
            gm.timeLabel = timerPill.label;
            gm.mainMenu = menu;
            gm.winPanel = win;
            gm.losePanel = lose.Root;
            EditorUtility.SetDirty(gm);

            var bindings = canvas.GetComponent<UIBindings>();
            if (bindings == null) bindings = Undo.AddComponent<UIBindings>(canvas.gameObject);
            Undo.RecordObject(bindings, "Build Game UI");
            bindings.gameManager = gm;
            bindings.boosterManager = booster;
            bindings.shopPanel = shop;
            bindings.pausePanel = pause;
            bindings.pauseButton = pauseButton;
            bindings.restartButton = null;
            bindings.shuffleButton = shuffle;
            bindings.removeButton = remove;
            bindings.hintButton = hintButton;
            bindings.coinsButton = coinsButton;
            bindings.nextLevelButton = next;
            bindings.winRestartButton = replay;
            bindings.continueButton = lose.Continue;
            bindings.shuffleRescueButton = lose.Shuffle;
            bindings.loseRestartButton = lose.Replay;
            bindings.loseShopButton = lose.Shop;
            bindings.removeArmedRing = armedRing;
            bindings.removeArmedHint = armedHint;
            EditorUtility.SetDirty(bindings);

            // On the canvas because it is always active: OnApplicationPause and
            // OnApplicationFocus never reach a behaviour that is switched off, and the pause
            // panel is switched off until the moment it is needed.
            var lifecycle = canvas.GetComponent<AppLifecycle>();
            if (lifecycle == null) lifecycle = Undo.AddComponent<AppLifecycle>(canvas.gameObject);
            Undo.RecordObject(lifecycle, "Build Game UI");
            lifecycle.pausePanel = pause;
            lifecycle.mainMenu = menu;
            lifecycle.shopPanel = shop;
            lifecycle.gameManager = gm;
            lifecycle.purchaser = purchaser;
            EditorUtility.SetDirty(lifecycle);

            // The cart, the queue, the hand and the unlock gates, built the same way the
            // migration builds them so a full rebuild keeps them.
            WireColourMatch(gm, bindings, canvas.transform, safe.transform, font);

            SkinTextPass(canvas.transform);

            EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
            Selection.activeGameObject = canvas.gameObject;
            Debug.Log("Built the game UI on " + canvas.name + ".");
        }

        /// <summary>
        /// Rebuilds every player-facing canvas with the GUI Pro-CasualGame skin. This is also a
        /// command-line entry point, so the checked-in scenes can never drift away from the builder.
        /// </summary>
        [MenuItem("SheepGame/Rebuild All GUI Pro UI")]
        public static void RebuildAllGuiProUi()
        {
            if (!LayerLabSkin.Available)
            {
                Debug.LogError("GUI Pro-CasualGame is missing; UI rebuild cancelled.");
                return;
            }

            var gameScene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
            Build();
            EditorSceneManager.SaveScene(gameScene);

            BuildBootSceneNow();
            AssetDatabase.SaveAssets();

            // Leave the project in the scene people actually work in, not in the loading screen.
            EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
            Debug.Log("Rebuilt all SheepGame UI with GUI Pro-CasualGame assets.");
        }

        // Build Game UI clears the canvas and draws it again, which is right when the layout
        // has moved and wrong when one button has been added to it: the HUD in the scene has
        // been tuned by hand since it was last generated. This puts the pause button into an
        // existing HUD and touches nothing else.
        // The Daily Run's way in, for a scene built before the mode existed. Same shape as the
        // pause-button migration: find what is there, add only what is missing, wire it, and
        // leave everything else alone.
        [MenuItem("SheepGame/Add Daily Button")]
        public static void AddDailyButton()
        {
            var menu = Object.FindFirstObjectByType<MainMenuPanel>(FindObjectsInactive.Include);
            if (menu == null)
            {
                Debug.LogWarning("Add Daily Button: no MainMenuPanel in the open scene.");
                return;
            }

            // The menu's own safe area, the one Play and Shop sit in -- not the HUD's, which
            // belongs to a different overlay and is switched on when this one is switched off.
            Transform safe = menu.playButton != null
                ? menu.playButton.transform.parent.parent
                : menu.transform;

            var existing = safe.Find("DailyButtonSlot");
            if (existing == null)
            {
                var font = EnsureFont();
                var button = BuildPillButton(safe, "DailyButton", "Daily Run", font,
                                             Hex("5DB3B2"), Ink, new Vector2(360f, 130f), out _);
                Place(button.transform.parent.gameObject, new Vector2(0.5f, 0f),
                      new Vector2(0f, 260f), new Vector2(360f, 130f));

                Undo.RecordObject(menu, "Add Daily Button");
                menu.dailyButton = button;
            }
            else if (menu.dailyButton == null)
            {
                Undo.RecordObject(menu, "Add Daily Button");
                menu.dailyButton = existing.GetComponentInChildren<Button>(true);
            }

            EditorUtility.SetDirty(menu);
            EditorSceneManager.MarkSceneDirty(menu.gameObject.scene);
            Debug.Log("Add Daily Button: the menu now offers today's board. Save the scene to keep it.");
        }

        // A way past a level without playing it, for looking at the one after it. Built by
        // its own menu item rather than as part of Build Game UI: the HUD that ships has no
        // such control, and this one turns itself off outside the editor and development
        // builds. Running this on a HUD that already has it only re-wires the binding.
        [MenuItem("SheepGame/Add Skip Level Button")]
        public static void AddSkipLevelButton()
        {
            var bindings = Object.FindFirstObjectByType<UIBindings>(FindObjectsInactive.Include);
            if (bindings == null)
            {
                EditorUtility.DisplayDialog("Add Skip Level Button", "No UIBindings in the open scene.", "OK");
                return;
            }

            var canvas = bindings.GetComponentInParent<Canvas>();
            if (canvas == null) canvas = Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);

            // The HUD's own safe area, as the pause button does: an overlay's is a child of
            // a panel that is switched off, and a button parented into one goes with it.
            Transform safe = null;
            foreach (Transform child in canvas.transform)
                if (child.GetComponent<SafeAreaFitter>() != null) { safe = child; break; }
            if (safe == null) safe = canvas.transform;

            var existing = safe.Find("SkipLevelButtonSlot");
            if (existing == null)
            {
                var font = EnsureFont();

                // Grey rather than one of the HUD's colours, and under the coin pill on the
                // right rather than in the booster row: it should not read as one of the
                // three things the player is meant to press.
                var button = BuildIconButton(safe, "SkipLevelButton", SkinIcon(UISpriteFactory.IconCheck, LayerLabSkin.IconCheck),
                                             Muted, 128f, 60f, "Skip", font);
                Place(button.transform.parent.gameObject, new Vector2(1f, 1f),
                      new Vector2(-96f, -280f), new Vector2(128f, 128f));

                Undo.RecordObject(bindings, "Add Skip Level Button");
                bindings.skipLevelButton = button;
            }
            else if (bindings.skipLevelButton == null)
            {
                Undo.RecordObject(bindings, "Add Skip Level Button");
                bindings.skipLevelButton = existing.GetComponentInChildren<Button>(true);
            }

            EditorUtility.SetDirty(bindings);
            EditorSceneManager.MarkSceneDirty(bindings.gameObject.scene);
            Debug.Log("Add Skip Level Button: the HUD now carries a skip. Save the scene to keep it.");
        }

        [MenuItem("SheepGame/Add Pause Button")]
        public static void AddPauseButton()
        {
            var bindings = Object.FindFirstObjectByType<UIBindings>(FindObjectsInactive.Include);
            if (bindings == null)
            {
                EditorUtility.DisplayDialog("Add Pause Button", "No UIBindings in the open scene.", "OK");
                return;
            }

            var canvas = bindings.GetComponentInParent<Canvas>();
            if (canvas == null) canvas = Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);

            // The HUD's own safe area, not one of the overlays': those are children of panels
            // that are switched off, and a button parented into one would vanish with it.
            Transform safe = null;
            foreach (Transform child in canvas.transform)
                if (child.GetComponent<SafeAreaFitter>() != null) { safe = child; break; }
            if (safe == null) safe = canvas.transform;

            var pausePanel = Object.FindFirstObjectByType<PausePanel>(FindObjectsInactive.Include);

            var existing = safe.Find("PauseButtonSlot");
            if (existing == null)
            {
                var font = EnsureFont();
                var button = BuildIconButton(safe, "PauseButton", SkinIcon(UISpriteFactory.IconPause, LayerLabSkin.IconPause),
                                             Slate, 128f, 60f, "Pause", font);
                Place(button.transform.parent.gameObject, new Vector2(0f, 1f),
                      new Vector2(96f, -96f), new Vector2(128f, 128f));

                Undo.RecordObject(bindings, "Add Pause Button");
                bindings.pauseButton = button;
            }

            Undo.RecordObject(bindings, "Add Pause Button");
            if (pausePanel != null) bindings.pausePanel = pausePanel;

            // The slot the pause button has taken. Restart is on the pause panel, on the win
            // panel and on the lose panel, so nothing is lost by it leaving the top row —
            // and leaving it there would put two buttons in a space measured for one.
            var oldRestart = safe.Find("RestartButtonSlot");
            if (oldRestart != null)
            {
                bindings.restartButton = null;
                Undo.DestroyObjectImmediate(oldRestart.gameObject);
            }

            EditorUtility.SetDirty(bindings);
            EditorSceneManager.MarkSceneDirty(bindings.gameObject.scene);

            Debug.Log(existing != null
                ? "[HUD] Pause button was already there; rewired it."
                : "[HUD] Added the pause button and moved restart onto the pause panel.");
        }

        // Brings a shop that already exists up to the current layout. Additive on purpose:
        // Build Game UI would draw the whole panel again and lose whatever has been tuned in
        // the scene since. Everything here is idempotent, so running it twice is harmless.
        [MenuItem("SheepGame/Fix Shop Layout")]
        public static void FixShopLayout()
        {
            // Play mode throws the scene away when it stops, so a layout fixed there is a
            // layout fixed for as long as the player is looking at it and no longer. Worth
            // saying out loud rather than letting MarkSceneDirty throw halfway through.
            //
            // Logged rather than shown in a dialog. This one is called from a script as often
            // as from the menu, and a modal window in an editor being driven remotely stops
            // everything until somebody walks over and clicks it.
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[Shop] Stop play mode first — changes made while playing are " +
                                 "discarded when it ends. Nothing was changed.");
                return;
            }

            var shop = Object.FindFirstObjectByType<ShopPanel>(FindObjectsInactive.Include);
            if (shop == null || shop.rowTemplate == null)
            {
                Debug.LogWarning("[Shop] No ShopPanel with a row template in the open scene.");
                return;
            }

            var report = new System.Text.StringBuilder();

            // ---- the drag catcher ---------------------------------------------------------
            var scroller = shop.listParent != null
                ? shop.listParent.GetComponentInParent<ScrollRect>(true) : null;
            var viewport = scroller != null ? scroller.viewport : null;

            if (viewport == null) report.Append("no ScrollRect viewport found; ");
            else if (viewport.GetComponent<Graphic>() != null) report.Append("viewport already catches drags; ");
            else
            {
                var catcher = Undo.AddComponent<Image>(viewport.gameObject);
                catcher.color = new Color(0f, 0f, 0f, 0f);
                catcher.raycastTarget = true;
                report.Append("added the invisible drag catcher to the viewport; ");
            }

            // ---- the header ---------------------------------------------------------------
            var cardFill = viewport != null ? viewport.parent as RectTransform : null;

            // Found by name, which is what a one-off migration is for: the panel holds no
            // reference to its own title or balance pill, and adding two fields to carry them
            // across a single move would outlive the move by the life of the project.
            var titleRect = cardFill != null ? cardFill.Find("Title") as RectTransform : null;
            var balanceRect = cardFill != null ? cardFill.Find("Balance") as RectTransform : null;

            if (titleRect != null)
            {
                Undo.RecordObject(titleRect, "Fix Shop Layout");
                titleRect.anchorMin = new Vector2(0.5f, 1f);
                titleRect.anchorMax = new Vector2(0.5f, 1f);
                titleRect.anchoredPosition = new Vector2(0f, ShopHeaderY);
                titleRect.sizeDelta = new Vector2(ShopTitleWidth, titleRect.sizeDelta.y);

                var titleText = titleRect.GetComponent<TMP_Text>();
                if (titleText != null)
                {
                    Undo.RecordObject(titleText, "Fix Shop Layout");
                    titleText.alignment = TextAlignmentOptions.Center;
                    titleText.enableAutoSizing = true;
                    titleText.fontSizeMin = 40f;
                    titleText.fontSizeMax = ShopTitleSize;
                }
            }
            else report.Append("TITLE NOT FOUND; ");

            if (balanceRect != null)
            {
                Undo.RecordObject(balanceRect, "Fix Shop Layout");
                balanceRect.anchorMin = new Vector2(0f, 1f);
                balanceRect.anchorMax = new Vector2(0f, 1f);
                balanceRect.anchoredPosition = new Vector2(ShopBalanceX, ShopHeaderY);
            }
            else report.Append("BALANCE NOT FOUND; ");

            if (shop.closeButton != null)
            {
                var slot = (RectTransform)shop.closeButton.transform.parent;
                Undo.RecordObject(slot, "Fix Shop Layout");
                slot.anchoredPosition = new Vector2(-ShopCloseX, ShopHeaderY);
            }

            if (viewport != null)
            {
                Undo.RecordObject(viewport, "Fix Shop Layout");
                viewport.offsetMax = new Vector2(viewport.offsetMax.x, ShopListTop);
                report.Append("header on one row; ");
            }

            // ---- the footer ---------------------------------------------------------------

            if (shop.scrollArea == null && viewport != null)
            {
                Undo.RecordObject(shop, "Fix Shop Layout");
                shop.scrollArea = viewport;
            }

            if (viewport != null)
            {
                Undo.RecordObject(viewport, "Fix Shop Layout");
                // The plain figure, because that is what the panel opens on. ShopPanel moves
                // this edge itself the moment there is a status line to make room for.
                viewport.offsetMin = new Vector2(viewport.offsetMin.x, ShopPanel.ListBottomPlain);
            }

            if (shop.restoreButton != null)
            {
                var slot = (RectTransform)shop.restoreButton.transform.parent;
                Undo.RecordObject(slot, "Fix Shop Layout");
                slot.anchoredPosition = new Vector2(0f, ShopPanel.RestoreCentre);
                report.Append("restore centred and the footer tightened; ");
            }

            if (shop.statusLabel != null)
            {
                var statusRect = shop.statusLabel.rectTransform;
                Undo.RecordObject(statusRect, "Fix Shop Layout");
                statusRect.anchoredPosition = new Vector2(0f, ShopPanel.StatusCentre);
                statusRect.sizeDelta = new Vector2(statusRect.sizeDelta.x, ShopPanel.StatusHeight);
            }

            if (cardFill == null) report.Append("card fill not found; ");

            // ---- the row template ---------------------------------------------------------
            var view = shop.rowTemplate;
            Undo.RecordObject(view, "Fix Shop Layout");

            var fill = view.title != null ? view.title.transform.parent : null;

            if (view.coinIcon == null && fill != null)
                view.coinIcon = fill.Find("Coin") as RectTransform;
            if (view.buySlot == null && view.buyButton != null)
                view.buySlot = view.buyButton.transform.parent as RectTransform;

            if (view.coinIcon == null) report.Append("COIN ICON NOT FOUND - wire it by hand; ");
            if (view.buySlot == null) report.Append("BUY SLOT NOT FOUND - wire it by hand; ");

            // Every part it is about to move, plus the row itself.
            foreach (var t in view.GetComponentsInChildren<RectTransform>(true))
                Undo.RecordObject(t, "Fix Shop Layout");
            var element = view.GetComponent<LayoutElement>();
            if (element != null) Undo.RecordObject(element, "Fix Shop Layout");

            if (view.badge != null)
            {
                Undo.RecordObject((RectTransform)view.badge.transform, "Fix Shop Layout");
                var badgeRect = (RectTransform)view.badge.transform;
                badgeRect.anchorMin = new Vector2(0f, 1f);
                badgeRect.anchorMax = new Vector2(0f, 1f);
                badgeRect.sizeDelta = new Vector2(badgeRect.sizeDelta.x, ShopRowView.BadgeHeight);
                badgeRect.anchoredPosition = new Vector2(
                    badgeRect.anchoredPosition.x,
                    -(ShopRowView.BadgeTopGap + ShopRowView.BadgeHeight * 0.5f - ShopRowView.Outline));
            }

            // The template is stored with no badge, so it is stored at the plain height. Each
            // clone is laid out again by ShopPanel once it knows its product.
            view.ApplyLayout(badged: false);
            report.Append($"template laid out plain at {ShopRowView.PlainHeight}, badged rows will be {ShopRowView.BadgedHeight}.");

            EditorUtility.SetDirty(view);
            EditorSceneManager.MarkSceneDirty(view.gameObject.scene);

            Debug.Log("[Shop] " + report);
        }

        // The two readouts that were measured wrong rather than designed wrong, brought into
        // line in a HUD that already exists. Additive, like every other migration here.
        [MenuItem("SheepGame/Fix Level Card And Reward")]
        public static void FixLevelCardAndReward()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[HUD] Stop play mode first -- changes made while playing are " +
                                 "discarded when it ends. Nothing was changed.");
                return;
            }

            var report = new System.Text.StringBuilder();

            // ---- the taps readout ----------------------------------------------------------
            var meter = Object.FindFirstObjectByType<UITapsMeter>(FindObjectsInactive.Include);
            if (meter == null) report.Append("no UITapsMeter; ");
            else
            {
                var caption = meter.transform.Find("TapsCaption");
                if (caption == null) report.Append("TAPS CAPTION NOT FOUND; ");
                else
                {
                    var rect = (RectTransform)caption;
                    Undo.RecordObject(rect, "Fix Level Card And Reward");
                    rect.pivot = new Vector2(1f, 0.5f);

                    Undo.RecordObject(meter, "Fix Level Card And Reward");
                    meter.caption = caption.GetComponent<TMP_Text>();
                    caption.gameObject.SetActive(true);
                    report.Append("taps caption pivoted to its right edge and wired to the meter; ");
                }
            }

            // ---- the piece counter, removed --------------------------------------------------
            if (meter != null)
            {
                var line = meter.transform.parent;                    // the card's Fill
                var pieces = line != null ? line.Find("RemainingText") : null;
                if (pieces != null)
                {
                    Undo.DestroyObjectImmediate(pieces.gameObject);
                    report.Append("piece counter removed; ");
                }

                var tapsRect = (RectTransform)meter.transform;
                Undo.RecordObject(tapsRect, "Fix Level Card And Reward");
                tapsRect.anchoredPosition = new Vector2(0f, tapsRect.anchoredPosition.y);
                CentreRow(meter.gameObject, spacing: 12f);
                report.Append("taps centred, group and contents; ");
            }

            // ---- the reward pill -----------------------------------------------------------
            var gm = Object.FindFirstObjectByType<GameManager>(FindObjectsInactive.Include);
            var rewardLabel = gm != null ? gm.rewardLabel : null;
            if (gm != null && gm.remainingLabel != null)
            {
                Undo.RecordObject(gm, "Fix Level Card And Reward");
                gm.remainingLabel = null;
                EditorUtility.SetDirty(gm);
            }

            if (rewardLabel == null) report.Append("no GameManager.rewardLabel; ");
            else
            {
                var fill = rewardLabel.transform.parent;
                var coin = fill != null ? fill.Find("Coin") : null;

                var rewardRect = rewardLabel.rectTransform;
                Undo.RecordObject(rewardRect, "Fix Level Card And Reward");
                rewardRect.anchoredPosition = new Vector2(0f, rewardRect.anchoredPosition.y);

                if (coin != null)
                {
                    var coinRect = (RectTransform)coin;
                    Undo.RecordObject(coinRect, "Fix Level Card And Reward");
                    coinRect.anchoredPosition = new Vector2(0f, coinRect.anchoredPosition.y);
                    AddFixedSize(coin, coinRect.sizeDelta.x, coinRect.sizeDelta.y);
                }
                else report.Append("REWARD COIN NOT FOUND; ");

                CentreRow(fill.gameObject, spacing: 12f);
                report.Append("reward pill now centres its own contents.");
            }

            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log("[HUD] " + report);
        }

        // Adds the Time Rush announcement to a HUD that already exists, and wires it. Like the
        // other migrations here it builds one thing and leaves the rest of the canvas alone.
        [MenuItem("SheepGame/Add Time Rush Banner")]
        public static void AddTimeRushBanner()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[HUD] Stop play mode first -- changes made while playing are " +
                                 "discarded when it ends. Nothing was changed.");
                return;
            }

            var gm = Object.FindFirstObjectByType<GameManager>(FindObjectsInactive.Include);
            if (gm == null)
            {
                Debug.LogWarning("[HUD] No GameManager in the open scene.");
                return;
            }

            var bindings = Object.FindFirstObjectByType<UIBindings>(FindObjectsInactive.Include);
            var canvas = bindings != null ? bindings.GetComponent<Canvas>()
                                          : Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
            if (canvas == null)
            {
                Debug.LogWarning("[HUD] No canvas to build on.");
                return;
            }

            var existing = canvas.transform.Find("ModeBanner");
            UIModeBanner banner = existing != null ? existing.GetComponent<UIModeBanner>() : null;

            if (banner == null)
            {
                banner = BuildModeBanner(canvas.transform, EnsureFont());

                // Directly under the pause panel, so an interruption is answered first.
                var pause = Object.FindFirstObjectByType<PausePanel>(FindObjectsInactive.Include);
                if (pause != null && pause.transform.parent == canvas.transform)
                    banner.transform.SetSiblingIndex(pause.transform.GetSiblingIndex());
            }

            Undo.RecordObject(gm, "Add Time Rush Banner");
            gm.modeBanner = banner;

            // Without this the announcement plays over the main menu, and the clock runs
            // behind it: the level is dealt before Play is ever pressed.
            var menuPanel = Object.FindFirstObjectByType<MainMenuPanel>(FindObjectsInactive.Include);
            if (menuPanel != null) gm.mainMenu = menuPanel;
            else Debug.LogWarning("[HUD] No MainMenuPanel found; GameManager.mainMenu left empty.");

            // The clock's own chip, beside the level card rather than inside it. Built into
            // the HUD's safe area -- the one directly under the canvas, not an overlay's.
            Transform hud = null;
            foreach (Transform child in canvas.transform)
                if (child.GetComponent<SafeAreaFitter>() != null) { hud = child; break; }

            if (hud == null) Debug.LogWarning("[HUD] No safe area under the canvas; timer pill skipped.");
            else
            {
                // Rebuilt rather than patched: it is generated, carries no hand-tuning, and an
                // earlier build of it had the switching component on the object it switched --
                // which cannot work, because Unity never wakes a disabled object to let it
                // enable itself. Replacing the whole chip is the honest way to correct that.
                var slot = hud.Find("TimerPillSlot");
                if (slot != null) Undo.DestroyObjectImmediate(slot.gameObject);

                var pill = BuildTimerPill(hud, EnsureFont(), gm);

                Undo.RecordObject(pill, "Add Time Rush Banner");
                pill.gameManager = gm;
                gm.timeLabel = pill.label;
                EditorUtility.SetDirty(pill);
            }

            EditorUtility.SetDirty(gm);

            // The meter now listens for the mode as well as the taps, so it needs the manager
            // it used to get away without.
            var meter = Object.FindFirstObjectByType<UITapsMeter>(FindObjectsInactive.Include);
            if (meter != null && meter.gameManager == null)
            {
                Undo.RecordObject(meter, "Add Time Rush Banner");
                meter.gameManager = gm;
                EditorUtility.SetDirty(meter);
            }

            EditorSceneManager.MarkSceneDirty(gm.gameObject.scene);
            Debug.Log(existing != null
                ? "[HUD] Time Rush banner was already there; rewired it."
                : "[HUD] Added the Time Rush banner and wired it to the GameManager.");
        }

        // ---- boot scene ---------------------------------------------------------------

        const string BootScenePath = "Assets/Scenes/Boot.unity";
        const string BootArtPath = "Assets/_Game/BaggageJam.png";
        const string GameScenePath = "Assets/Scenes/TerminalSandbox.unity";

        [MenuItem("SheepGame/Android/Install Early Ads Bootstrap")]
        public static void InstallEarlyAdsBootstrap()
        {
            var scene = SceneManager.GetSceneByPath(BootScenePath);
            bool openedHere = !scene.IsValid() || !scene.isLoaded;
            if (openedHere) scene = EditorSceneManager.OpenScene(BootScenePath, OpenSceneMode.Additive);

            AddEarlyAdsBootstrap(scene);
            EditorSceneManager.SaveScene(scene);
            if (openedHere) EditorSceneManager.CloseScene(scene, removeScene: true);
            Debug.Log("[Ads] Installed the early LevelPlay bootstrap in Boot.unity.");
        }

        static void AddEarlyAdsBootstrap(Scene scene)
        {
            AdsManager ads = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name != "AdsBootstrap") continue;
                ads = root.GetComponent<AdsManager>();
                if (ads == null) ads = Undo.AddComponent<AdsManager>(root);
                break;
            }

            if (ads == null)
            {
                var go = new GameObject("AdsBootstrap", typeof(AdsManager));
                EditorSceneManager.MoveGameObjectToScene(go, scene);
                ads = go.GetComponent<AdsManager>();
            }

            var config = AssetDatabase.LoadAssetAtPath<AdsConfig>(AdsConfigPath);
            if (config == null) throw new InvalidDataException("AdsConfig is missing: " + AdsConfigPath);
            ads.config = config;
            EditorUtility.SetDirty(ads);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        // The loading screen gets a scene of its own because the game scene is the thing being
        // waited for: a generated hall, the belt dressing, the effects and the HUD. Anything
        // that shows progress has to already be on screen while that loads, which means it
        // cannot live in the scene it is loading.
        [MenuItem("SheepGame/Build Boot Scene")]
        public static void BuildBootScene()
        {
            if (System.IO.File.Exists(BootScenePath) &&
                !EditorUtility.DisplayDialog("Build Boot Scene",
                    BootScenePath + " already exists and will be rebuilt from scratch.\n\n" +
                    "Anything added to it by hand will be lost.", "Rebuild", "Cancel"))
                return;

            BuildBootSceneNow();
        }

        // The loading screen's picture. Scaled to cover rather than to fit: phones run from
        // 0.46 to 0.75 wide-over-tall and the art is one shape, so something has to give --
        // and a strip of empty colour down the side is worse than losing a little of the edge.
        // The prompt the art was generated from keeps its important content away from the
        // edges for exactly this reason.
        static void AddBootBackground(Transform parent)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(BootArtPath);
            if (sprite == null)
            {
                Debug.LogWarning($"[Boot] No sprite at {BootArtPath} -- the loading screen will " +
                                 "be the camera's clear colour. Check the texture is imported " +
                                 "as a Sprite.");
                return;
            }

            var go = Node("Background", parent);
            // Centred rather than stretched: with EnvelopeParent the fitter drives both the
            // width and the height, and a stretched rect would be fighting it every frame.
            Place(go, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(100f, 100f));

            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            image.preserveAspect = false;

            var fitter = go.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = sprite.rect.width / Mathf.Max(1f, sprite.rect.height);

            go.transform.SetAsFirstSibling();
        }

        // A full-screen background wants none of a texture importer's defaults: mipmaps it
        // will never use, and a compression that bands a flat sky.
        static void ImportBootArt()
        {
            var importer = AssetImporter.GetAtPath(BootArtPath) as TextureImporter;
            if (importer == null) return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }

        /// <summary>Builds the boot scene without asking anything. Menu use goes through the confirm.</summary>
        // Built into an additive scene rather than by opening a new single one. The first
        // attempt did the latter, which meant closing whatever was open, which meant asking
        // whether to save it — a modal prompt that hung the editor when this was run from
        // tooling rather than from the menu. Additive touches nothing that is already open.
        public static void BuildBootSceneNow()
        {
            ImportBootArt();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

            var font = EnsureFont();

            var cameraGO = new GameObject("Camera", typeof(Camera));
            var camera = cameraGO.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            // The terminal's own floor colour, so the hand-over to the game is not a flash of
            // one colour into another.
            camera.backgroundColor = Hex("F2F5F9");
            camera.cullingMask = 0;              // nothing 3D to draw; the UI is an overlay
            cameraGO.AddComponent<AudioListener>();

            // New objects land in the active scene, which an additive NewScene does not become,
            // so the two roots are moved across by hand.
            EditorSceneManager.MoveGameObjectToScene(cameraGO, scene);

            // Start LevelPlay while the loading screen is visible. By the time the player can
            // open the shop, all three rewarded units have already had their first LoadAd.
            AddEarlyAdsBootstrap(scene);

            var canvasGO = new GameObject("BootCanvas", typeof(RectTransform), typeof(Canvas),
                                          typeof(CanvasScaler), typeof(GraphicRaycaster));
            EditorSceneManager.MoveGameObjectToScene(canvasGO, scene);
            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = Reference;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0f;
            canvasGO.AddComponent<CanvasScalerFitter>();

            // Behind everything, and outside the safe area on purpose: a background that
            // stopped at the notch would show the camera's clear colour in the strip above it.
            AddBootBackground(canvasGO.transform);

            var safe = Node("SafeArea", canvasGO.transform);
            Stretch(safe);
            safe.AddComponent<SafeAreaFitter>();

            // No title text. The artwork carries the wordmark already, and a second one in
            // TMP over it would be the game's name written twice in two different typefaces.

            // Down on the pale floor at the foot of the picture, below the carousel entirely.
            // At 72% it landed across the dark belt: a dark track on a dark belt, with the
            // percentage sitting on it unreadable. The art has one clear area and this is it.
            var barSize = new Vector2(760f, 84f);
            // Pale interior, not dark. AddSticker draws the Ink outline for us, and that
            // outline is what separates the bar from the floor -- so the inside is free to be
            // light, which is what lets one dark percentage read against both the empty track
            // and the amber fill without changing colour halfway across.
            // A warm neutral, the terminal's own floor. It was pale teal, which was a mistake:
            // the fill ramp *starts* in teal, so the first third of the bar filled almost
            // invisibly and the colour only appeared once it reached the amber -- which reads
            // as a bar that stretches rather than fills. A track has to differ from every
            // colour that will be laid over it, not just from some of them.
            //
            // Light on purpose, and framed by a thick dark outline: the interior stays pale so
            // one dark percentage reads against the empty track and all three ramp colours,
            // and the outline is what separates the bar from the floor behind it.
            var bar = BuildProgressBar(safe.transform, barSize, Hex("EDE3D2"), Amber, outline: 11f);
            Place(bar.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0f, -700f), barSize);

            var barFill = bar.fill.GetComponent<Image>();
            if (barFill != null)
            {
                // The fill draws nothing of its own. It is a rectangular window, and what
                // shows through it is one unchanging picture of the finished bar.
                //
                // It used to be a rounded pill that grew, which is what made the filling look
                // wrong: a pill has a round cap at *both* ends, so the leading edge was always
                // a semicircle -- a blob at the start and a taper at 95%. A progress bar's
                // leading edge is a straight cut; only the far end is round, and that roundness
                // belongs to the picture underneath, not to the window.
                barFill.enabled = false;

                var window = Undo.AddComponent<RectMask2D>(bar.fill.gameObject);
                window.softness = Vector2Int.zero;

                // As wide as the track's whole interior and anchored to its left edge, so the
                // window uncovers more of it rather than stretching a small one. Its own round
                // caps line up with the track's, and the right one only appears at the end.
                var art = Node("FillArt", bar.fill);
                var artRect = RectOf(art);
                artRect.anchorMin = new Vector2(0f, 0f);
                artRect.anchorMax = new Vector2(0f, 1f);
                artRect.pivot = new Vector2(0f, 0.5f);
                artRect.anchoredPosition = Vector2.zero;
                artRect.sizeDelta = new Vector2(barSize.x - 22f, 0f);   // the track less its outline

                var artImage = art.AddComponent<Image>();
                artImage.sprite = UISpriteFactory.ProgressPill;
                artImage.type = Image.Type.Simple;
                artImage.raycastTarget = false;

                // The sweep the buttons use, clipped by the same window. Built here rather than
                // through AddShine because that one installs a stencil Mask, and the window
                // above already does the clipping.
                var shine = Node("Shine", art.transform);
                var shineRect = RectOf(shine);
                shineRect.anchorMin = new Vector2(0.5f, 0f);
                shineRect.anchorMax = new Vector2(0.5f, 1f);
                shineRect.pivot = new Vector2(0.5f, 0.5f);
                shineRect.sizeDelta = new Vector2(90f, 90f);
                shineRect.localRotation = Quaternion.Euler(0f, 0f, 18f);

                var shineImage = shine.AddComponent<Image>();
                shineImage.color = new Color(1f, 1f, 1f, 0.26f);
                shineImage.raycastTarget = false;
                shine.AddComponent<UIShine>();
            }

            // Inside the bar, not under it. Ink on a pale track and Ink on amber both read;
            // the outline keeps the whole thing off the floor behind it.
            var percent = AddText(bar.transform, "Percent", "0%", font, 46f, Ink);
            percent.fontStyle = FontStyles.Bold;
            percent.alignment = TextAlignmentOptions.Center;
            Place(percent.gameObject, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300f, 60f));
            // Above the fill, which is drawn into the track's own Fill child.
            percent.transform.SetAsLastSibling();

            var loader = canvasGO.AddComponent<BootLoader>();
            loader.gameSceneName = System.IO.Path.GetFileNameWithoutExtension(GameScenePath);
            loader.progressBar = bar;
            loader.percentLabel = percent;

            EditorSceneManager.SaveScene(scene, BootScenePath);
            // Closed and removed again: it was only ever open in order to be written.
            EditorSceneManager.CloseScene(scene, removeScene: true);
            SetBuildOrder();

            Debug.Log($"Built {BootScenePath} and made it the first scene in Build Settings.");
        }

        // A boot scene that is not first in the build order is a boot scene the build never
        // opens, and that failure is invisible in the editor.
        static void SetBuildOrder()
        {
            var wanted = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(BootScenePath, true),
                new EditorBuildSettingsScene(GameScenePath, true)
            };

            // Anything else already in the list is kept, after those two and untouched.
            foreach (var existing in EditorBuildSettings.scenes)
            {
                if (existing.path == BootScenePath || existing.path == GameScenePath) continue;
                wanted.Add(existing);
            }

            EditorBuildSettings.scenes = wanted.ToArray();
        }

        [MenuItem("SheepGame/Rebuild UI Sprites")]
        public static void RebuildSprites()
        {
            UISpriteFactory.Clear();
            AssetDatabase.Refresh();
            Build();
        }

        // ---- pieces -----------------------------------------------------------------

        static TMP_Text BuildLevelCard(Transform parent, TMP_FontAsset font,
                                       out UIProgressBar progress, out TMP_Text remaining,
                                       out TMP_Text taps, out UITapsMeter tapsMeter)
        {
            // Narrowed from 600 to leave the top-right corner to the coin pill. 520 left only
            // ten units between the two, measured — near enough to touching that they read as
            // one shape. 480 against a 240 pill puts 30 between them, and the pill needs the
            // 240 to hold a coin, a five-character balance and a plus without any two touching.
            const float width = 480f;

            var card = Node("LevelCard", parent);
            Place(card, new Vector2(0.5f, 1f), new Vector2(0f, -44f), new Vector2(width, 200f));
            RectOf(card).pivot = new Vector2(0.5f, 1f);
            var cardFill = AddSticker(card, UISpriteFactory.Round, Card, 46f);

            var title = AddText(cardFill.transform, "LevelText", "LEVEL 1", font, 58f, Ink);
            title.fontStyle = FontStyles.UpperCase | FontStyles.Bold;
            title.characterSpacing = 6f;
            Place(title.gameObject, new Vector2(0.5f, 1f), new Vector2(0f, -16f), new Vector2(width - 60f, 78f));
            RectOf(title.gameObject).pivot = new Vector2(0.5f, 1f);

            progress = BuildProgressBar(cardFill.transform, new Vector2(396f, 36f), Hex("E4EAF2"), Amber);
            Place(progress.gameObject, new Vector2(0.5f, 0f), new Vector2(0f, 46f), new Vector2(396f, 36f));
            RectOf(progress.gameObject).pivot = new Vector2(0.5f, 0f);

            // The piece counter used to sit on the left of this line, and it is gone. Two bare
            // numbers side by side -- "30 left" and "47 TAPS" -- made the player work out which
            // was which, and the progress bar directly above already says how much of the board
            // is done. Taps have no other representation, so taps are what the line keeps.
            remaining = null;

            var tapsGroup = Node("Taps", cardFill.transform);
            Place(tapsGroup, new Vector2(0.5f, 0f), new Vector2(0f, 8f), new Vector2(206f, 38f));
            RectOf(tapsGroup).pivot = new Vector2(0.5f, 0f);

            taps = AddText(tapsGroup.transform, "TapsText", "0", font, 30f, Slate);
            taps.fontStyle = FontStyles.Bold;
            taps.alignment = TextAlignmentOptions.Right;
            Place(taps.gameObject, new Vector2(0.5f, 0.5f), new Vector2(-52f, 0f), new Vector2(110f, 38f));

            var tapsCaption = AddText(tapsGroup.transform, "TapsCaption", "TAPS", font, 26f, Muted);
            tapsCaption.fontStyle = FontStyles.UpperCase | FontStyles.Bold;
            tapsCaption.characterSpacing = 4f;
            tapsCaption.alignment = TextAlignmentOptions.Right;
            // Pivoted to its own right edge, so the -6 means "six in from the group's right
            // edge" rather than "centre six in from it". With the default centre pivot a
            // 96-wide box hung 42 units past the group and ran off the card: measured
            // 391..487 against a card that ends at 473, which is the clipped word on screen.
            Place(tapsCaption.gameObject, new Vector2(1f, 0.5f), new Vector2(-6f, 0f), new Vector2(96f, 34f));
            RectOf(tapsCaption.gameObject).pivot = new Vector2(1f, 0.5f);

            // Same trap as the reward pill: centring the group is not centring what is in it.
            // The number sits right-aligned in a 110-wide box, so a one-digit count left most
            // of that box empty and pushed the visible pair 41 units right of the card's axis.
            // Measured, not guessed -- it looks fine in the editor at three digits.
            CentreRow(tapsGroup, spacing: 12f);

            tapsMeter = tapsGroup.AddComponent<UITapsMeter>();
            tapsMeter.label = taps;
            tapsMeter.caption = tapsCaption;
            tapsMeter.popTarget = RectOf(tapsGroup);
            tapsMeter.normalColour = Slate;
            tapsMeter.warningColour = Coral;

            return title;
        }

        // The coin balance doubles as the way into the shop, which is why it is a button and
        // carries a plus. A separate shop button somewhere else would be a second thing to
        // find on a screen that has no room for one.
        static Button BuildCoinPill(Transform parent, TMP_FontAsset font)
        {
            // Laid out as three fixed things with 12 between them: coin, balance, plus. The
            // balance is the only one that changes width, which is why UIWalletLabel shortens
            // anything past four digits rather than letting it grow into the plus.
            var size = new Vector2(240f, 92f);

            var slot = Node("CoinPillSlot", parent);
            Place(slot, new Vector2(1f, 1f), new Vector2(-150f, -96f), size);

            var lip = Node("Lip", slot.transform);
            Place(lip, new Vector2(0.5f, 0.5f), new Vector2(0f, -Lip), size);
            AddPanel(lip, UISpriteFactory.Round, Ink, size.y * 0.5f).raycastTarget = false;

            var go = Node("CoinPill", slot.transform);
            Place(go, new Vector2(0.5f, 0.5f), Vector2.zero, size);
            var body = AddSticker(go, UISpriteFactory.Round, Card, size.y * 0.5f);

            BuildCoinIcon(body.transform, new Vector2(-79f, 0f), 56f);

            // Left-aligned so the balance grows away from the coin rather than sliding under
            // it: a centred number moves both edges every time the total changes.
            var count = AddText(body.transform, "CoinCount", "0", font, 38f, Ink);
            count.fontStyle = FontStyles.Bold;
            count.alignment = TextAlignmentOptions.Left;
            Place(count.gameObject, new Vector2(0.5f, 0.5f), new Vector2(5f, 0f), new Vector2(88f, 52f));
            count.gameObject.AddComponent<UIWalletLabel>();

            var plus = Node("Plus", body.transform);
            Place(plus, new Vector2(1f, 0.5f), new Vector2(-36f, 0f), new Vector2(46f, 46f));
            var plusDisc = plus.AddComponent<Image>();
            plusDisc.sprite = UISpriteFactory.Circle;
            plusDisc.color = Amber;
            plusDisc.raycastTarget = false;

            var plusMark = Node("Mark", plus.transform);
            Place(plusMark, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(26f, 26f));
            var plusImage = plusMark.AddComponent<Image>();
            plusImage.sprite = SkinIcon(UISpriteFactory.IconPlus, LayerLabSkin.IconPlus);
            plusImage.color = Panel;
            plusImage.raycastTarget = false;

            return Finish(go, body);
        }

        // A coin, out of three circles and no new sprite: a dark disc for the outline, an
        // amber face, and a darker ring for the rim. The rim is what stops it reading as a
        // plain amber dot at 60 units across.
        static void BuildCoinIcon(Transform parent, Vector2 position, float diameter)
        {
            var coin = Node("Coin", parent);
            Place(coin, new Vector2(0.5f, 0.5f), position, new Vector2(diameter, diameter));

            if (LayerLabSkin.Available && LayerLabSkin.Coin != null)
            {
                var packCoin = coin.AddComponent<Image>();
                packCoin.sprite = LayerLabSkin.Coin;
                packCoin.preserveAspect = true;
                packCoin.raycastTarget = false;
                // The pack's coin is drawn with its own shadow and runs to the edge; a touch
                // larger keeps it the same visual size as the flat one it replaces.
                RectOf(coin).sizeDelta = new Vector2(diameter * 1.15f, diameter * 1.15f);
                return;
            }

            var outline = coin.AddComponent<Image>();
            outline.sprite = UISpriteFactory.Circle;
            outline.color = Ink;
            outline.raycastTarget = false;

            var face = Node("Face", coin.transform);
            Place(face, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(diameter - 9f, diameter - 9f));
            var faceImage = face.AddComponent<Image>();
            faceImage.sprite = UISpriteFactory.Circle;
            faceImage.color = Amber;
            faceImage.raycastTarget = false;

            var rim = Node("Rim", face.transform);
            Place(rim, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(diameter * 0.6f, diameter * 0.6f));
            var rimImage = rim.AddComponent<Image>();
            rimImage.sprite = UISpriteFactory.Ring;
            rimImage.color = Darken(Amber, 0.78f);
            rimImage.raycastTarget = false;
        }

        static void BuildBoosters(Transform parent, TMP_FontAsset font, out Button hintButton, out Button shuffle,
                                  out Button remove, out GameObject armedRing, out GameObject armedHint)
        {
            // Raised clear of the bottom edge because each button now carries a caption
            // hanging under it. The spacing is set by the count badges, not the buttons: a
            // badge hangs off its button's top-right corner, so at the 660-wide row three
            // 184 buttons left only 3 units between one badge and the next button. 720 puts
            // 19 there and 22 between the captions, which are the two tightest pairs.
            var row = Node("Boosters", parent);
            Place(row, new Vector2(0.5f, 0f), new Vector2(0f, 208f), new Vector2(720f, 200f));
            RectOf(row).pivot = new Vector2(0.5f, 0f);

            // Left to right by how much each one does: a hint saves one tap, a shuffle
            // changes the whole board, a remove takes a piece outright.
            hintButton = BuildIconButton(row.transform, "HintButton", SkinIcon(UISpriteFactory.IconBulb, LayerLabSkin.IconBulb), Grape, 184f, 96f, "Hint", font);
            Place(hintButton.transform.parent.gameObject, new Vector2(0.5f, 0.5f), new Vector2(-232f, 0f), new Vector2(184f, 184f));
            AddShine(hintButton.targetGraphic as Image, 52f);
            AddCountBadge(hintButton, SheepGame.Core.Meta.ItemIds.Hint, font);

            shuffle = BuildIconButton(row.transform, "ShuffleButton", SkinIcon(UISpriteFactory.IconShuffle, LayerLabSkin.IconShuffle), Blue, 184f, 100f, "Shuffle", font);
            Place(shuffle.transform.parent.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(184f, 184f));
            AddShine(shuffle.targetGraphic as Image, 52f);
            AddCountBadge(shuffle, SheepGame.Core.Meta.ItemIds.Shuffle, font);

            remove = BuildIconButton(row.transform, "RemoveButton", SkinIcon(UISpriteFactory.IconHammer, LayerLabSkin.IconHammer), Coral, 184f, 96f, "Remove", font);
            Place(remove.transform.parent.gameObject, new Vector2(0.5f, 0.5f), new Vector2(232f, 0f), new Vector2(184f, 184f));
            AddShine(remove.targetGraphic as Image, 52f);
            AddCountBadge(remove, SheepGame.Core.Meta.ItemIds.Remove, font);

            // The button face carries a stencil Mask for its shine. The ring is deliberately
            // a sibling above that face, not its child, so the pulse can extend past the
            // button without being clipped by the mask.
            var ring = Node("ArmedRing", remove.transform.parent);
            Place(ring, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(232f, 232f));
            var ringImage = ring.AddComponent<Image>();
            ringImage.sprite = UISpriteFactory.Ring;
            ringImage.color = Alpha(Panel, 0.95f);
            ringImage.raycastTarget = false;
            ring.AddComponent<UIPulse>();
            ring.transform.SetAsLastSibling();
            ring.SetActive(false);
            armedRing = ring;

            var hint = Node("RemoveHint", parent);
            Place(hint, new Vector2(0.5f, 0f), new Vector2(0f, 396f), new Vector2(640f, 88f));
            RectOf(hint).pivot = new Vector2(0.5f, 0f);
            var hintFill = AddSticker(hint, UISpriteFactory.Round, Coral, 44f);
            var hintText = AddText(hintFill.transform, "Text", "Tap a piece to remove it", font, 34f, Panel);
            hintText.fontStyle = FontStyles.Bold;
            Stretch(hintText.gameObject, 20f);
            hint.SetActive(false);
            armedHint = hint;
        }

        // The count sits on the button's slot, not inside the button: the shine effect puts
        // a circular Mask on the fill, and anything parented under that gets clipped to the
        // disc — a badge hanging off the corner would lose the half that hangs off.
        static UIItemCounter AddCountBadge(Button button, string itemId, TMP_FontAsset font)
        {
            var slot = button.transform.parent;

            var badge = Node("CountBadge", slot);
            Place(badge, new Vector2(1f, 1f), new Vector2(-4f, -4f), new Vector2(66f, 66f));
            var fill = AddSticker(badge, UISpriteFactory.Circle, Panel, 33f, 5f);
            fill.raycastTarget = false;
            // The outline this sticker put on the badge itself must not eat taps meant for
            // the button underneath it.
            var badgeImage = badge.GetComponent<Image>();
            if (badgeImage != null) badgeImage.raycastTarget = false;

            var count = AddText(fill.transform, "Count", "0", font, 36f, Ink);
            count.fontStyle = FontStyles.Bold;
            Stretch(count.gameObject);

            // Empty shows a plus rather than a zero: a zero reads as a broken button, a plus
            // reads as an offer — and pressing it is what opens the shop.
            var plus = Node("Plus", fill.transform);
            Place(plus, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(32f, 32f));
            var plusImage = plus.AddComponent<Image>();
            plusImage.sprite = SkinIcon(UISpriteFactory.IconPlus, LayerLabSkin.IconPlus);
            plusImage.color = Coral;
            plusImage.raycastTarget = false;

            var counter = badge.AddComponent<UIItemCounter>();
            counter.itemId = itemId;
            counter.countLabel = count;
            counter.emptyIcon = plus;
            return counter;
        }

        static GameObject BuildWinPanel(Transform parent, TMP_FontAsset font, out Button next,
                                        out Button replay, out TMP_Text reward)
        {
            var safe = BuildOverlay(parent, "WinPanel", 0.62f, out var overlay, out var group);

            // 760 rather than 700: at 700 the title, the subtitle and the reward chip sat 8
            // and 17 apart, measured on their rendered glyphs rather than their boxes. Text
            // blocks want about 30 between them before they stop reading as one paragraph.
            var card = Node("Card", safe);
            Place(card, new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(860f, 760f));
            var cardFill = AddSticker(card, UISpriteFactory.Round, Card, 58f, 9f);
            var pop = card.AddComponent<UIPopIn>();
            pop.fadeGroup = group;

            // Badge straddling the card's top edge — the one piece of decoration, and the
            // reason the card's own title starts low.
            var badge = Node("Badge", cardFill.transform);
            Place(badge, new Vector2(0.5f, 1f), new Vector2(0f, 26f), new Vector2(190f, 190f));
            var badgeFill = AddSticker(badge, UISpriteFactory.Circle, Amber, 95f);
            badgeFill.raycastTarget = false;
            var check = Node("Check", badgeFill.transform);
            Place(check, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(104f, 104f));
            var checkImage = check.AddComponent<Image>();
            checkImage.sprite = SkinIcon(UISpriteFactory.IconCheck, LayerLabSkin.IconCheck);
            checkImage.color = Panel;
            checkImage.raycastTarget = false;

            var title = AddText(cardFill.transform, "Title", "Level complete!", font, 76f, Ink);
            title.fontStyle = FontStyles.UpperCase | FontStyles.Bold;
            title.characterSpacing = 2f;
            Place(title.gameObject, new Vector2(0.5f, 1f), new Vector2(0f, -190f), new Vector2(760f, 96f));

            var subtitle = AddText(cardFill.transform, "Subtitle", "The board is clear. On to the next one.", font, 34f, Muted);
            Place(subtitle.gameObject, new Vector2(0.5f, 1f), new Vector2(0f, -306f), new Vector2(700f, 60f));

            // The reward is the reason unused taps are worth anything, so it is shown as a
            // won amount rather than folded silently into the balance at the top of the screen.
            var rewardChip = Node("Reward", cardFill.transform);
            Place(rewardChip, new Vector2(0.5f, 1f), new Vector2(0f, -420f), new Vector2(300f, 104f));
            var rewardFill = AddSticker(rewardChip, UISpriteFactory.Round, Hex("FFF3D2"), 52f, 5f);
            rewardFill.raycastTarget = false;
            BuildCoinIcon(rewardFill.transform, new Vector2(0f, 0f), 62f);
            reward = AddText(rewardFill.transform, "RewardText", "+0", font, 48f, Ink);
            reward.fontStyle = FontStyles.Bold;
            reward.alignment = TextAlignmentOptions.Left;
            // Starts 12 clear of the coin's right edge. Left-aligned text begins at its box's
            // left edge, so the box has to be placed, not centred and hoped for.
            Place(reward.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(170f, 60f));

            // The pill was centred; its contents were not. The coin sat at -88 and the number
            // in a 170-wide left-aligned box at +40, so a two-digit reward drew its ink across
            // 421..572 inside a pill spanning 390..690 -- 31 units of air on the left against
            // 118 on the right, which is the lean that reads as "slipped".
            //
            // Laid out rather than positioned, because the number is not a fixed width: +50
            // and +1000 are 60 units apart, so any pair of hand-picked offsets is wrong for
            // one of them. The group measures its content and centres it, whatever it says.
            CentreRow(rewardFill.gameObject, spacing: 12f);
            AddFixedSize(reward.transform.parent.Find("Coin"), 62f, 62f);

            replay = BuildIconButton(cardFill.transform, "ReplayButton", SkinIcon(UISpriteFactory.IconRestart, LayerLabSkin.IconRestart), Slate, 132f, 68f);
            Place(replay.transform.parent.gameObject, new Vector2(0f, 0f), new Vector2(160f, 150f), new Vector2(132f, 132f));

            next = BuildPillButton(cardFill.transform, "NextButton", "Next", font, Amber, Ink,
                                   new Vector2(500f, 156f), out _);
            AddShine(next.targetGraphic as Image, 70f, 0.42f);
            Place(next.transform.parent.gameObject, new Vector2(0.5f, 0f), new Vector2(90f, 140f), new Vector2(500f, 156f));

            overlay.SetActive(false);
            return overlay;
        }

        // Everything on the lose panel the wiring needs to reach. Eight out parameters is
        // where a signature stops being readable.
        struct LosePanelParts
        {
            public GameObject Root;
            public Button Continue;
            public Button Shuffle;
            public Button Replay;
            public Button Shop;
            public TMP_Text ContinueLabel;
            public TMP_Text ContinuePrice;
            public TMP_Text ShuffleLabel;
            public TMP_Text ShufflePrice;
        }

        // The mirror of the win panel, and deliberately not a dead end: the two rescues are
        // the first things under the thumb, and the shop is one press further.
        //
        // Two rescues rather than one because they fix different things. More taps forgive
        // the guesses already made; a reshuffle changes how many of the next ones will be
        // wrong. Offering only the first makes the panel a toll booth, and offering only the
        // second is useless to a player who has no taps left to use it with.
        static LosePanelParts BuildLosePanel(Transform parent, TMP_FontAsset font)
        {
            var parts = new LosePanelParts();

            var safe = BuildOverlay(parent, "LosePanel", 0.62f, out var overlay, out var group);

            // 900 rather than 830: at 830 the title and subtitle touched, and the lower of the
            // two rescues ran straight through the shop button and the restart under it.
            var card = Node("Card", safe);
            Place(card, new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(860f, 900f));
            var cardFill = AddSticker(card, UISpriteFactory.Round, Card, 58f, 9f);
            var pop = card.AddComponent<UIPopIn>();
            pop.fadeGroup = group;

            var badge = Node("Badge", cardFill.transform);
            Place(badge, new Vector2(0.5f, 1f), new Vector2(0f, 26f), new Vector2(190f, 190f));
            var badgeFill = AddSticker(badge, UISpriteFactory.Circle, Coral, 95f);
            badgeFill.raycastTarget = false;
            var mark = Node("Mark", badgeFill.transform);
            Place(mark, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(88f, 88f));
            var markImage = mark.AddComponent<Image>();
            markImage.sprite = SkinIcon(UISpriteFactory.IconClose, LayerLabSkin.IconClose);
            markImage.color = Panel;
            markImage.raycastTarget = false;

            var title = AddText(cardFill.transform, "Title", "Out of taps!", font, 76f, Ink);
            title.fontStyle = FontStyles.UpperCase | FontStyles.Bold;
            title.characterSpacing = 2f;
            Place(title.gameObject, new Vector2(0.5f, 1f), new Vector2(0f, -186f), new Vector2(760f, 96f));

            var subtitle = AddText(cardFill.transform, "Subtitle",
                                   "Keep going, or turn every case a new way.", font, 34f, Muted);
            Place(subtitle.gameObject, new Vector2(0.5f, 1f), new Vector2(0f, -300f), new Vector2(740f, 60f));

            // Amber and top: the plain top-up is the offer most players take, so it gets the
            // reach and the shine. The reshuffle sits under it in the cooler colour — the
            // considered choice, not the reflex one.
            parts.Continue = BuildPillButton(cardFill.transform, "ContinueButton", "Continue", font,
                                             Amber, Ink, new Vector2(540f, 158f), out parts.ContinueLabel,
                                             chevron: false, priceRoom: PriceChipWidth);
            AddShine(parts.Continue.targetGraphic as Image, 70f, 0.42f);
            var continueSlot = parts.Continue.transform.parent.gameObject;
            Place(continueSlot, new Vector2(0.5f, 0f), new Vector2(0f, 420f), new Vector2(540f, 158f));
            parts.ContinuePrice = AddPriceChip(continueSlot.transform, font);

            parts.Shuffle = BuildPillButton(cardFill.transform, "ShuffleRescueButton", "Shuffle", font,
                                            Blue, Panel, new Vector2(540f, 140f), out parts.ShuffleLabel,
                                            chevron: false, priceRoom: PriceChipWidth);
            var shuffleSlot = parts.Shuffle.transform.parent.gameObject;
            Place(shuffleSlot, new Vector2(0.5f, 0f), new Vector2(0f, 250f), new Vector2(540f, 140f));
            parts.ShufflePrice = AddPriceChip(shuffleSlot.transform, font);

            // The bottom row is the way out, either way. Kept well clear of the rescues above
            // it: a thumb reaching for "shuffle" must not be able to land on "restart".
            parts.Shop = BuildPillButton(cardFill.transform, "ShopButton", "Shop", font,
                                         Hex("DCE3EC"), Slate, new Vector2(300f, 112f), out _);
            Place(parts.Shop.transform.parent.gameObject, new Vector2(1f, 0f), new Vector2(-214f, 88f), new Vector2(300f, 112f));

            parts.Replay = BuildIconButton(cardFill.transform, "LoseReplayButton", SkinIcon(UISpriteFactory.IconRestart, LayerLabSkin.IconRestart), Slate, 124f, 64f);
            Place(parts.Replay.transform.parent.gameObject, new Vector2(0f, 0f), new Vector2(152f, 88f), new Vector2(124f, 124f));

            overlay.SetActive(false);
            parts.Root = overlay;
            return parts;
        }

        // The price sits inside the right end of the button it belongs to, so there is never
        // a question of which thing costs what — and the two rescues can sit one above the
        // other without their prices being read against the wrong one.
        //
        // It used to straddle the button's top-right corner, which looked fine alone and
        // collided with whatever was above: the chip reached 41 units past the button and ate
        // the gap to the pill overhead. Inside the pill it cannot reach anything.
        //
        // 160 rather than 200 (2026-09-06, user's call): a three-digit price plus the coin
        // icon never needed the extra 40, and the width feeds straight into how far the
        // button's own label gets pushed off-centre — see BuildPillButton.
        const float PriceChipWidth = 160f;
        const float PriceChipMargin = 16f;   // the chip's own gap to the button's right edge

        static TMP_Text AddPriceChip(Transform buttonSlot, TMP_FontAsset font)
        {
            var price = Node("Price", buttonSlot);
            Place(price, new Vector2(1f, 0.5f), new Vector2(-(PriceChipWidth * 0.5f + PriceChipMargin), 0f),
                  new Vector2(PriceChipWidth, 66f));

            var priceFill = AddSticker(price, UISpriteFactory.Round, Ink, 33f, 5f);
            priceFill.raycastTarget = false;
            // The chip sits over the button it prices, so it must not swallow the tap.
            var priceImage = price.GetComponent<Image>();
            if (priceImage != null) priceImage.raycastTarget = false;

            BuildCoinIcon(priceFill.transform, Vector2.zero, 44f);

            var label = AddText(priceFill.transform, "PriceText", "0", font, 34f, Panel);
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Left;
            Place(label.gameObject, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(80f, 44f));

            // Centred as a group rather than positioned by hand: "0" and "160" are different
            // widths, and a fixed offset is only ever right for one of them. A fixed 160-wide
            // chip built to hold "160" left a one-digit "0" hugging the coin with a wide strip
            // of empty pill on the right -- the same problem the win panel's reward pill had,
            // fixed there with the same row.
            CentreRow(priceFill.gameObject, spacing: 10f);
            AddFixedSize(priceFill.transform.Find("Coin"), 44f, 44f);
            return label;
        }

        // Every full-screen panel is three layers: the overlay itself, a dim that covers the
        // whole screen including the notch, and a safe-area layer holding everything the
        // player reads or touches.
        //
        // The dim and the content want opposite things — the dim must reach the very edges or
        // a bright strip shows beside the Dynamic Island, while a title or a close button up
        // there would be under it. Only the HUD had a safe area before this; the panels were
        // stretched to the raw screen, which put the menu's coin pill directly beneath the
        // island on any recent iPhone.
        static Transform BuildOverlay(Transform parent, string name, float dimAlpha,
                                      out GameObject overlay, out CanvasGroup group)
        {
            overlay = Node(name, parent);
            Stretch(overlay);
            group = overlay.AddComponent<CanvasGroup>();

            var dim = Node("Dim", overlay.transform);
            Stretch(dim);
            var dimImage = dim.AddComponent<Image>();
            dimImage.color = Alpha(Hex("0C141D"), dimAlpha);

            var safe = Node("SafeArea", overlay.transform);
            Stretch(safe);
            safe.AddComponent<SafeAreaFitter>();
            return safe.transform;
        }

        // ---- the clock ----------------------------------------------------------------

        // Its own chip under the level card, amber so it cannot be mistaken for the readouts
        // in the card above it, and coral in the closing seconds. Only on screen during a
        // Time Rush -- UITimerPill switches it with the mode.
        static UITimerPill BuildTimerPill(Transform parent, TMP_FontAsset font, GameManager gm)
        {
            var size = new Vector2(240f, 96f);

            var slot = Node("TimerPillSlot", parent);
            // Under the level card, which ends 244 down, with the usual gap between blocks.
            Place(slot, new Vector2(0.5f, 1f), new Vector2(0f, -322f), size);

            // The slot stays active and everything visible hangs off Body, which is what gets
            // switched. The component lives on the slot for a reason: it subscribes to the
            // manager in OnEnable, and Unity never sends OnEnable to a disabled object -- a
            // component sitting on the thing it is supposed to switch on can never switch it
            // on, because it is asleep until something else does.
            var body = Node("Body", slot.transform);
            Stretch(body);

            var lip = Node("Lip", body.transform);
            Place(lip, new Vector2(0.5f, 0.5f), new Vector2(0f, -Lip), size);
            AddPanel(lip, UISpriteFactory.Round, Ink, size.y * 0.5f).raycastTarget = false;

            var pill = Node("TimerPill", body.transform);
            Place(pill, new Vector2(0.5f, 0.5f), Vector2.zero, size);
            var fill = AddSticker(pill, UISpriteFactory.Round, Amber, size.y * 0.5f);

            var clock = Node("Clock", fill.transform);
            Place(clock, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(56f, 56f));
            var clockImage = clock.AddComponent<Image>();
            clockImage.sprite = SkinIcon(UISpriteFactory.IconClock, LayerLabSkin.IconClock);
            clockImage.color = Ink;
            clockImage.raycastTarget = false;

            var seconds = AddText(fill.transform, "TimeText", "0", font, 46f, Ink);
            seconds.fontStyle = FontStyles.Bold;
            seconds.alignment = TextAlignmentOptions.Center;
            Place(seconds.gameObject, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(120f, 60f));

            // The number is one to three digits wide, so the pair is laid out rather than
            // positioned -- the same reason the reward pill is.
            CentreRow(fill.gameObject, spacing: 12f);
            AddFixedSize(clock.transform, 56f, 56f);

            var view = slot.AddComponent<UITimerPill>();
            view.gameManager = gm;
            view.root = body;
            view.label = seconds;
            view.fill = fill;
            view.clock = clockImage;
            view.popTarget = RectOf(body);
            view.normalFill = Amber;
            view.warningFill = Coral;
            view.normalInk = LayerLabSkin.Available ? Color.white : Ink;
            view.warningInk = Panel;

            body.SetActive(false);
            return view;
        }

        // ---- mode banner --------------------------------------------------------------

        // Announces a change of rules and then gets out of the way. Not a dialog: there is
        // nothing to press, and the round it announces does not start until it has gone.
        static UIModeBanner BuildModeBanner(Transform parent, TMP_FontAsset font)
        {
            // A light dim. The board behind it is the thing the announcement is about, so it
            // stays readable -- but the sheet still takes the taps, because a tap aimed at the
            // banner must not fall through onto a level that has not started.
            var safe = BuildOverlay(parent, "ModeBanner", 0.45f, out var overlay, out var group);

            var card = Node("Card", safe);
            Place(card, new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(780f, 300f));
            var cardFill = AddSticker(card, UISpriteFactory.Round, Card, 58f, 9f);

            // The badge hangs off the top edge, the same shape the win panel uses, so a change
            // of rules reads as a sibling of a level result rather than as a new kind of thing.
            var badge = Node("Badge", cardFill.transform);
            Place(badge, new Vector2(0.5f, 1f), new Vector2(0f, 22f), new Vector2(168f, 168f));
            var badgeFill = AddSticker(badge, UISpriteFactory.Circle, Amber, 84f);

            var clock = Node("Clock", badgeFill.transform);
            Place(clock, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(92f, 92f));
            var clockImage = clock.AddComponent<Image>();
            clockImage.sprite = SkinIcon(UISpriteFactory.IconClock, LayerLabSkin.IconClock);
            clockImage.color = Ink;
            clockImage.raycastTarget = false;

            var title = AddText(cardFill.transform, "Title", "Time Rush", font, 72f, Ink);
            title.fontStyle = FontStyles.UpperCase | FontStyles.Bold;
            title.characterSpacing = 3f;
            title.alignment = TextAlignmentOptions.Center;
            title.enableAutoSizing = true;
            title.fontSizeMin = 44f;
            title.fontSizeMax = 72f;
            Place(title.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0f, 16f), new Vector2(680f, 92f));

            var subtitle = AddText(cardFill.transform, "Subtitle", "Beat the clock. No tap limit.",
                                   font, 32f, Muted);
            subtitle.alignment = TextAlignmentOptions.Center;
            Place(subtitle.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0f, -56f), new Vector2(680f, 52f));

            var banner = overlay.AddComponent<UIModeBanner>();
            banner.root = overlay;
            banner.card = RectOf(card);
            banner.group = group;
            banner.title = title;
            banner.subtitle = subtitle;

            overlay.SetActive(false);
            return banner;
        }

        // ---- main menu ---------------------------------------------------------------

        // Sits over the game scene rather than in a scene of its own, so the hall behind it is
        // the backdrop and the heavy scene is loaded once instead of twice. The board is
        // already dealt underneath, which is why Play only has to get out of the way.
        static MainMenuPanel BuildMainMenu(Transform parent, TMP_FontAsset font, GameObject hudRoot,
                                           GameManager gm, ShopPanel shop)
        {
            // Light dim rather than the panels' 0.62: this one is meant to be seen through.
            // The terminal is the game's best asset and the menu is standing on it.
            var safe = BuildOverlay(parent, "MainMenu", 0.38f, out var overlay, out var group);

            BuildCoinPill(safe, font);

            // The wordmark, not a text card: Logo.png already carries its own name, colour
            // and shadow, so a white panel behind it would just be an unused frame. Sized to
            // the art's own aspect (1672x940) rather than a fixed box, so a redrawn logo with
            // different proportions still comes out undistorted next time this runs.
            var logo = Node("Logo", safe);
            var logoSprite = EnsureLogoSprite();
            float logoWidth = 760f;
            float logoHeight = logoSprite != null
                ? logoWidth * logoSprite.rect.height / logoSprite.rect.width
                : logoWidth * 940f / 1672f;
            Place(logo, new Vector2(0.5f, 1f), new Vector2(0f, -190f - logoHeight * 0.5f),
                  new Vector2(logoWidth, logoHeight));
            var logoImage = logo.AddComponent<Image>();
            logoImage.sprite = logoSprite;
            logoImage.preserveAspect = true;
            logoImage.raycastTarget = false;
            var pop = logo.AddComponent<UIPopIn>();
            pop.fadeGroup = group;

            // The level pill used to live inside the title card's bottom edge; it is its own
            // chip now, hanging under the logo with the usual gap rather than sharing a box
            // with it (user's call, 2026-09-06) — the logo is a fixed piece of art and the
            // level number is not, and the two read as separate things anyway.
            float logoBottom = 190f + logoHeight;
            var levelChip = Node("LevelChip", safe);
            Place(levelChip, new Vector2(0.5f, 1f), new Vector2(0f, -(logoBottom + 30f + 46f)),
                  new Vector2(320f, 92f));
            var levelFill = AddSticker(levelChip, UISpriteFactory.Round, Hex("E9EEF5"), 46f, 5f);
            levelFill.raycastTarget = false;
            var levelChipImage = levelChip.GetComponent<Image>();
            if (levelChipImage != null) levelChipImage.raycastTarget = false;
            var levelText = AddText(levelFill.transform, "LevelText", "Level 1", font, 44f, Slate);
            levelText.fontStyle = FontStyles.Bold;
            Stretch(levelText.gameObject, 10f);

            var play = BuildPillButton(safe, "PlayButton", "Play", font, Amber, Ink,
                                       new Vector2(560f, 180f), out _);
            AddShine(play.targetGraphic as Image, 76f, 0.42f);
            Place(play.transform.parent.gameObject, new Vector2(0.5f, 0f), new Vector2(0f, 620f), new Vector2(560f, 180f));

            var shopButton = BuildPillButton(safe, "MenuShopButton", "Shop", font,
                                             Blue, Panel, new Vector2(360f, 130f), out _);
            Place(shopButton.transform.parent.gameObject, new Vector2(0.5f, 0f), new Vector2(0f, 420f), new Vector2(360f, 130f));

            // Below Play and Shop rather than beside them: Play is the one thing the menu is
            // for, and a second button of the same weight next to it makes the player choose
            // before they have started.
            var dailyButton = BuildPillButton(safe, "DailyButton", "Daily Run", font,
                                              Hex("5DB3B2"), Ink, new Vector2(360f, 130f), out _);
            Place(dailyButton.transform.parent.gameObject, new Vector2(0.5f, 0f),
                  new Vector2(0f, 260f), new Vector2(360f, 130f));

            var settingsButton = BuildPillButton(safe, "SettingsButton", "Settings", font,
                                                 Grape, Panel, new Vector2(360f, 112f), out _);
            Place(settingsButton.transform.parent.gameObject, new Vector2(0.5f, 0f),
                  new Vector2(0f, 112f), new Vector2(360f, 112f));

            var menu = overlay.AddComponent<MainMenuPanel>();
            menu.gameManager = gm;
            menu.shopPanel = shop;
            menu.root = overlay;
            menu.hudRoot = hudRoot;
            menu.playButton = play;
            menu.shopButton = shopButton;
            menu.dailyButton = dailyButton;
            menu.settingsButton = settingsButton;
            menu.levelLabel = levelText;

            return menu;
        }

        // ---- settings and privacy ---------------------------------------------------

        static SettingsPanel BuildSettingsPanel(Transform parent, TMP_FontAsset font,
                                                 PrivacyChoicesPanel privacy)
        {
            var safe = BuildOverlay(parent, "SettingsPanel", 0.7f, out var overlay, out var group);
            var card = Node("Card", safe);
            Place(card, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900f, 1120f));
            var cardFill = AddSticker(card, UISpriteFactory.Round, Hex("DDF4FF"), 58f, 9f);
            var pop = card.AddComponent<UIPopIn>();
            pop.fadeGroup = group;

            var title = AddText(cardFill.transform, "Title", "Settings", font, 72f, Color.white);
            title.fontStyle = FontStyles.UpperCase | FontStyles.Bold;
            Place(title.gameObject, new Vector2(0.5f, 1f), new Vector2(0f, -105f), new Vector2(720f, 94f));

            var heading = Node("Heading", cardFill.transform);
            Place(heading, new Vector2(0.5f, 1f), new Vector2(0f, -105f), new Vector2(760f, 118f));
            var headingFill = AddSticker(heading, UISpriteFactory.Round, Blue, 50f, 5f);
            headingFill.raycastTarget = false;
            heading.transform.SetAsFirstSibling();

            var sound = BuildSettingToggle(cardFill.transform, "SoundButton", "Sound", font,
                                           Grape, new Vector2(700f, 140f), out var soundValue);
            Place(sound.transform.parent.gameObject, new Vector2(0.5f, 1f), new Vector2(0f, -305f), new Vector2(700f, 140f));

            var music = BuildSettingToggle(cardFill.transform, "MusicButton", "Music", font,
                                           Blue, new Vector2(700f, 140f), out var musicValue);
            Place(music.transform.parent.gameObject, new Vector2(0.5f, 1f), new Vector2(0f, -475f), new Vector2(700f, 140f));

            var vibration = BuildSettingToggle(cardFill.transform, "VibrationButton", "Vibration", font,
                                               Coral, new Vector2(700f, 140f), out var vibrationValue);
            Place(vibration.transform.parent.gameObject, new Vector2(0.5f, 1f), new Vector2(0f, -645f), new Vector2(700f, 140f));

            var privacyButton = BuildPillButton(cardFill.transform, "PrivacyChoicesButton",
                                                "Privacy Choices", font, Hex("36C7A2"), Panel,
                                                new Vector2(700f, 140f), out _);
            Place(privacyButton.transform.parent.gameObject, new Vector2(0.5f, 1f),
                  new Vector2(0f, -815f), new Vector2(700f, 140f));

            var close = BuildPillButton(cardFill.transform, "SettingsCloseButton", "Close", font,
                                        Amber, Ink, new Vector2(420f, 124f), out _);
            Place(close.transform.parent.gameObject, new Vector2(0.5f, 0f),
                  new Vector2(0f, 94f), new Vector2(420f, 124f));

            var panel = overlay.AddComponent<SettingsPanel>();
            panel.root = overlay;
            panel.soundButton = sound;
            panel.musicButton = music;
            panel.vibrationButton = vibration;
            panel.privacyButton = privacyButton;
            panel.closeButton = close;
            panel.soundValue = soundValue;
            panel.musicValue = musicValue;
            panel.vibrationValue = vibrationValue;
            panel.privacyPanel = privacy;
            overlay.SetActive(false);
            return panel;
        }

        static PrivacyChoicesPanel BuildPrivacyChoicesPanel(Transform parent, TMP_FontAsset font)
        {
            var safe = BuildOverlay(parent, "PrivacyChoicesPanel", 0.78f, out var overlay, out var group);
            var card = Node("Card", safe);
            Place(card, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900f, 900f));
            var cardFill = AddSticker(card, UISpriteFactory.Round, Hex("EEE3FF"), 58f, 9f);
            var pop = card.AddComponent<UIPopIn>();
            pop.fadeGroup = group;

            var title = AddText(cardFill.transform, "Title", "Privacy Choices", font, 64f, Color.white);
            title.fontStyle = FontStyles.UpperCase | FontStyles.Bold;
            Place(title.gameObject, new Vector2(0.5f, 1f), new Vector2(0f, -105f), new Vector2(760f, 90f));
            var heading = Node("Heading", cardFill.transform);
            Place(heading, new Vector2(0.5f, 1f), new Vector2(0f, -105f), new Vector2(790f, 118f));
            AddSticker(heading, UISpriteFactory.Round, Grape, 50f, 5f).raycastTarget = false;
            heading.transform.SetAsFirstSibling();

            var description = AddText(cardFill.transform, "Description",
                "Choose how ads may use your data. You can change this anytime in Settings.",
                font, 34f, Slate);
            description.alignment = TextAlignmentOptions.Center;
            description.textWrappingMode = TextWrappingModes.Normal;
            Place(description.gameObject, new Vector2(0.5f, 1f), new Vector2(0f, -245f), new Vector2(720f, 120f));

            var personalized = BuildPillButton(cardFill.transform, "PersonalizedAdsButton",
                                               "Personalized Ads", font, Hex("36C7A2"), Panel,
                                               new Vector2(700f, 142f), out _);
            Place(personalized.transform.parent.gameObject, new Vector2(0.5f, 1f),
                  new Vector2(0f, -420f), new Vector2(700f, 142f));

            var limited = BuildPillButton(cardFill.transform, "LimitedAdsButton", "Limited Ads", font,
                                          Blue, Panel, new Vector2(700f, 142f), out _);
            Place(limited.transform.parent.gameObject, new Vector2(0.5f, 1f),
                  new Vector2(0f, -595f), new Vector2(700f, 142f));

            var close = BuildPillButton(cardFill.transform, "PrivacyCloseButton", "Close", font,
                                        Hex("DCE3EC"), Slate, new Vector2(360f, 112f), out _);
            Place(close.transform.parent.gameObject, new Vector2(0.5f, 0f),
                  new Vector2(0f, 82f), new Vector2(360f, 112f));

            var panel = overlay.AddComponent<PrivacyChoicesPanel>();
            panel.root = overlay;
            panel.personalizedButton = personalized;
            panel.limitedButton = limited;
            panel.closeButton = close;
            overlay.SetActive(false);
            return panel;
        }

        static Button BuildSettingToggle(Transform parent, string name, string label,
                                         TMP_FontAsset font, Color colour, Vector2 size,
                                         out TMP_Text value)
        {
            var button = BuildPillButton(parent, name, label, font, colour, Panel, size, out var caption);
            Place(caption.gameObject, new Vector2(0.5f, 0.5f), new Vector2(-95f, 0f),
                  new Vector2(size.x - 250f, size.y));
            caption.alignment = TextAlignmentOptions.Left;

            var badge = Node("Value", button.transform);
            Place(badge, new Vector2(1f, 0.5f), new Vector2(-90f, 0f), new Vector2(150f, 86f));
            var badgeFill = AddSticker(badge, UISpriteFactory.Round, Darken(colour, 0.72f), 38f, 4f);
            badgeFill.raycastTarget = false;
            value = AddText(badgeFill.transform, "ValueText", "ON", font, 38f, Color.white);
            value.fontStyle = FontStyles.UpperCase | FontStyles.Bold;
            Stretch(value.gameObject, 4f);
            return button;
        }

        // ---- pause ---------------------------------------------------------------------

        // Raised by the system taking the screen away — the shade, Control Centre, a call.
        // Deliberately the last panel built, so it sits above everything: an interruption
        // arriving over the shop has to be answered before the shop is.
        static PausePanel BuildPausePanel(Transform parent, TMP_FontAsset font,
                                          GameManager gm, MainMenuPanel menu)
        {
            var safe = BuildOverlay(parent, "PausePanel", 0.62f, out var overlay, out var group);

            var card = Node("Card", safe);
            Place(card, new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(860f, 680f));
            var cardFill = AddSticker(card, UISpriteFactory.Round, Card, 58f, 9f);
            var pop = card.AddComponent<UIPopIn>();
            pop.fadeGroup = group;

            var title = AddText(cardFill.transform, "Title", "Paused", font, 76f, Ink);
            title.fontStyle = FontStyles.UpperCase | FontStyles.Bold;
            title.characterSpacing = 2f;
            Place(title.gameObject, new Vector2(0.5f, 1f), new Vector2(0f, -130f), new Vector2(700f, 100f));

            var resume = BuildPillButton(cardFill.transform, "ResumeButton", "Resume", font,
                                         Amber, Ink, new Vector2(520f, 160f), out _);
            AddShine(resume.targetGraphic as Image, 70f, 0.42f);
            Place(resume.transform.parent.gameObject, new Vector2(0.5f, 0f), new Vector2(0f, 330f), new Vector2(520f, 160f));

            var menuButton = BuildPillButton(cardFill.transform, "MenuButton", "Menu", font,
                                             Hex("DCE3EC"), Slate, new Vector2(300f, 112f), out _);
            Place(menuButton.transform.parent.gameObject, new Vector2(1f, 0f), new Vector2(-214f, 110f), new Vector2(300f, 112f));

            var restart = BuildIconButton(cardFill.transform, "PauseRestartButton", SkinIcon(UISpriteFactory.IconRestart, LayerLabSkin.IconRestart), Slate, 124f, 64f);
            Place(restart.transform.parent.gameObject, new Vector2(0f, 0f), new Vector2(152f, 110f), new Vector2(124f, 124f));

            var pause = overlay.AddComponent<PausePanel>();
            pause.gameManager = gm;
            pause.mainMenu = menu;
            pause.root = overlay;
            pause.resumeButton = resume;
            pause.restartButton = restart;
            pause.menuButton = menuButton;

            overlay.SetActive(false);
            return pause;
        }

        // ---- shop --------------------------------------------------------------------

        // The shop's header, one row: balance on the left, title centred on the card, close on
        // the right. It used to be two — a left-aligned title with the balance pill under it —
        // which spent 200 units of the tallest panel in the game on two half-empty lines.
        //
        // The two ends carry the same margin so the row is symmetric, and the title is centred
        // on the card rather than in the space left between them: it is the card's name, and a
        // name that sits off-axis because of what happens to be beside it reads as misaligned.
        // The width is what fits between the two ends at that centring — 461 to the balance's
        // 288, less a 30 gap, doubled.
        const float ShopHeaderY = -84f;          // the row's centre, from the card fill's top
        const float ShopHeaderInset = 38f;       // margin at both ends
        const float ShopCloseSize = 104f;
        const float ShopBalanceWidth = 250f;
        const float ShopTitleSize = 72f;
        const float ShopTitleWidth = 286f;
        const float ShopCloseX = ShopHeaderInset + ShopCloseSize * 0.5f;
        const float ShopBalanceX = ShopHeaderInset + ShopBalanceWidth * 0.5f;

        // Under the header with the usual gap between blocks. The close button is the lowest
        // thing in the row, reaching 136 down.
        const float ShopListTop = -170f;

        static ShopPanel BuildShopPanel(Transform parent, TMP_FontAsset font, StorePurchaser purchaser)
        {
            var safe = BuildOverlay(parent, "ShopPanel", 0.7f, out var overlay, out var group);

            // The only card that stretches. A fixed 1500 is right on a 19.5:9 phone and taller
            // than the whole safe area on a 4:3 screen, where it would hang off both ends.
            // Everything inside it is anchored to an edge — header at the top, scroll area
            // stretched, status and restore at the bottom — so the height can be the device's
            // to decide.
            var card = Node("Card", safe);
            var cardRect = RectOf(card);
            cardRect.anchorMin = new Vector2(0.5f, 0f);
            cardRect.anchorMax = new Vector2(0.5f, 1f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.anchoredPosition = Vector2.zero;
            cardRect.sizeDelta = new Vector2(940f, -220f);   // 110 clear of the safe area, top and bottom
            cardRect.localScale = Vector3.one;
            var cardFill = AddSticker(card, UISpriteFactory.Round, Hex("F2F5F9"), 58f, 9f);
            var pop = card.AddComponent<UIPopIn>();
            pop.fadeGroup = group;

            var title = AddText(cardFill.transform, "Title", "Shop", font, ShopTitleSize, Ink);
            title.fontStyle = FontStyles.UpperCase | FontStyles.Bold;
            title.characterSpacing = 4f;
            title.alignment = TextAlignmentOptions.Center;
            // Shrinks rather than wraps. The word is authored on the object, so a longer one
            // can be typed into the inspector without coming back here to widen the box —
            // and the box is as wide as it can be without reaching the balance pill.
            title.enableAutoSizing = true;
            title.fontSizeMin = 40f;
            title.fontSizeMax = ShopTitleSize;
            Place(title.gameObject, new Vector2(0.5f, 1f), new Vector2(0f, ShopHeaderY),
                  new Vector2(ShopTitleWidth, 84f));

            // The balance belongs in the header: every price on this screen is read against it.
            // On the header line rather than under it, so the row reads left to right as
            // what you have, where you are, and the way out.
            var balance = Node("Balance", cardFill.transform);
            Place(balance, new Vector2(0f, 1f), new Vector2(ShopBalanceX, ShopHeaderY),
                  new Vector2(ShopBalanceWidth, 80f));
            var balanceFill = AddSticker(balance, UISpriteFactory.Round, Panel, 40f, 5f);
            balanceFill.raycastTarget = false;
            BuildCoinIcon(balanceFill.transform, new Vector2(-85f, 0f), 54f);
            var balanceText = AddText(balanceFill.transform, "BalanceText", "0", font, 40f, Ink);
            balanceText.fontStyle = FontStyles.Bold;
            balanceText.alignment = TextAlignmentOptions.Left;
            Place(balanceText.gameObject, new Vector2(0.5f, 0.5f), new Vector2(4f, 0f), new Vector2(100f, 52f));
            balanceText.gameObject.AddComponent<UIWalletLabel>();

            var close = BuildIconButton(cardFill.transform, "CloseButton", SkinIcon(UISpriteFactory.IconClose, LayerLabSkin.IconClose), Slate, 104f, 50f);
            Place(close.transform.parent.gameObject, new Vector2(1f, 1f),
                  new Vector2(-ShopCloseX, ShopHeaderY), new Vector2(ShopCloseSize, ShopCloseSize));

            // The list scrolls. It used to be a plain layout group in a fixed rect, which
            // works only while the catalog fits: six products at 176 tall needed 1146 units
            // of a 974-unit rect, so the last rows ran over the status line and the restore
            // button, and a seventh would have left the card altogether. A catalog is exactly
            // the kind of thing that grows, so the fix is scrolling, not a taller card.
            var scroll = Node("ScrollArea", cardFill.transform);
            var scrollRect = RectOf(scroll);
            scrollRect.anchorMin = Vector2.zero;
            scrollRect.anchorMax = Vector2.one;
            scrollRect.pivot = new Vector2(0.5f, 0.5f);
            scrollRect.offsetMin = new Vector2(46f, ShopPanel.ListBottomPlain);
            scrollRect.offsetMax = new Vector2(-46f, ShopListTop);
            // RectMask2D rather than Mask: it clips to the rectangle without a stencil pass
            // and without needing a graphic of its own to mask against.
            scroll.AddComponent<RectMask2D>();

            // A ScrollRect is dragged, and a drag only exists where a raycast landed. The rows
            // set raycastTarget false on every image they own, and RectMask2D is not a graphic,
            // so the only target in the whole list was the price pill: a drag anywhere else —
            // which is most of the list — reached nothing and the shop would not scroll past
            // its fifth product. An invisible image over the viewport is what catches it.
            var catcher = scroll.AddComponent<Image>();
            catcher.color = new Color(0f, 0f, 0f, 0f);
            catcher.raycastTarget = true;

            var list = Node("List", scroll.transform);
            var listRect = RectOf(list);
            listRect.anchorMin = new Vector2(0f, 1f);
            listRect.anchorMax = new Vector2(1f, 1f);
            listRect.pivot = new Vector2(0.5f, 1f);
            listRect.offsetMin = new Vector2(0f, 0f);
            listRect.offsetMax = new Vector2(0f, 0f);

            var layout = list.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 20f;
            layout.padding = new RectOffset(0, 0, 4, 4);
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            // Without this the content rect keeps whatever height it was built with and the
            // scroll has nothing to scroll.
            var fitter = list.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroller = scroll.AddComponent<ScrollRect>();
            scroller.content = listRect;
            scroller.viewport = scrollRect;
            scroller.horizontal = false;
            scroller.vertical = true;
            scroller.movementType = ScrollRect.MovementType.Elastic;
            scroller.elasticity = 0.1f;
            scroller.scrollSensitivity = 30f;

            var template = BuildShopRow(list.transform, font);

            var status = AddText(cardFill.transform, "Status", string.Empty, font, 32f, Slate);
            status.fontStyle = FontStyles.Bold;
            Place(status.gameObject, new Vector2(0.5f, 0f), new Vector2(0f, ShopPanel.StatusCentre), new Vector2(820f, ShopPanel.StatusHeight));
            status.gameObject.SetActive(false);

            var restore = BuildPillButton(cardFill.transform, "RestoreButton", "Restore", font,
                                          Hex("DCE3EC"), Slate, new Vector2(340f, ShopPanel.RestoreHeight), out _);
            // Centred. The 40 it used to carry put it visibly off-axis with nothing on
            // either side of it to balance against.
            Place(restore.transform.parent.gameObject, new Vector2(0.5f, 0f), new Vector2(0f, ShopPanel.RestoreCentre), new Vector2(340f, ShopPanel.RestoreHeight));

            var shop = overlay.AddComponent<ShopPanel>();
            shop.purchaser = purchaser;
            shop.root = overlay;
            shop.listParent = listRect;
            shop.rowTemplate = template;
            shop.statusLabel = status;
            shop.scrollArea = scrollRect;
            shop.closeButton = close;
            shop.restoreButton = restore;

            overlay.SetActive(false);
            return shop;
        }

        // One row, left inactive: ShopPanel clones it per product. Its parts are handed to a
        // ShopRowView rather than looked up by name at runtime, so renaming anything here
        // breaks the build rather than the shop.
        static ShopRowView BuildShopRow(Transform parent, TMP_FontAsset font)
        {
            // The heights and every vertical offset belong to ShopRowView, because only the
            // runtime knows whether a product carries a badge — a badged row is 200 and a
            // plain one 156, the difference being the band the badge sits in. The builder
            // still owns the widths, the colours and the wiring.
            //
            // Two things the numbers there are defending, both measured: a badge that
            // straddled the row's top edge sat 32 units into the 20-unit gap above, which put
            // it on the row before it; and the title and description overlapped by 3 until
            // their centres were 60 apart, which is what a 38pt line over a 26pt one needs.
            const float height = ShopRowView.BadgedHeight;

            var row = Node("RowTemplate", parent);
            var rowRect = RectOf(row);
            rowRect.sizeDelta = new Vector2(0f, height);
            var element = row.AddComponent<LayoutElement>();
            element.preferredHeight = height;
            element.minHeight = height;

            var fill = AddSticker(row, UISpriteFactory.Round, Card, 42f, 6f);
            fill.raycastTarget = false;
            var rowImage = row.GetComponent<Image>();
            if (rowImage != null) rowImage.raycastTarget = false;

            BuildCoinIcon(fill.transform, new Vector2(-322f, 0f), 80f);

            // The title and its description are one label pair, so they sit closer to each
            // other than to anything else on the row — that closeness is what says they
            // belong together.
            var title = AddText(fill.transform, "Title", "Coins", font, 38f, Ink);
            title.fontStyle = FontStyles.Bold;
            title.alignment = TextAlignmentOptions.Left;
            Place(title.gameObject, new Vector2(0.5f, 0.5f), new Vector2(-66f, 0f), new Vector2(380f, 44f));

            var description = AddText(fill.transform, "Description", "", font, 26f, Muted);
            description.alignment = TextAlignmentOptions.Left;
            Place(description.gameObject, new Vector2(0.5f, 0.5f), new Vector2(-66f, 0f), new Vector2(380f, 36f));

            var buy = BuildPillButton(fill.transform, "BuyButton", "$0.99", font, Amber, Ink,
                                      new Vector2(230f, 92f), out var priceText, false);
            Place(buy.transform.parent.gameObject, new Vector2(1f, 0.5f), new Vector2(-134f, 0f), new Vector2(230f, 92f));

            var owned = Node("OwnedTag", fill.transform);
            Place(owned, new Vector2(1f, 0.5f), new Vector2(-134f, 0f), new Vector2(230f, 92f));
            var ownedFill = AddSticker(owned, UISpriteFactory.Round, Hex("DCE3EC"), 48f, 5f);
            ownedFill.raycastTarget = false;
            var ownedImage = owned.GetComponent<Image>();
            if (ownedImage != null) ownedImage.raycastTarget = false;
            var ownedText = AddText(ownedFill.transform, "Text", "Owned", font, 32f, Slate);
            ownedText.fontStyle = FontStyles.Bold;
            Stretch(ownedText.gameObject, 8f);
            owned.SetActive(false);

            var badge = Node("Badge", fill.transform);
            Place(badge, new Vector2(0f, 1f), new Vector2(126f, -(ShopRowView.BadgeTopGap + ShopRowView.BadgeHeight * 0.5f - ShopRowView.Outline)), new Vector2(196f, ShopRowView.BadgeHeight));
            var badgeFill = AddSticker(badge, UISpriteFactory.Round, Coral, 26f, 4f);
            badgeFill.raycastTarget = false;
            var badgeImage = badge.GetComponent<Image>();
            if (badgeImage != null) badgeImage.raycastTarget = false;
            var badgeText = AddText(badgeFill.transform, "Text", "BEST VALUE", font, 24f, Panel);
            badgeText.fontStyle = FontStyles.UpperCase | FontStyles.Bold;
            badgeText.characterSpacing = 2f;
            Stretch(badgeText.gameObject, 4f);
            badge.SetActive(false);

            var view = row.AddComponent<ShopRowView>();
            view.title = title;
            view.description = description;
            view.price = priceText;
            view.buyButton = buy;
            view.badge = badge;
            view.badgeLabel = badgeText;
            view.ownedTag = owned;
            view.coinIcon = (RectTransform)fill.transform.Find("Coin");
            view.buySlot = (RectTransform)buy.transform.parent;

            // The template is built with no badge showing, so it is laid out that way. Every
            // clone is laid out again by ShopPanel the moment it knows its product.
            view.ApplyLayout(badged: false);

            row.SetActive(false);
            return view;
        }

        // A rounded track with a fill that grows from its left edge. The caller positions it;
        // the size is passed in as well because the corner radius is derived from the height.
        static UIProgressBar BuildProgressBar(Transform parent, Vector2 size, Color track, Color fill,
                                              float outline = 5f)
        {
            var trackGO = Node("ProgressTrack", parent);
            Place(trackGO, new Vector2(0.5f, 0.5f), Vector2.zero, size);
            var trackImage = AddSticker(trackGO, UISpriteFactory.Round, track, size.y * 0.5f, outline);
            if (LayerLabSkin.Available)
            {
                trackImage.sprite = LayerLabSkin.SliderTrack;
                trackImage.color = Color.white;
                FitSkin(trackImage, size, size.y * 0.5f);
            }

            var fillGO = Node("ProgressFill", trackImage.transform);
            var fillRect = RectOf(fillGO);
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.pivot = new Vector2(0f, 0.5f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            // Part-filled so the bar reads as a bar in the editor; whatever drives it
            // overwrites this on its first frame.
            fillRect.anchorMax = new Vector2(0.45f, 1f);
            var fillImage = AddPanel(fillGO, UISpriteFactory.Round, fill, Mathf.Max(4f, size.y * 0.4f));
            if (LayerLabSkin.Available)
            {
                fillImage.sprite = LayerLabSkin.SliderFill;
                fillImage.color = Color.white;
                FitSkin(fillImage, size, size.y * 0.5f);
                fillRect.offsetMin = new Vector2(4f, 4f);
                fillRect.offsetMax = new Vector2(-4f, -4f);
            }

            var bar = trackGO.AddComponent<UIProgressBar>();
            bar.fill = fillRect;
            return bar;
        }

        // ---- widgets ----------------------------------------------------------------

        // Buttons are wrapped in a slot holding the drop shadow, because a child of the
        // button would draw on top of it and a sibling would move independently.
        static Button BuildIconButton(Transform parent, string name, Sprite icon, Color colour,
                                      float diameter, float iconSize, string label = null,
                                      TMP_FontAsset font = null)
        {
            var slot = Node(name + "Slot", parent);
            Place(slot, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(diameter, diameter));

            // The caption hangs below the slot on its own chip rather than as bare text: it
            // sits over the scene, which is pale concrete in one place and dark belt in
            // another, and plain text would disappear into one of them.
            if (!string.IsNullOrEmpty(label))
            {
                var chip = Node("Label", slot.transform);
                Place(chip, new Vector2(0.5f, 0f), new Vector2(0f, -Lip - 8f), new Vector2(diameter + 26f, 54f));
                RectOf(chip).pivot = new Vector2(0.5f, 1f);
                // The pack captions a button with bare outlined text under it, as its pause
                // screen does; the outline carries it over pale floor and dark belt alike.
                Transform captionParent = chip.transform;
                if (!LayerLabSkin.Available)
                {
                    var chipFill = AddSticker(chip, UISpriteFactory.Round, Card, 27f, 5f);
                    chipFill.raycastTarget = false;
                    captionParent = chipFill.transform;
                }

                var caption = AddText(captionParent, "Text", label, font, LayerLabSkin.Available ? 34f : 28f, Ink);
                caption.fontStyle = FontStyles.UpperCase | FontStyles.Bold;
                caption.characterSpacing = 3f;
                Stretch(caption.gameObject, 4f);
            }

            // The lip: the same outline colour, sitting a little below, so the button reads
            // as a thing with thickness rather than a disc with a drop shadow. It is what
            // separates a toy button from a flat one, and it is why the press animation
            // squashing the button downwards looks like it is being pushed in.
            if (!LayerLabSkin.Available)
            {
                var lip = Node("Lip", slot.transform);
                Place(lip, new Vector2(0.5f, 0.5f), new Vector2(0f, -Lip), new Vector2(diameter, diameter));
                var lipImage = lip.AddComponent<Image>();
                lipImage.sprite = UISpriteFactory.Circle;
                lipImage.color = Ink;
                lipImage.raycastTarget = false;
            }

            var go = Node(name, slot.transform);
            Place(go, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(diameter, diameter));
            var body = AddSticker(go, UISpriteFactory.Circle, colour, diameter * 0.5f);

            var iconGO = Node("Icon", body.transform);
            Place(iconGO, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(iconSize, iconSize));
            var iconImage = iconGO.AddComponent<Image>();
            iconImage.sprite = icon;
            iconImage.color = Panel;
            iconImage.raycastTarget = false;

            return Finish(go, body);
        }

        // priceRoom reserves width at the right end for a price chip, and shifts the label
        // left by half of it so the two are centred in what is left rather than fighting over
        // the middle.
        static Button BuildPillButton(Transform parent, string name, string label, TMP_FontAsset font,
                                      Color colour, Color textColour, Vector2 size,
                                      out TMP_Text labelText, bool chevron = true, float priceRoom = 0f)
        {
            // The pack's buttons carry a word and nothing else; a chevron reads as clutter on them.
            if (LayerLabSkin.Available) chevron = false;

            var slot = Node(name + "Slot", parent);
            Place(slot, new Vector2(0.5f, 0.5f), Vector2.zero, size);

            var lip = Node("Lip", slot.transform);
            Place(lip, new Vector2(0.5f, 0.5f), new Vector2(0f, -Lip), size);
            AddPanel(lip, UISpriteFactory.Round, Ink, size.y * 0.5f).raycastTarget = false;

            var go = Node(name, slot.transform);
            Place(go, new Vector2(0.5f, 0.5f), Vector2.zero, size);
            var body = AddSticker(go, UISpriteFactory.Round, colour, size.y * 0.5f);

            float fontSize = Mathf.Clamp(size.y * 0.36f, 28f, 54f);
            var text = AddText(body.transform, "Label", label, font, fontSize, textColour);
            text.fontStyle = FontStyles.UpperCase | FontStyles.Bold;
            text.characterSpacing = 4f;

            // With nothing at the right end the label owns the whole pill and centres; a
            // chevron or a price chip takes its corner and the label has to give it room.
            //
            // For a price chip this centres the label in the free strip to the chip's left,
            // not on the whole pill — the chip only occupies the right corner, so centring on
            // the full width pushes the label under it (2026-09-06, reported against the lose
            // panel's rescue buttons). The chip's own placement is AddPriceChip's, mirrored
            // here to find its left edge.
            float reserved, shift;
            if (chevron)
            {
                reserved = 120f;
                shift = -18f;
            }
            else if (priceRoom > 0f)
            {
                const float gap = 28f;      // clear air between the label and the chip
                const float sidePad = 24f;  // clear air from the button's own left edge
                float chipLeft = size.x * 0.5f - priceRoom - PriceChipMargin;
                float zoneLeft = -size.x * 0.5f + sidePad;
                float zoneRight = chipLeft - gap;
                reserved = size.x - Mathf.Max(60f, zoneRight - zoneLeft);
                shift = (zoneLeft + zoneRight) * 0.5f;
            }
            else
            {
                reserved = 40f;
                shift = 0f;
            }
            Place(text.gameObject, new Vector2(0.5f, 0.5f), new Vector2(shift, 0f),
                  new Vector2(size.x - reserved, size.y));

            // A priced label is data-driven — "+5 taps" and "Shuffle +3" come out of
            // EconomyConfig and a balance pass can make them longer — while the chip beside it
            // is fixed. Auto-sizing makes the text fit the room it is given, instead of the
            // room silently deciding what the wording is allowed to be.
            if (priceRoom > 0f)
            {
                text.enableAutoSizing = true;
                text.fontSizeMin = 26f;
                text.fontSizeMax = fontSize;
            }

            labelText = text;

            if (chevron)
            {
                var chevronGO = Node("Chevron", body.transform);
                Place(chevronGO, new Vector2(1f, 0.5f), new Vector2(-58f, 0f), new Vector2(52f, 52f));
                var chevronImage = chevronGO.AddComponent<Image>();
                chevronImage.sprite = LayerLabSkin.Available ? LayerLabSkin.IconChevron : UISpriteFactory.IconChevron;
                chevronImage.color = LayerLabSkin.Available ? Color.white : Alpha(textColour, 0.85f);
                chevronImage.raycastTarget = false;
            }

            return Finish(go, body);
        }

        static Button Finish(GameObject go, Image target)
        {
            var button = go.AddComponent<Button>();
            button.targetGraphic = target;
            var colours = button.colors;
            colours.highlightedColor = new Color(1.05f, 1.05f, 1.05f, 1f);
            colours.pressedColor = new Color(0.88f, 0.88f, 0.88f, 1f);
            colours.selectedColor = Color.white;
            colours.fadeDuration = 0.08f;
            button.colors = colours;
            go.AddComponent<UIButtonFeedback>();
            return button;
        }

        // ---- plumbing ---------------------------------------------------------------

        static GameObject Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Build Game UI");
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;
            return go;
        }

        static RectTransform RectOf(GameObject go) => (RectTransform)go.transform;

        // Turns a container into a row that measures its children and centres them as one
        // block. For a chip whose content changes width -- an icon beside a number -- this is
        // the only layout that stays centred, because the width is not known when it is built.
        static HorizontalLayoutGroup CentreRow(GameObject go, float spacing)
        {
            var row = go.GetComponent<HorizontalLayoutGroup>();
            if (row == null) row = Undo.AddComponent<HorizontalLayoutGroup>(go);

            Undo.RecordObject(row, "Centre row");
            row.childAlignment = TextAnchor.MiddleCenter;
            row.spacing = spacing;
            // Widths are controlled so each child is given exactly what it asks for -- a text
            // asks for its own ink -- and heights are not, so the authored sizes stand.
            row.childControlWidth = true;
            row.childControlHeight = false;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;
            row.childScaleWidth = false;
            row.childScaleHeight = false;
            return row;
        }

        // An Image has no preferred size of its own, so a layout group would allocate it
        // nothing. This is how a sprite keeps its size inside one.
        static void AddFixedSize(Transform target, float width, float height)
        {
            if (target == null) return;

            var element = target.GetComponent<LayoutElement>();
            if (element == null) element = Undo.AddComponent<LayoutElement>(target.gameObject);

            Undo.RecordObject(element, "Centre row");
            element.preferredWidth = width;
            element.preferredHeight = height;
            element.minWidth = width;
        }

        static void Place(GameObject go, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var rect = RectOf(go);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;
        }

        static void Stretch(GameObject go, float padding = 0f)
        {
            var rect = RectOf(go);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(padding, padding);
            rect.offsetMax = new Vector2(-padding, -padding);
            rect.localScale = Vector3.one;
        }

        // A bright bar that sweeps across a button every few seconds.
        //
        // Masked by the fill's own rounded sprite rather than by a RectMask2D: a rectangular
        // clip would let the bar square off the button's corners. That costs a stencil pass
        // per masked button, which is why only the three buttons a player is meant to reach
        // for get one, and nothing on the win card's furniture does.
        static void AddShine(Image fill, float width, float alpha = 0.34f)
        {
            var mask = fill.gameObject.GetComponent<Mask>();
            if (mask == null) mask = fill.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = true;

            var shine = Node("Shine", fill.transform);
            var rect = RectOf(shine);
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, 90f);      // taller than the button, so the
            rect.localRotation = Quaternion.Euler(0f, 0f, 18f);   // tilt still covers it

            var image = shine.AddComponent<Image>();
            image.color = new Color(1f, 1f, 1f, alpha);
            image.raycastTarget = false;

            shine.AddComponent<UIShine>();
        }

        // An outlined shape: the object itself becomes the dark outline and carries a child
        // holding the fill, inset by the outline's width.
        //
        // It has to be a child rather than a sibling behind, because a UI child always draws
        // over its parent and there is no way to put a graphic *under* one. Returning the
        // fill image means callers keep pointing their button's target graphic at the part
        // that changes colour when pressed.
        //
        // Skinned from the Layer Lab pack, which draws its own edges and depth: a pale card
        // becomes the pack's navy popup (or a navy chip when small), a coloured one its chunky
        // button in the nearest colour, a disc its round or hexagonal button. There is no
        // outline to inset from, so the object carries the one image and content goes inside it.
        static Image AddSticker(GameObject go, Sprite sprite, Color colour, float cornerRadius,
                                float outline = Outline)
        {
            if (!LayerLabSkin.Available)
            {
                AddPanel(go, sprite, Ink, cornerRadius).raycastTarget = true;

                var fill = Node("Fill", go.transform);
                Stretch(fill, outline);
                var inner = AddPanel(fill, sprite, colour, Mathf.Max(1f, cornerRadius - outline));
                inner.raycastTarget = false;
                return inner;
            }

            var size = RectOf(go).rect.size;
            Sprite skin;
            Color tint = Color.white;

            if (sprite == UISpriteFactory.Circle)
            {
                if (LayerLabSkin.IsPale(colour) || LayerLabSkin.IsDark(colour) || colour == Slate || colour == Muted)
                    skin = LayerLabSkin.CircleNavy;
                else if (colour == Coral) skin = LayerLabSkin.HexRed;
                else if (colour == Blue || colour == Grape) skin = LayerLabSkin.HexBlue;
                else { skin = LayerLabSkin.CircleWhite; tint = colour; }
            }
            else if (LayerLabSkin.IsPale(colour))
            {
                bool popup = size.x >= 500f && size.y >= 300f;
                skin = popup ? LayerLabSkin.PopupNavy : LayerLabSkin.ChipFrame;
                if (!popup) tint = LayerLabSkin.Navy;
            }
            else if (LayerLabSkin.IsDark(colour))
            {
                skin = LayerLabSkin.ChipFrame;
                tint = LayerLabSkin.NavyDeep;
            }
            else skin = LayerLabSkin.Button(colour);

            var image = go.AddComponent<Image>();
            image.sprite = skin;
            image.color = tint;
            FitSkin(image, size, cornerRadius);
            image.raycastTarget = true;
            return image;
        }

        // Sliced when the pack slices it, with the slice scaled so its borders fit the object
        // and its corners come out near the radius the layout asked for; whole and in
        // proportion when it does not (the round and hexagonal buttons).
        static void FitSkin(Image image, Vector2 size, float cornerRadius)
        {
            var sprite = image.sprite;
            if (sprite == null) return;

            Vector4 b = sprite.border;
            if (b == Vector4.zero)
            {
                image.type = Image.Type.Simple;
                image.preserveAspect = true;
                return;
            }

            image.type = Image.Type.Sliced;
            float fit = 1f;
            if (size.x > 1f) fit = Mathf.Max(fit, (b.x + b.z) / size.x);
            if (size.y > 1f) fit = Mathf.Max(fit, (b.y + b.w) / size.y);
            float corner = Mathf.Min(b.x, b.z) / Mathf.Max(1f, cornerRadius);
            image.pixelsPerUnitMultiplier = Mathf.Max(fit, Mathf.Min(corner, 3f), 0.01f);
        }

        // The rounded sprite is a single 48px-radius asset; the radius seen on screen is set
        // by scaling its slice borders, which is what pixelsPerUnitMultiplier does.
        //
        // With the skin on, a lip is dropped (the pack's buttons carry their own depth) and a
        // dark or pale flat shape becomes a navy chip, so trays and slots match the panels.
        static Image AddPanel(GameObject go, Sprite sprite, Color colour, float cornerRadius)
        {
            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = colour;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = Mathf.Max(0.01f, UISpriteFactory.RoundRadius / Mathf.Max(1f, cornerRadius));

            if (LayerLabSkin.Available && sprite == UISpriteFactory.Round)
            {
                if (go.name == "Lip") image.enabled = false;
                else if (colour.a > 0.99f && (LayerLabSkin.IsDark(colour) || LayerLabSkin.IsPale(colour)))
                {
                    image.sprite = LayerLabSkin.ChipFrame;
                    image.color = LayerLabSkin.IsDark(colour) ? LayerLabSkin.NavyDeep : LayerLabSkin.Navy;
                    FitSkin(image, RectOf(go).rect.size, cornerRadius);
                }
            }
            return image;
        }

        static TMP_Text AddText(Transform parent, string name, string content, TMP_FontAsset font,
                                float size, Color colour)
        {
            var go = Node(name, parent);
            var text = go.AddComponent<TextMeshProUGUI>();
            if (font != null) text.font = font;
            text.text = content;
            text.fontSize = size;
            text.color = LayerLabSkin.Available ? SkinText(colour) : colour;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            text.overflowMode = TextOverflowModes.Overflow;
            return text;
        }

        // Everything the pack puts text on is navy or a saturated button, so the dark inks the
        // white stickers used become white, and the greys a pale blue. Accents stay as they are.
        // Lilita One is a heavy face with its outline baked into the material: faux bold on top
        // of it smears the outline, and the tracking the condensed Oswald needed spreads it out
        // until two words no longer fit a button. Run over the finished canvas so every builder,
        // old migrations included, comes out the same.
        static void SkinTextPass(Transform root)
        {
            if (!LayerLabSkin.Available) return;
            foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                Undo.RecordObject(text, "Skin text");
                text.fontStyle &= ~FontStyles.Bold;
                text.characterSpacing = 0f;
                if (LayerLabSkin.Font != null && text.font != LayerLabSkin.Font) text.font = LayerLabSkin.Font;
            }
        }

        static Sprite SkinIcon(Sprite generated, Sprite pack) =>
            LayerLabSkin.Available && pack != null ? pack : generated;

        static Color SkinText(Color c)
        {
            if (c == Muted) return LayerLabSkin.TextMuted;
            Color.RGBToHSV(c, out _, out float s, out float v);
            if (v < 0.7f || s < 0.25f) return Color.white;
            return c;
        }

        static Canvas EnsureCanvas(GameManager gm)
        {
            var bindings = Object.FindFirstObjectByType<UIBindings>(FindObjectsInactive.Include);
            var canvas = bindings != null ? bindings.GetComponentInParent<Canvas>() : null;

            if (canvas == null)
            {
                foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (c.name != CanvasName) continue;
                    canvas = c;
                    break;
                }
            }

            if (canvas == null)
            {
                var go = new GameObject(CanvasName, typeof(RectTransform), typeof(Canvas),
                                        typeof(CanvasScaler), typeof(GraphicRaycaster));
                Undo.RegisterCreatedObjectUndo(go, "Build Game UI");
                // Sit next to the rest of the rig rather than at the scene root, so moving
                // the game into another scene still takes the HUD with it.
                if (gm.transform.parent != null) go.transform.SetParent(gm.transform.parent, false);
                canvas = go.GetComponent<Canvas>();
            }

            Undo.RecordObject(canvas, "Build Game UI");
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = Undo.AddComponent<CanvasScaler>(canvas.gameObject);
            Undo.RecordObject(scaler, "Build Game UI");
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = Reference;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            // Width-matched: a taller phone gets more room top and bottom rather than
            // smaller buttons, which is what the fixed-size board wants too.
            scaler.matchWidthOrHeight = 0f;

            // Which is right for phones and wrong for anything shorter than the reference —
            // an iPad matched on width has 1440 units of height for a layout drawn for 1920.
            // The fitter slides the match across as the shape changes and leaves every phone
            // exactly where the line above put it.
            if (canvas.GetComponent<CanvasScalerFitter>() == null)
                Undo.AddComponent<CanvasScalerFitter>(canvas.gameObject);

            if (canvas.GetComponent<GraphicRaycaster>() == null)
                Undo.AddComponent<GraphicRaycaster>(canvas.gameObject);

            return canvas;
        }

        static void EnsureEventSystem()
        {
            var existing = Object.FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include);
            if (existing != null)
            {
                if (existing.GetComponent<BaseInputModule>() == null)
                    Undo.AddComponent<InputSystemUIInputModule>(existing.gameObject);
                return;
            }

            // Without one of these no button in the scene responds at all, which is exactly
            // the state this scene was in.
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            Undo.RegisterCreatedObjectUndo(go, "Build Game UI");
            go.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }

        static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
                Undo.DestroyObjectImmediate(parent.GetChild(i).gameObject);
        }

        // Oswald is the only display face in the project; TMP needs it as a font asset, and
        // making one is cheaper than asking anyone to click through the font asset creator.
        static TMP_FontAsset EnsureFont()
        {
            const string assetPath = FontDir + "/Oswald SDF.asset";
            if (LayerLabSkin.Available && LayerLabSkin.Font != null) return LayerLabSkin.Font;

            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (existing != null) return existing;

            if (!Directory.Exists(FontDir)) Directory.CreateDirectory(FontDir);

            string localTtf = FontDir + "/Oswald-Regular.ttf";
            if (AssetDatabase.LoadAssetAtPath<Font>(localTtf) == null)
            {
                if (AssetDatabase.LoadAssetAtPath<Font>(FontSource) == null) return TMP_Settings.defaultFontAsset;
                AssetDatabase.CopyAsset(FontSource, localTtf);
                AssetDatabase.ImportAsset(localTtf, ImportAssetOptions.ForceUpdate);
            }

            var source = AssetDatabase.LoadAssetAtPath<Font>(localTtf);
            if (source == null) return TMP_Settings.defaultFontAsset;

            TMP_FontAsset created;
            try
            {
                created = TMP_FontAsset.CreateFontAsset(source, 90, 9, GlyphRenderMode.SDFAA,
                                                        1024, 1024, AtlasPopulationMode.Dynamic, true);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("Falling back to the default TMP font: " + e.Message);
                return TMP_Settings.defaultFontAsset;
            }

            if (created == null) return TMP_Settings.defaultFontAsset;

            created.name = "Oswald SDF";
            AssetDatabase.CreateAsset(created, assetPath);
            if (created.atlasTextures != null && created.atlasTextures.Length > 0)
            {
                created.atlasTextures[0].name = "Oswald Atlas";
                AssetDatabase.AddObjectToAsset(created.atlasTextures[0], created);
            }
            if (created.material != null)
            {
                created.material.name = "Oswald SDF Material";
                AssetDatabase.AddObjectToAsset(created.material, created);
            }
            AssetDatabase.SaveAssets();
            return created;
        }

        // Dropped in as a plain texture, which UI.Image cannot use directly — a raw PNG
        // import defaults to Default/None on a 3D-template project, and LoadAssetAtPath
        // <Sprite> on one of those returns null with no error, which reads as a missing file
        // rather than a missing import setting.
        const string LogoPath = "Assets/_Game/Logo.png";

        static Sprite EnsureLogoSprite()
        {
            var importer = AssetImporter.GetAtPath(LogoPath) as TextureImporter;
            if (importer == null) return null;

            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(LogoPath);
        }

        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }

        static Color Alpha(Color c, float a) => new Color(c.r, c.g, c.b, a);
    }
}
