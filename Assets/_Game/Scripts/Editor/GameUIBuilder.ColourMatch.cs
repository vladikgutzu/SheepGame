using System.Collections.Generic;
using SheepGame.Core;
using SheepGame.Core.Meta;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace SheepGame.EditorTools
{
    // The colour rules' half of the HUD: the cart at the top, the queue at the bottom, the
    // suitcases that fly between them, the tutorial hand, and the gates that keep boosters
    // off screen until they are handed over.
    //
    // Additive, like the other migrations: Build Game UI would draw the whole canvas again and
    // lose what has been tuned in the scene by hand. Everything it builds it first removes, so
    // running it twice leaves one of each.
    public static partial class GameUIBuilder
    {
        const string CartCardName = "CartCard";
        const string QueueTrayName = "QueueTray";
        const string TokenLayerName = "TokenLayer";
        const string HandName = "TutorialHand";
        const string AdsConfigPath = "Assets/_Game/Data/AdsConfig.asset";

        [MenuItem("SheepGame/Add Colour Match HUD")]
        public static void AddColourMatchHud()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[HUD] Stop play mode first -- changes made while playing are " +
                                 "discarded when it ends. Nothing was changed.");
                return;
            }

            var gm = Object.FindFirstObjectByType<GameManager>(FindObjectsInactive.Include);
            var bindings = Object.FindFirstObjectByType<UIBindings>(FindObjectsInactive.Include);
            if (gm == null || bindings == null)
            {
                Debug.LogWarning("[HUD] Needs a GameManager and a UIBindings in the open scene. Nothing was changed.");
                return;
            }

            var canvas = bindings.GetComponentInParent<Canvas>();
            Transform hud = null;
            foreach (Transform child in canvas.transform)
                if (child.GetComponent<SafeAreaFitter>() != null) { hud = child; break; }
            if (hud == null)
            {
                Debug.LogWarning("[HUD] No safe area under the canvas. Nothing was changed.");
                return;
            }

            WireColourMatch(gm, bindings, canvas.transform, hud, EnsureFont());

            EditorSceneManager.MarkSceneDirty(gm.gameObject.scene);
            Selection.activeGameObject = gm.colourHud != null ? gm.colourHud.gameObject : gm.gameObject;
            Debug.Log("[HUD] Added the cart, the queue, the tutorial hand and the unlock gates, " +
                      "and wired interstitials. Save the scene to keep it.");
        }

        // Shared by the migration and by Build Game UI, so a full rebuild does not lose any of it.
        static void WireColourMatch(GameManager gm, UIBindings bindings, Transform canvas, Transform hud,
                                    TMP_FontAsset font)
        {
            foreach (var name in new[] { CartCardName, QueueTrayName, TokenLayerName, HandName })
            {
                var old = hud.Find(name);
                if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            }
            var oldGate = hud.GetComponent<UIUnlockGate>();
            if (oldGate != null) Undo.DestroyObjectImmediate(oldGate);

            var boosterRow = hud.Find("Boosters") as RectTransform;

            var view = hud.gameObject.GetComponent<UIColourMatchHud>();
            if (view == null) view = Undo.AddComponent<UIColourMatchHud>(hud.gameObject);
            Undo.RecordObject(view, "Add Colour Match HUD");

            BuildCartCard(hud, font, view);
            BuildQueueTray(hud, view, boosterRow);
            BuildTokenLayer(hud, view);
            var hand = BuildTutorialHand(hud);
            EditorUtility.SetDirty(view);

            // Room for the cart under the level card: the clock drops below it, and the remove
            // booster's "tap a piece" chip rises above the queue.
            var timer = hud.Find("TimerPillSlot") as RectTransform;
            if (timer != null)
            {
                Undo.RecordObject(timer, "Add Colour Match HUD");
                timer.anchoredPosition = new Vector2(timer.anchoredPosition.x, -510f);
            }

            var removeHint = hud.Find("RemoveHint") as RectTransform;
            if (removeHint != null)
            {
                Undo.RecordObject(removeHint, "Add Colour Match HUD");
                removeHint.anchoredPosition = new Vector2(removeHint.anchoredPosition.x, 600f);
            }

            // Boosters and the coin balance wait for their level.
            var gate = Undo.AddComponent<UIUnlockGate>(hud.gameObject);
            gate.gameManager = gm;
            var entries = new List<UIUnlockGate.Entry>();
            AddGate(entries, ItemIds.Remove, bindings.removeButton);
            AddGate(entries, ItemIds.Shuffle, bindings.shuffleButton);
            AddGate(entries, ItemIds.Hint, bindings.hintButton);
            var coinSlot = hud.Find("CoinPillSlot");
            if (coinSlot != null) entries.Add(new UIUnlockGate.Entry { key = UIUnlockGate.Coins, target = coinSlot.gameObject });
            gate.entries = entries.ToArray();
            EditorUtility.SetDirty(gate);

            var menu = Object.FindFirstObjectByType<MainMenuPanel>(FindObjectsInactive.Include);
            if (menu != null && menu.dailyButton != null)
            {
                var menuSafe = menu.dailyButton.transform.parent.parent;
                var menuGate = menuSafe.GetComponent<UIUnlockGate>();
                if (menuGate == null) menuGate = Undo.AddComponent<UIUnlockGate>(menuSafe.gameObject);
                menuGate.gameManager = gm;
                menuGate.entries = new[]
                {
                    new UIUnlockGate.Entry { key = UIUnlockGate.Daily, target = menu.dailyButton.transform.parent.gameObject }
                };
                EditorUtility.SetDirty(menuGate);
            }

            // The banner learns to show the booster it is handing over.
            var banner = gm.modeBanner;
            if (banner != null && banner.badgeIcon == null)
            {
                foreach (var image in banner.GetComponentsInChildren<Image>(true))
                {
                    if (image.name != "Clock") continue;
                    Undo.RecordObject(banner, "Add Colour Match HUD");
                    banner.badgeIcon = image;
                    EditorUtility.SetDirty(banner);
                    break;
                }
            }

            // The lose panel says what ran out.
            TMP_Text loseTitle = null, loseSubtitle = null;
            if (gm.losePanel != null)
            {
                foreach (var text in gm.losePanel.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (text.name == "Title") loseTitle = text;
                    else if (text.name == "Subtitle") loseSubtitle = text;
                }
                if (loseTitle != null) { Undo.RecordObject(loseTitle, "Add Colour Match HUD"); loseTitle.text = gm.queueFullTitle; }
                if (loseSubtitle != null) { Undo.RecordObject(loseSubtitle, "Add Colour Match HUD"); loseSubtitle.text = gm.queueFullSubtitle; }
            }

            // Interstitials.
            var adsConfig = AssetDatabase.LoadAssetAtPath<AdsConfig>(AdsConfigPath);
            if (adsConfig == null)
            {
                adsConfig = ScriptableObject.CreateInstance<AdsConfig>();
                AssetDatabase.CreateAsset(adsConfig, AdsConfigPath);
                AssetDatabase.SaveAssets();
            }
            var ads = gm.GetComponent<AdsManager>();
            if (ads == null) ads = Undo.AddComponent<AdsManager>(gm.gameObject);
            Undo.RecordObject(ads, "Add Colour Match HUD");
            ads.config = adsConfig;
            EditorUtility.SetDirty(ads);

            Undo.RecordObject(gm, "Add Colour Match HUD");
            gm.colourHud = view;
            gm.tutorialHand = hand;
            gm.uiBindings = bindings;
            gm.ads = ads;
            gm.loseTitleLabel = loseTitle;
            gm.loseSubtitleLabel = loseSubtitle;
            EditorUtility.SetDirty(gm);
        }

        static void AddGate(List<UIUnlockGate.Entry> entries, string key, Button button)
        {
            if (button == null) return;
            entries.Add(new UIUnlockGate.Entry { key = key, target = button.transform.parent.gameObject });
        }

        // ---- the cart --------------------------------------------------------------------

        // Under the level card, which ends 244 down, with the usual 30 between blocks. The card
        // is the colour of the cart; the three seats are pale so a case of the same colour
        // still shows against it.
        static void BuildCartCard(Transform hud, TMP_FontAsset font, UIColourMatchHud view)
        {
            var size = new Vector2(640f, 176f);

            var slot = Node(CartCardName, hud);
            Place(slot, new Vector2(0.5f, 1f), new Vector2(0f, -274f - size.y * 0.5f), size);

            var lip = Node("Lip", slot.transform);
            Place(lip, new Vector2(0.5f, 0.5f), new Vector2(0f, -Lip), size);
            AddPanel(lip, UISpriteFactory.Round, Ink, 44f).raycastTarget = false;

            var card = Node("Card", slot.transform);
            Place(card, new Vector2(0.5f, 0.5f), Vector2.zero, size);
            // Takes the taps that land on it, so a press on the cart never reaches a case behind.
            var fill = AddSticker(card, UISpriteFactory.Round, Blue, 44f);

            var seats = new RectTransform[3];
            for (int i = 0; i < seats.Length; i++)
            {
                var seat = Node("Seat" + i, fill.transform);
                Place(seat, new Vector2(0.5f, 0.5f), new Vector2(-196f + i * 132f, 0f), new Vector2(118f, 118f));
                var back = AddPanel(seat, UISpriteFactory.Round, Alpha(Panel, 0.55f), 26f);
                back.raycastTarget = false;
                seats[i] = RectOf(seat);
            }

            var done = Node("Done", fill.transform);
            Place(done, new Vector2(0.5f, 0.5f), new Vector2(-64f, 0f), new Vector2(110f, 110f));
            var doneImage = done.AddComponent<Image>();
            doneImage.sprite = SkinIcon(UISpriteFactory.IconCheck, LayerLabSkin.IconCheck);
            doneImage.color = Panel;
            doneImage.raycastTarget = false;
            done.SetActive(false);

            // What comes next, so a case parked now can be parked on purpose.
            var nextLabel = AddText(fill.transform, "NextLabel", "NEXT", font, 26f, Alpha(Panel, 0.9f));
            nextLabel.fontStyle = FontStyles.UpperCase | FontStyles.Bold;
            nextLabel.characterSpacing = 4f;
            Place(nextLabel.gameObject, new Vector2(0.5f, 0.5f), new Vector2(228f, 44f), new Vector2(140f, 36f));

            var chips = new Image[2];
            for (int i = 0; i < chips.Length; i++)
            {
                var chip = Node("Next" + i, fill.transform);
                Place(chip, new Vector2(0.5f, 0.5f), new Vector2(196f + i * 66f, -18f), new Vector2(56f, 56f));
                var chipFill = AddSticker(chip, UISpriteFactory.Circle, Coral, 28f, 5f);
                chip.GetComponent<Image>().raycastTarget = false;
                chips[i] = chipFill;
            }

            view.cartCard = RectOf(card);
            view.cartFill = fill;
            view.cartSeats = seats;
            view.nextChips = chips;
            view.doneMark = done;
            view.cartsLeftLabel = null;
        }

        // ---- the queue -------------------------------------------------------------------

        // A row of places that sizes itself to however many the level has, so five places do
        // not sit in a tray drawn for seven.
        static void BuildQueueTray(Transform hud, UIColourMatchHud view, RectTransform boosterRow)
        {
            var tray = Node(QueueTrayName, hud);
            var rect = RectOf(tray);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(700f, 150f);
            rect.anchoredPosition = new Vector2(0f, 470f);

            // Takes taps for the same reason the cart does.
            AddPanel(tray, UISpriteFactory.Round, Ink, 42f).raycastTarget = true;
            var fillGo = Node("Fill", tray.transform);
            Stretch(fillGo, Outline);
            AddPanel(fillGo, UISpriteFactory.Round, Hex("F4F7FB"), 36f).raycastTarget = false;
            fillGo.AddComponent<LayoutElement>().ignoreLayout = true;

            var row = tray.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(22, 22, 20, 20);
            row.spacing = 14f;
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = false;
            row.childControlHeight = false;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;

            var fitter = tray.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

            var slots = new RectTransform[7];
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = Node("Slot" + i, tray.transform);
                var slotRect = RectOf(slot);
                slotRect.sizeDelta = new Vector2(106f, 106f);
                var back = AddPanel(slot, UISpriteFactory.Round, Hex("DCE3EC"), 24f);
                back.raycastTarget = false;
                AddFixedSize(slot.transform, 106f, 106f);
                slots[i] = slotRect;
            }

            // Where a case with no place hangs: just above the middle of the tray.
            var overflow = Node("Overflow", tray.transform);
            Place(overflow, new Vector2(0.5f, 1f), new Vector2(0f, 70f), new Vector2(10f, 10f));
            overflow.AddComponent<LayoutElement>().ignoreLayout = true;
            var overflowRect = RectOf(overflow);

            view.queueTray = rect;
            view.queueSlots = slots;
            view.overflowAnchor = overflowRect;
            view.boosterRow = boosterRow;
            view.trayYWithBoosters = 480f;
            view.trayYWithoutBoosters = 250f;
        }

        // ---- flying suitcases --------------------------------------------------------------

        static void BuildTokenLayer(Transform hud, UIColourMatchHud view)
        {
            var layer = Node(TokenLayerName, hud);
            Stretch(layer);

            var template = Node("SuitcaseToken", layer.transform);
            Place(template, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(100f, 100f));
            var outline = template.AddComponent<Image>();
            outline.sprite = UISpriteFactory.IconSuitcaseOutline;
            outline.color = Ink;
            outline.raycastTarget = false;

            var fill = Node("Fill", template.transform);
            Stretch(fill);
            var fillImage = fill.AddComponent<Image>();
            fillImage.sprite = UISpriteFactory.IconSuitcase;
            fillImage.color = Coral;
            fillImage.raycastTarget = false;

            template.SetActive(false);

            view.tokenLayer = RectOf(layer);
            view.tokenTemplate = RectOf(template);
        }

        // ---- the hand ---------------------------------------------------------------------

        // Last in the HUD, so it draws over the board, the cart and the booster buttons alike,
        // and takes no taps: the player has to be able to press exactly what it points at.
        static UITutorialHand BuildTutorialHand(Transform hud)
        {
            var root = Node(HandName, hud);
            Stretch(root);

            var body = Node("Body", root.transform);
            Stretch(body);

            var ring = Node("Ring", body.transform);
            Place(ring, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(150f, 150f));
            var ringImage = ring.AddComponent<Image>();
            ringImage.sprite = UISpriteFactory.Ring;
            ringImage.color = Panel;
            ringImage.raycastTarget = false;
            var ringGroup = ring.AddComponent<CanvasGroup>();
            ringGroup.blocksRaycasts = false;
            ringGroup.interactable = false;

            var hand = Node("Hand", body.transform);
            Place(hand, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(170f, 170f));
            RectOf(hand).pivot = UISpriteFactory.HandFingertip;
            RectOf(hand).localRotation = Quaternion.Euler(0f, 0f, 18f);
            var handOutline = hand.AddComponent<Image>();
            handOutline.sprite = UISpriteFactory.IconHandOutline;
            handOutline.color = Ink;
            handOutline.raycastTarget = false;

            var handFill = Node("Fill", hand.transform);
            Stretch(handFill);
            var handFillImage = handFill.AddComponent<Image>();
            handFillImage.sprite = UISpriteFactory.IconHand;
            handFillImage.color = Panel;
            handFillImage.raycastTarget = false;

            var view = root.AddComponent<UITutorialHand>();
            view.body = body;
            view.hand = RectOf(hand);
            view.ring = RectOf(ring);
            view.ringGroup = ringGroup;

            body.SetActive(false);
            return view;
        }
    }
}
