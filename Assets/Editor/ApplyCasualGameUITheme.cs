using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using SheepGame.EditorTools;

/// <summary>Art direction for the existing, wired Baggage Jam canvas. Safe to reapply.</summary>
public static class ApplyCasualGameUITheme
{
    const string Pack = "Assets/Layer Lab/GUI Pro-CasualGame/ResourcesData/";
    static Color Navy => C("102947");
    static Color Blue => C("204C78");
    static Color Muted => C("B8D5E9");
    static Color Gold => C("FFDC68");
    static Transform canvas;
    static TMP_FontAsset font;
    static TMP_FontAsset bodyFont;

    // One grid for every screen: 32 from the safe edges, 24 between things, 56 inside a card.
    const float Margin = 32;
    const float TopRowY = 96;            // centre of the top row, down from the safe area's top
    const float SecondaryRowY = 56 + 65; // centre of a card's 130-high bottom row
    static readonly Vector2 WalletSize = new Vector2(270, 96);
    static readonly Vector2 LevelSize = new Vector2(236, 92);

    // Every large panel is one white frame, tinted per panel: the white fill takes the colour,
    // the grey foot a darker shade of it, and the dark outline stays dark.
    const string PanelFrame = "Frame/BorderFrame_Square01_White.png";
    static Color PanelColour(string panel)
    {
        switch (panel)
        {
            case "WinPanel": return C("188653");    // won: green
            case "LosePanel": return C("B93D4C");   // lost: red
            case "PausePanel": return C("245AA8");  // held: calm blue
            case "ShopPanel": return C("6F3FB3");   // commerce: purple
            case "SettingsPanel": return C("45ADF0");
            case "PrivacyChoicesPanel": return C("AB68E8");
            case "ModeBanner": return C("E9781D");  // a change of pace: orange
            default: return C("123B66");            // the menu's dock: deep blue
        }
    }

    [MenuItem("SheepGame/UI/Apply GUI Pro Casual Theme")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play mode before applying the design.");
        canvas = GameObject.Find("MainSystemn/UICanvas").transform;
        Undo.RegisterFullObjectHierarchyUndo(canvas.gameObject, "Baggage Jam art direction");
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Pack + "Fonts/LilitaOne-Regular Outline 54 SDF.asset");
        // The plain atlas shipped with the pack has different face metrics and clips in several
        // of the existing 60-high text boxes. The 40 outline atlas shares the UI metrics while
        // keeping small copy noticeably lighter than the 54 display face.
        bodyFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Pack + "Fonts/LilitaOne-Regular Outline 40 SDF.asset");
        if (!font || !bodyFont) throw new InvalidOperationException("GUI Pro font missing.");

        // Reset the previous pass's accidental material overrides on inactive labels, too.
        foreach (var t in canvas.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            var so = new SerializedObject(t);
            so.FindProperty("m_fontAsset").objectReferenceValue = font;
            so.FindProperty("m_sharedMaterial").objectReferenceValue = font.material;
            so.FindProperty("m_fontColor").colorValue = Color.white;
            so.ApplyModifiedProperties();
            t.characterSpacing = 0;
            t.fontStyle = FontStyles.Normal;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Overflow;
            EditorUtility.SetDirty(t);
        }
        foreach (var image in canvas.GetComponentsInChildren<UnityEngine.UI.Image>(true))
        {
            if (image.name == "Dim") { Skin(image, null, new Color(.025f,.06f,.13f,.86f)); image.raycastTarget = true; }
            if (image.name == "Shine") image.gameObject.SetActive(false);
            if (image.name == "Lip") image.enabled = false;
        }
        foreach (var b in canvas.GetComponentsInChildren<UnityEngine.UI.Button>(true))
        {
            bool small = b.GetComponent<RectTransform>().rect.width < 220;
            ButtonStyle(b.transform, small ? "BlueGray" : "Blue");
        }
        foreach (var safe in canvas.GetComponentsInChildren<Transform>(true).Where(t => t.name == "CoinPillSlot")) Wallet(safe);
        MainMenu();
        Hud();
        Panels();
        RefineVisualHierarchy();
        StyleTimer();
        RoundCorners();
        Preview("MainMenu");
        Canvas.ForceUpdateCanvases();
        EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
        EditorSceneManager.SaveScene(canvas.gameObject.scene);
        Debug.Log("Baggage Jam: complete art-directed menu, HUD, results, pause, shop and announcement applied.");
    }

    static void MainMenu()
    {
        var menu = canvas.Find("MainMenu");
        var safe = menu.Find("SafeArea");
        var art = Node(menu, "Designer_AirportArt");
        art.SetAsFirstSibling();
        Stretch(art);
        var raw = art.GetComponent<RawImage>() ?? art.gameObject.AddComponent<RawImage>();
        raw.texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Game/BaggageJam.png");
        raw.raycastTarget = false;
        raw.color = Color.white;
        var fit = art.GetComponent<AspectRatioFitter>() ?? art.gameObject.AddComponent<AspectRatioFitter>();
        fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        fit.aspectRatio = (float)raw.texture.width / raw.texture.height;
        menu.Find("Dim").GetComponent<UnityEngine.UI.Image>().color = new Color(0,0,0,0);
        safe.Find("Logo").gameObject.SetActive(false); // The full airport illustration contains the wordmark.

        // The illustration carries the wordmark and the mood; the only thing over its top is
        // the wallet, in the same corner and at the same size as in the game.
        Hide(safe, "Designer_Header");
        var wallet = safe.Find("CoinPillSlot"); Place(wallet, 1,1,-(Margin+WalletSize.x/2),-TopRowY,WalletSize.x,WalletSize.y);

        // Everything that acts sits in one dock under the thumb, the level as a tab on its edge.
        var dock = Image(safe,"Designer_DepartureDock",PanelFrame,PanelColour("MainMenu"));
        Place(dock,.5f,0,0,24+290,980,580);
        dock.SetSiblingIndex(1);
        var line = Image(dock,"Accent","Frame/BasicFrame_Round20.png",C("53DCE0"));
        Place(line,.5f,1,0,-12,780,5);
        Hide(dock, "Ready");
        Hide(dock, "Footer");

        var chip = safe.Find("LevelChip");
        Place(chip,.5f,0,0,24+580,420,96);
        chip.SetAsLastSibling();
        Skin(chip.GetComponent<UnityEngine.UI.Image>(), S(PanelFrame), C("2E7CD6"));
        var level = chip.Find("LevelText").GetComponent<TextMeshProUGUI>();
        level.fontSize = 44; level.alignment = TextAlignmentOptions.Center; Stretch(level.transform,12);

        var play = safe.Find("PlayButtonSlot"); Place(play,.5f,0,0,450,850,166);
        ButtonStyle(play.Find("PlayButton"),"Yellow",66);
        Label(play,"BOARD & PLAY");
        var arrow = Image(play.Find("PlayButton"),"Designer_PlayIcon","IconMisc/Icon_PictoIcon_Play.png",Color.white);
        Place(arrow,.5f,.5f,305,0,52,60);
        var shop = safe.Find("MenuShopButtonSlot"); Place(shop,.5f,0,-221,266,408,132);
        ButtonStyle(shop.Find("MenuShopButton"),"Purple",44); Label(shop,"SHOP");
        var shopIcon=Image(shop.Find("MenuShopButton"),"Designer_Icon","IconMisc/Icon_MenuIcon02_Shop.Png",Color.white);
        Place(shopIcon,.5f,.5f,-130,5,84,84);
        Place(shop.Find("MenuShopButton/Label"),.5f,.5f,35,0,240,75);
        var daily = safe.Find("DailyButtonSlot"); Place(daily,.5f,0,221,266,408,132);
        ButtonStyle(daily.Find("DailyButton"),"Sky",44); Label(daily,"DAILY RUN");
        var dailyIcon=Image(daily.Find("DailyButton"),"Designer_Icon","Icon_ItemIcons/128/Icon_Compass.png",Color.white);
        Place(dailyIcon,.5f,.5f,-135,4,75,75);
        Place(daily.Find("DailyButton/Label"),.5f,.5f,40,0,245,75);

        // Before its level the Daily Run shows locked instead of hiding: a lock where the
        // compass is and "LEVEL 8" for a name, so the player knows it is coming.
        var dailyButton=daily.Find("DailyButton");
        var lockMark=Image(dailyButton,"Designer_Lock","Icon_PictoIcons/128/Pictoicon_Lock.Png",Color.white);
        Place(lockMark,.5f,.5f,-135,4,64,64);
        lockMark.GetComponent<UnityEngine.UI.Image>().type=UnityEngine.UI.Image.Type.Simple;
        lockMark.GetComponent<UnityEngine.UI.Image>().preserveAspect=true;
        lockMark.gameObject.SetActive(false);
        var lockable=daily.GetComponent<SheepGame.Core.UILockableButton>();
        if(!lockable) lockable=Undo.AddComponent<SheepGame.Core.UILockableButton>(daily.gameObject);
        lockable.button=dailyButton.GetComponent<UnityEngine.UI.Button>();
        lockable.face=dailyButton.GetComponent<UnityEngine.UI.Image>();
        lockable.icon=dailyIcon.GetComponent<UnityEngine.UI.Image>();
        lockable.lockMark=lockMark.gameObject;
        lockable.label=dailyButton.GetComponentInChildren<TextMeshProUGUI>(true);
        lockable.openText="DAILY RUN";
        EditorUtility.SetDirty(lockable);
        daily.gameObject.SetActive(true);

        var settings=safe.Find("SettingsButtonSlot");
        if(settings)
        {
            Place(settings,.5f,0,0,96,850,112);
            ButtonStyle(settings.Find("SettingsButton"),"Purple",40);
            Label(settings,"SETTINGS");
        }
    }

    static void Hud()
    {
        var hud = canvas.Find("SafeArea");
        Place(hud.Find("Boosters"),.5f,0,0,122,720,160);
        var colourHud=hud.GetComponent<SheepGame.Core.UIColourMatchHud>();
        colourHud.trayYWithBoosters=280;colourHud.trayYWithoutBoosters=190;EditorUtility.SetDirty(colourHud);
        // The top centre of the frame is the check-in terminal and its line of carts, so nothing
        // sits there. The top row is pause with the level beside it, and that is the whole
        // gameplay HUD above the board.
        Place(hud.Find("PauseButtonSlot"),0,1,Margin+64,-TopRowY,128,128);
        var card = hud.Find("LevelCard");
        card.gameObject.SetActive(true);
        Place(card,0,1,Margin+128+24+LevelSize.x/2,-TopRowY,LevelSize.x,LevelSize.y);
        Skin(card.GetComponent<UnityEngine.UI.Image>(), S(PanelFrame), PanelColour("MainMenu"));
        // The Time Rush clock and the Remove prompt are panels too. The clock's colour is set at
        // run time (amber, then coral in the last seconds); this is only its resting tint.
        var timerFace=hud.Find("TimerPillSlot/Body/TimerPill");
        if(timerFace) Skin(timerFace.GetComponent<UnityEngine.UI.Image>(), S(PanelFrame), C("FFC635"));
        var removeHintFace=hud.Find("RemoveHint");
        if(removeHintFace) Skin(removeHintFace.GetComponent<UnityEngine.UI.Image>(), S(PanelFrame), PanelColour("LosePanel"));
        var text = card.Find("LevelText").GetComponent<TextMeshProUGUI>(); text.color = Color.white;
        text.fontSize = 44; text.alignment = TextAlignmentOptions.Center; text.textWrappingMode = TextWrappingModes.NoWrap;
        Stretch(text.transform, 8);
        // The tap limit is gone and the bar said nothing the board does not: only the level.
        Hide(card, "ProgressTrack");
        Hide(card, "Taps");
        // No wallet in play: nothing on the board spends or reads the balance, and there is no
        // free corner for it -- the top right is the line of carts, and a spot on the floor
        // moves with the camera's aspect. The balance lives in the menu, the shop and the
        // result panels. The unlock gate would switch it back on, so it loses that entry.
        var hudWallet = hud.Find("CoinPillSlot");
        if (hudWallet)
        {
            var gate = hud.GetComponent<SheepGame.Core.UIUnlockGate>();
            if (gate && gate.entries != null)
            {
                gate.entries = gate.entries.Where(e => e.target != hudWallet.gameObject).ToArray();
                EditorUtility.SetDirty(gate);
            }
            hudWallet.gameObject.SetActive(false);
        }
        // In Time Rush the clock takes the level's own slot: the top row is the one place that
        // does not move with the screen's shape, and for that minute the time is the number
        // that matters. The timer is a little larger and drawn after the level, so it covers
        // it whole while it shows, and the level comes back when it hides.
        var timer = hud.Find("TimerPillSlot");
        Place(timer,0,1,Margin+128+24+LevelSize.x/2,-TopRowY,240,96);
        timer.SetSiblingIndex(card.GetSiblingIndex()+1);
        // Just above the boosters: over the belt, not over the cases it asks you to tap.
        var removeHint = hud.Find("RemoveHint"); if(removeHint) ((RectTransform)removeHint).anchoredPosition=new Vector2(0,262);
        ButtonStyle(hud.Find("PauseButtonSlot/PauseButton"),"BlueGray");
        hud.Find("PauseButtonSlot/Label").gameObject.SetActive(false);
        foreach(var pair in new[]{new[]{"Hint","Purple","Icon_ItemIcons/128/Icon_Book.png"}, new[]{"Shuffle","Blue","Icon_ItemIcons/128/Icon_Cards.png"},new[]{"Remove","Orange","Icon_ItemIcons/128/Icon_Hammer.png"}})
        {
            var slot = hud.Find("Boosters/"+pair[0]+"ButtonSlot");
            if(!slot) continue;
            var slotRect=(RectTransform)slot;slotRect.sizeDelta=new Vector2(184,160);
            var button = slot.Find(pair[0]+"Button"); ButtonStyle(button,pair[1]);
            var ico = button.Find("Icon");
            if(ico) {Skin(ico.GetComponent<UnityEngine.UI.Image>(),S(pair[2]),Color.white,false);Place(ico,.5f,.5f,0,16,78,78);}
            Place(slot.Find("Label"),.5f,.5f,0,-43,180,38);
            var caption = slot.Find("Label/Text").GetComponent<TextMeshProUGUI>(); caption.fontSize=34; caption.color=Color.white;
            slot.Find("Label").SetAsLastSibling();
            var badge=slot.Find("CountBadge");
            if(badge) {Skin(badge.GetComponent<UnityEngine.UI.Image>(),S("Frame/BasicFrame_Round20.png"),Navy);Place(badge,.5f,.5f,72,64,60,52);}
        }
        var cart=hud.Find("CartCard/Card");
        if(cart)
        {
            Skin(cart.GetComponent<UnityEngine.UI.Image>(),S("Frame/BasicFrame_Round20.png"),C("49C3DD"));
            cart.GetComponent<UnityEngine.UI.Image>().raycastTarget=true;
            foreach(string n in new[]{"Next0","Next1"}) Skin(cart.Find(n).GetComponent<UnityEngine.UI.Image>(),S("Button/Button_Circle147_White.png"),Color.white);
        }
        var token=hud.Find("TokenLayer/SuitcaseToken/Fill").GetComponent<UnityEngine.UI.Image>();
        Skin(token,AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Game/UI/Sprites/Icon_Suitcase.png"),Color.white,false);
        var hand=hud.Find("TutorialHand/Body/Hand/Fill");
        if(hand)Skin(hand.GetComponent<UnityEngine.UI.Image>(),AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Game/UI/Sprites/Icon_Hand.png"),Color.white,false);
        var queue=hud.Find("QueueTray");
        if(queue)
        {
            ((RectTransform)queue).anchoredPosition=new Vector2(0,280);
            var qr=(RectTransform)queue;qr.sizeDelta=new Vector2(qr.sizeDelta.x,130);
            foreach(Transform child in queue)
            {
                if(!child.name.StartsWith("Slot"))continue;
                ((RectTransform)child).sizeDelta=new Vector2(90,90);
                var le=child.GetComponent<LayoutElement>();if(le){le.preferredWidth=le.minWidth=90;le.preferredHeight=le.minHeight=90;}
            }
            Skin(queue.GetComponent<UnityEngine.UI.Image>(),S("Frame/BasicFrame_Round20.png"),C("0C2039"));
            queue.GetComponent<UnityEngine.UI.Image>().raycastTarget=true;
            var fill=queue.Find("Fill"); if(fill) Skin(fill.GetComponent<UnityEngine.UI.Image>(),S("Frame/BasicFrame_Round20.png"),Blue);
        }
    }

    static void Panels()
    {
        foreach(string name in new[]{"WinPanel","LosePanel","PausePanel","ModeBanner","ShopPanel","SettingsPanel","PrivacyChoicesPanel"})
        {
            var panel=canvas.Find(name); var card=panel.Find("SafeArea/Card");
            Skin(card.GetComponent<UnityEngine.UI.Image>(),S(PanelFrame),PanelColour(name));
            foreach(var t in card.GetComponentsInChildren<TextMeshProUGUI>(true))
                if(t.name=="Subtitle" || t.name=="Description")
                {
                    UseBodyFont(t);
                    t.color=C("EAF6FF");
                    t.textWrappingMode=TextWrappingModes.Normal;
                }
            var title=card.Find("Title");
            var ribbon=Image(card,"Designer_TitleRibbon","Label/Title_Ribbon_Bg_Orange.png",Color.white);
            Place(ribbon,.5f,1,0,-100,name=="ShopPanel"?430:790,120);
            ribbon.SetAsFirstSibling();
            if(title) { Place(title,.5f,1,0,-100,name=="ShopPanel"?370:740,86); title.GetComponent<TextMeshProUGUI>().fontSize=name=="ShopPanel"?56:64; title.SetAsLastSibling(); }
            if(name=="WinPanel") Win(card);
            if(name=="LosePanel") Lose(card);
            if(name=="PausePanel") Pause(card);
            if(name=="ShopPanel") Shop(card);
            if(name=="ModeBanner")
            {
                ribbon.gameObject.SetActive(false);
                // The badge hangs over the top edge; title and line stack under it with room.
                // Sized to be read at a glance: it is on screen for under two seconds.
                Place(card,.5f,.5f,0,40,880,380);
                var badge=card.Find("Badge");
                if(badge)
                {
                    Place(badge,.5f,1,0,30,200,200);
                    var disc=badge.GetComponent<UnityEngine.UI.Image>();
                    if(disc)
                    {
                        disc.enabled=true;
                        Skin(disc,S("Button/Button_Circle147_White.png"),Gold);
                    }
                    var icon=badge.Find("Clock");
                    if(icon)
                    {
                        // The hourglass, in the yellow disc. Also the default the banner falls
                        // back to after showing a booster's icon.
                        Skin(icon.GetComponent<UnityEngine.UI.Image>(),S("Icon_ItemIcons/256/Icon_Hourglass.png"),Color.white,false);
                        Place(icon,.5f,.5f,0,0,124,124);
                    }
                }
                if(title)
                {
                    Place(title,.5f,1,0,-172,820,120);
                    var tt=title.GetComponent<TextMeshProUGUI>(); tt.enableAutoSizing=false; tt.fontSize=96;
                    tt.alignment=TextAlignmentOptions.Center; tt.textWrappingMode=TextWrappingModes.NoWrap;
                }
                var subtitle=card.Find("Subtitle");
                if(subtitle)
                {
                    Place(subtitle,.5f,1,0,-272,800,70);
                    var st=subtitle.GetComponent<TextMeshProUGUI>(); st.color=Muted; st.enableAutoSizing=false; st.fontSize=50; st.text="Beat the clock!";
                    UseBodyFont(st);
                    st.alignment=TextAlignmentOptions.Center;
                }
                // There is no tap limit any more, so the Time Rush line stops promising one.
                var gm=UnityEngine.Object.FindFirstObjectByType<SheepGame.Core.GameManager>(FindObjectsInactive.Include);
                if(gm){ gm.timeRushSubtitle="Beat the clock!"; EditorUtility.SetDirty(gm); }
            }
            // Body copy is deliberately quieter than headings and actions. A clean, non-outline
            // face remains readable on coloured surfaces without competing with button labels.
            foreach(var t in card.GetComponentsInChildren<TextMeshProUGUI>(true))
                if(t.name=="Subtitle" || t.name=="Description" || t.name=="Designer_Description")
                {
                    UseBodyFont(t);
                    t.color=C("F3FAFF");
                }
        }
    }

    static void Win(Transform card)
    {
        Place(card,.5f,.5f,0,0,900,904);
        card.Find("Badge").gameObject.SetActive(false);
        for(int i=0;i<3;i++)
        {
            var star=Image(card,"Designer_Star"+i,"Icon_ItemIcons/512/Icon_Star.Png",Color.white);
            Place(star,.5f,1,(i-1)*210,i==1?-292:-320,i==1?230:175,i==1?230:175);
            star.localEulerAngles=new Vector3(0,0,(1-i)*12);
        }
        Place(card.Find("Subtitle"),.5f,1,0,-470,760,60);
        Body(card.Find("Subtitle"));
        var reward=card.Find("Reward"); Place(reward,.5f,1,0,-590,380,104);
        Skin(reward.GetComponent<UnityEngine.UI.Image>(),S(PanelFrame),C("157A41"));
        var amount=reward.Find("RewardText").GetComponent<TextMeshProUGUI>(); amount.color=Gold; amount.fontSize=60;
        // One 720 row: a square replay beside the wide primary, 56 up from the card's foot.
        var next=card.Find("NextButtonSlot"); Place(next,.5f,0,87,56+75,546,150);ButtonStyle(next.Find("NextButton"),"Yellow",62);
        Place(card.Find("ReplayButtonSlot"),.5f,0,-285,56+75,150,150);
        Hide(card,"Designer_Footer");
    }

    static void Lose(Transform card)
    {
        Place(card,.5f,.5f,0,0,900,1020);
        var badge=card.Find("Badge");Place(badge,.5f,1,0,-270,160,160);
        Skin(badge.GetComponent<UnityEngine.UI.Image>(),S("Button/Button_Circle147_Navy.png"),Color.white);
        Place(card.Find("Subtitle"),.5f,1,0,-410,760,90);
        Body(card.Find("Subtitle"));
        Place(card.Find("ContinueButtonSlot"),.5f,0,0,SecondaryRowY+65+24+130+24+75,720,150);
        ButtonStyle(card.Find("ContinueButtonSlot/ContinueButton"),"Yellow",52);
        Place(card.Find("ShuffleRescueButtonSlot"),.5f,0,0,SecondaryRowY+65+24+65,720,130);
        ButtonStyle(card.Find("ShuffleRescueButtonSlot/ShuffleRescueButton"),"Blue",48);
        SecondaryRow(card,"LoseReplayButtonSlot","ShopButtonSlot");
        // The coin goes while the continue is paid for by a free rescue (GameManager.RefreshWallet).
        var continueCoin=card.Find("ContinueButtonSlot/Price/Coin");
        var manager=UnityEngine.Object.FindFirstObjectByType<SheepGame.Core.GameManager>(FindObjectsInactive.Include);
        if(manager && continueCoin){ manager.continuePriceIcon=continueCoin.gameObject; EditorUtility.SetDirty(manager); }
    }

    static void Pause(Transform card)
    {
        Place(card,.5f,.5f,0,0,900,840);
        var clock=Image(card,"Designer_Clock","Icon_ItemIcons/128/Icon_Timer.png",Color.white); Place(clock,.5f,1,0,-270,160,160);
        Text(card,"Designer_Description","Your next departure can wait.",34,Muted,0,0,760,60);
        Place(card.Find("Designer_Description"),.5f,1,0,-400,760,60);
        Body(card.Find("Designer_Description"));
        Place(card.Find("ResumeButtonSlot"),.5f,0,0,SecondaryRowY+65+24+75,720,150); ButtonStyle(card.Find("ResumeButtonSlot/ResumeButton"),"Yellow",62);
        SecondaryRow(card,"PauseRestartButtonSlot","MenuButtonSlot");
    }

    static void Shop(Transform card)
    {
        // Sized to its six rows rather than the whole screen, the header on one line.
        Place(card,.5f,.5f,0,0,940,1540);
        Place(card.Find("Balance"),0,1,163,-100,250,80);
        Place(card.Find("CloseButtonSlot"),1,1,-90,-100,104,104);
        var scroll=card.Find("ScrollArea");
        scroll.GetComponent<UnityEngine.UI.Image>().raycastTarget=true;
        var row=scroll.Find("List/RowTemplate");
        Skin(row.GetComponent<UnityEngine.UI.Image>(),S(PanelFrame),Blue);
        var view=row.GetComponent<SheepGame.Core.ShopRowView>();
        view.productArtwork=new[]{
            Artwork("starter.pack","Icon_ItemIcons/512/Icon_Gift.Png","5C478D"),
            Artwork("coins.small","Icon_ShopItems/ShopItem_Coin_1.png","2B6489"),
            Artwork("coins.medium","Icon_ShopItems/ShopItem_Coin_3.png","2B6489"),
            Artwork("coins.large","Icon_ShopItems/ShopItem_Coin_5.png","84613D"),
            Artwork("boosters.pack","Icon_ItemIcons/512/Icon_Booster.Png","5C478D"),
            Artwork("taps.pack","Icon_ItemIcons/512/Icon_Energy_Yellow.Png","2B6489")};
        EditorUtility.SetDirty(view);
        row.Find("Coin").GetComponent<RectTransform>().sizeDelta=new Vector2(112,112);
        var balance=card.Find("Balance");Skin(balance.GetComponent<UnityEngine.UI.Image>(),S(PanelFrame),C("4E2585"));
        FixCoin(balance.Find("Coin"));
        FixCoin(row.Find("Coin"));
        ButtonStyle(row.Find("BuyButtonSlot/BuyButton"),"Green",36);
        var description=row.Find("Description");
        if(description) UseBodyFont(description.GetComponent<TextMeshProUGUI>());
        card.Find("Title").GetComponent<TextMeshProUGUI>().text="SHOP";
    }

    // Shared surfaces and a single action accent keep the artwork and the board in focus.
    static void RefineVisualHierarchy()
    {
        const string materialPath="Assets/_Game/UI/CasualCleanText.mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if(!material)
        {
            material=new Material(font.material);
            AssetDatabase.CreateAsset(material,materialPath);
        }
        material.CopyPropertiesFromMaterial(font.material);
        material.SetFloat("_OutlineWidth",0.04f);
        material.SetFloat("_FaceDilate",0);
        material.DisableKeyword("UNDERLAY_ON");
        material.DisableKeyword("UNDERLAY_INNER");
        EditorUtility.SetDirty(material);
        foreach(var t in canvas.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            t.font=font;
            t.fontSharedMaterial=material;
            t.color=C("F3F6FA");
            // Existing auto-size maxima otherwise override authored typography.
            t.enableAutoSizing=false;
            t.overflowMode=TextOverflowModes.Overflow;
            EditorUtility.SetDirty(t);
        }
        foreach(var b in canvas.GetComponentsInChildren<UnityEngine.UI.Button>(true))
        {
            string n=b.name;
            bool primary=n=="PlayButton" || n=="NextButton" || n=="ResumeButton" || n=="ContinueButton";
            bool purchase=n=="BuyButton";
            string face=primary?"FFC936":purchase?"42C96B":"45ADF0";
            if(n=="MenuShopButton" || n=="HintButton")face="AB68E8";
            if(n=="DailyButton")face="2DCBCD";
            if(n=="RemoveButton")face="FF9B45";
            if(n=="SettingsButton" || n=="SoundButton")face="AB68E8";
            if(n=="MusicButton" || n=="LimitedAdsButton")face="45ADF0";
            if(n=="VibrationButton")face="FF8A58";
            if(n=="PrivacyChoicesButton" || n=="PersonalizedAdsButton")face="42C96B";
            if(n=="SettingsCloseButton")face="FFC936";
            if(n=="PrivacyCloseButton")face="90A6B9";
            Skin(b.GetComponent<UnityEngine.UI.Image>(),S(PanelFrame),C(face));
        }
        foreach(string name in new[]{"WinPanel","LosePanel","PausePanel","ShopPanel","ModeBanner","SettingsPanel","PrivacyChoicesPanel"})
        {
            var card=canvas.Find(name+"/SafeArea/Card");
            string surface=name=="WinPanel"?"DDF5B9":name=="LosePanel"?"FFE2CD":name=="ShopPanel" || name=="PrivacyChoicesPanel"?"EEE3FF":name=="ModeBanner"?"FFB343":"DDF4FF";
            Skin(card.GetComponent<UnityEngine.UI.Image>(),S(PanelFrame),C(surface));
            Hide(card,"Designer_TitleRibbon");
            var titleTransform=card.Find("Title") ?? card.Find("Designer_TitleRibbon/Title");
            if(titleTransform.parent!=card)
            {
                Undo.SetTransformParent(titleTransform,card,"Unify panel heading");
                Place(titleTransform,.5f,1,0,-100,740,86);
            }
            var title=titleTransform.GetComponent<TextMeshProUGUI>();
            title.fontSize=name=="ModeBanner"?86:64;
            title.color=C("FFFFFF");
            if(name!="ModeBanner")
            {
                var heading=Image(card,"Designer_HeadingFace",PanelFrame,C(name=="WinPanel"?"55C879":name=="LosePanel"?"F08065":name=="ShopPanel" || name=="PrivacyChoicesPanel"?"A866DE":"45ADF0"));
                Place(heading,.5f,1,0,-100,name=="ShopPanel"?320:780,112);
                heading.SetAsFirstSibling();
            }
            if(name!="ModeBanner")
            {
                var divider=Image(card,"Designer_HeaderRule","Frame/BasicFrame_Round20.png",C("B4D4E5"));
                Place(divider,.5f,1,0,-174,name=="ShopPanel"?820:760,2);
            }
            foreach(var t in card.GetComponentsInChildren<TextMeshProUGUI>(true))
                if(t.name=="Subtitle" || t.name=="Description" || t.name=="Designer_Description") t.color=Color.white;
        }
        var shop=canvas.Find("ShopPanel/SafeArea/Card");
        Place(shop.Find("Title"),.5f,1,0,-96,320,88);
        Place(shop.Find("Balance"),0,1,166,-96,220,84);
        Place(shop.Find("Balance/Coin"),.5f,.5f,-74,0,56,56);
        Place(shop.Find("Balance/BalanceText"),.5f,.5f,26,0,132,60);
        var balanceText=shop.Find("Balance/BalanceText").GetComponent<TextMeshProUGUI>();
        balanceText.enableAutoSizing=true;balanceText.fontSizeMin=26;balanceText.fontSizeMax=38;
        Skin(shop.Find("Balance").GetComponent<UnityEngine.UI.Image>(),S(PanelFrame),C("AF78DF"));
        Place(shop.Find("CloseButtonSlot"),1,1,-100,-96,96,96);
        var row=shop.Find("ScrollArea/List/RowTemplate");
        var view=row.GetComponent<SheepGame.Core.ShopRowView>();
        for(int i=0;i<view.productArtwork.Length;i++)
        {
            var art=view.productArtwork[i];art.background=C(i==0 || i==4?"C9A8EB":"8DCEE9");view.productArtwork[i]=art;
        }
        EditorUtility.SetDirty(view);
        Skin(row.Find("Badge").GetComponent<UnityEngine.UI.Image>(),S(PanelFrame),C("F39745"));
        row.Find("Title").GetComponent<TextMeshProUGUI>().fontSize=38;
        row.Find("Description").GetComponent<TextMeshProUGUI>().fontSize=28;
        var description=row.Find("Description").GetComponent<TextMeshProUGUI>();
        description.rectTransform.sizeDelta=new Vector2(380,68);
        description.enableAutoSizing=true;description.fontSizeMin=24;description.fontSizeMax=28;
        description.textWrappingMode=TextWrappingModes.Normal;
        description.color=Color.white;
        var menu=canvas.Find("MainMenu/SafeArea");
        Skin(menu.Find("Designer_DepartureDock").GetComponent<UnityEngine.UI.Image>(),S(PanelFrame),C("C5EDFA"));
        Hide(menu.Find("Designer_DepartureDock"),"Accent");
        Skin(menu.Find("LevelChip").GetComponent<UnityEngine.UI.Image>(),S(PanelFrame),C("45ADF0"));
        Label(menu.Find("PlayButtonSlot"),"PLAY");
        StylePlayAction();
        foreach(var slot in canvas.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="CoinPillSlot"))
        {
            var pill=slot.Find("CoinPill");if(!pill)continue;
            Skin(pill.GetComponent<UnityEngine.UI.Image>(),S(PanelFrame),C("AB68E8"));
            Skin(pill.Find("Plus").GetComponent<UnityEngine.UI.Image>(),S(PanelFrame),C("42C96B"));
        }
        Skin(canvas.Find("WinPanel/SafeArea/Card/Reward").GetComponent<UnityEngine.UI.Image>(),S(PanelFrame),C("55C879"));
        Skin(canvas.Find("SafeArea/LevelCard").GetComponent<UnityEngine.UI.Image>(),S(PanelFrame),C("45ADF0"));
        Skin(canvas.Find("LosePanel/SafeArea/Card/Badge").GetComponent<UnityEngine.UI.Image>(),S("Button/Button_Circle147_White.png"),C("F08065"));
        foreach(var im in canvas.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            if(im.name=="CountBadge" || im.name=="Price") Skin(im,S(PanelFrame),C("3593D4"));
        var reward=canvas.Find("WinPanel/SafeArea/Card/Reward");
        Place(reward.Find("Coin"),.5f,.5f,-70,0,64,64);
        Place(reward.Find("RewardText"),.5f,.5f,38,0,150,80);
        foreach(string name in new[]{"Hint","Shuffle","Remove"})
        {
            var slot=canvas.Find("SafeArea/Boosters/"+name+"ButtonSlot");
            Place(slot.Find("Label"),.5f,.5f,0,-30,180,48);
            var caption=slot.Find("Label/Text").GetComponent<TextMeshProUGUI>();
            caption.fontSize=30;
        }
        // Text on gold actions uses dark ink, keeping contrast without a heavy outline.
        foreach(string path in new[]{"MainMenu/SafeArea/PlayButtonSlot/PlayButton","WinPanel/SafeArea/Card/NextButtonSlot/NextButton","PausePanel/SafeArea/Card/ResumeButtonSlot/ResumeButton","LosePanel/SafeArea/Card/ContinueButtonSlot/ContinueButton"})
            foreach(var t in canvas.Find(path).GetComponentsInChildren<TextMeshProUGUI>(true)) t.color=Color.white;
        AssetDatabase.SaveAssets();
    }

    [MenuItem("SheepGame/UI/Refine Booster Height")]
    public static void RefineBoosterHeight()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play mode first.");
        canvas=GameObject.Find("MainSystemn/UICanvas").transform;
        var row=canvas.Find("SafeArea/Boosters");
        Undo.RegisterFullObjectHierarchyUndo(row.gameObject,"Booster height and captions");
        Place(row,.5f,0,0,122,720,160);
        foreach(string name in new[]{"Hint","Shuffle","Remove"})
        {
            var slot=row.Find(name+"ButtonSlot");
            ((RectTransform)slot).sizeDelta=new Vector2(184,160);
            var layout=slot.GetComponent<UnityEngine.UI.LayoutElement>();
            if(layout){layout.minHeight=160;layout.preferredHeight=160;}
            Place(slot.Find("Label"),.5f,.5f,0,-30,180,48);
        }
        Canvas.ForceUpdateCanvases();
        EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
        EditorSceneManager.SaveScene(canvas.gameObject.scene);
    }

    static void StyleTimer()
    {
        var slot=canvas.Find("SafeArea/TimerPillSlot");
        var view=slot.GetComponent<SheepGame.Core.UITimerPill>();
        ((RectTransform)slot).sizeDelta=new Vector2(240,108);
        Stretch(slot.Find("Body"));
        Stretch(view.fill.transform);
        var layout=view.fill.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        layout.padding=new RectOffset(24,24,0,14);
        layout.spacing=14;
        layout.childAlignment=TextAnchor.MiddleCenter;
        layout.childControlWidth=true;layout.childControlHeight=true;
        layout.childForceExpandWidth=false;layout.childForceExpandHeight=false;
        var iconLayout=view.clock.GetComponent<UnityEngine.UI.LayoutElement>();
        iconLayout.minWidth=iconLayout.preferredWidth=44;
        iconLayout.minHeight=iconLayout.preferredHeight=44;
        var textLayout=view.label.GetComponent<UnityEngine.UI.LayoutElement>();
        if(!textLayout)textLayout=Undo.AddComponent<UnityEngine.UI.LayoutElement>(view.label.gameObject);
        textLayout.minWidth=textLayout.preferredWidth=104;
        textLayout.minHeight=textLayout.preferredHeight=76;
        view.label.fontSize=52;view.label.enableAutoSizing=false;
        view.label.alignment=TextAlignmentOptions.Center;
        view.label.textWrappingMode=TextWrappingModes.NoWrap;
        view.normalInk=Color.white;view.warningInk=Color.white;
        view.clock.color=view.label.color=Color.white;
        EditorUtility.SetDirty(view);
    }

    [MenuItem("SheepGame/UI/Refine Timer")]
    public static void RefineTimer()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play mode first.");
        canvas=GameObject.Find("MainSystemn/UICanvas").transform;
        Undo.RegisterFullObjectHierarchyUndo(canvas.Find("SafeArea/TimerPillSlot").gameObject,"Timer layout");
        StyleTimer();
        Canvas.ForceUpdateCanvases();
        EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
        EditorSceneManager.SaveScene(canvas.gameObject.scene);
    }

    static void StylePlayAction()
    {
        var button=canvas.Find("MainMenu/SafeArea/PlayButtonSlot/PlayButton");
        var label=button.Find("Label").GetComponent<TextMeshProUGUI>();
        label.text="PLAY";
        label.fontSize=66;
        label.enableAutoSizing=false;
        label.color=Color.white;
        float width=label.GetPreferredValues("PLAY").x;
        const float gap=20, iconWidth=40;
        Place(label.transform,.5f,.5f,-(gap+iconWidth)/2,0,width+4,100);
        var icon=button.Find("Designer_PlayIcon");
        Place(icon,.5f,.5f,(width+gap)/2,0,iconWidth,48);
        icon.GetComponent<UnityEngine.UI.Image>().color=Color.white;
    }

    [MenuItem("SheepGame/UI/Refine Play Button")]
    public static void RefinePlayButton()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play mode first.");
        canvas=GameObject.Find("MainSystemn/UICanvas").transform;
        Undo.RegisterFullObjectHierarchyUndo(canvas.Find("MainMenu/SafeArea/PlayButtonSlot").gameObject,"Refine Play button");
        StylePlayAction();
        EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
        EditorSceneManager.SaveScene(canvas.gameObject.scene);
    }

    static void Wallet(Transform slot)
    {
        // A glossy purple button face with the pack's dark outline, not a flat tinted capsule:
        // the flat navy one disappeared into the dark ceiling beams at the top of the menu art.
        // Purple is also the Shop button's colour, which is where the wallet leads.
        var pill=slot.Find("CoinPill"); if(!pill)return;
        Skin(pill.GetComponent<UnityEngine.UI.Image>(),S("Button/Button01_175_Purple.png"),Color.white);
        pill.GetComponent<UnityEngine.UI.Image>().raycastTarget=true;
        float half=WalletSize.x/2f;
        var coin=pill.Find("Coin");
        FixCoin(coin);
        if(coin) Place(coin,.5f,.5f,-(half-46),2,72,72);
        // A bright green "+", larger, with the white cross over the button's own dark one so it
        // reads as an outlined plus.
        var plus=pill.Find("Plus"); if(plus)
        {
            Skin(plus.GetComponent<UnityEngine.UI.Image>(),S("UI_Etc/ResourceBar_Btn_Single_Green.png"),Color.white);
            Place(plus,1,.5f,-40,2,64,64);
            var mark=plus.Find("Mark"); if(mark){ Skin(mark.GetComponent<UnityEngine.UI.Image>(),S("UI_Etc/ResourceBar_Btn_Icon_Add.png"),Color.white,false); Place(mark,.5f,.5f,0,1,34,34); }
        }
        var count=pill.Find("CoinCount");
        if(count)
        {
            var t=count.GetComponent<TextMeshProUGUI>();
            t.color=Color.white; t.alignment=TextAlignmentOptions.Center; t.textWrappingMode=TextWrappingModes.NoWrap;
            t.enableAutoSizing=true; t.fontSizeMin=30; t.fontSizeMax=46;
            // Between the coin's right edge and the plus's left edge.
            float left=-(half-46)+36, right=half-40-32;
            Place(count,.5f,.5f,(left+right)/2f,2,right-left-8,60);
        }
    }
    static void FixCoin(Transform t) { if(t)Skin(t.GetComponent<UnityEngine.UI.Image>(),S("UI_Etc/ResourceBar_Icon_Coin.png"),Color.white,false); }
    static SheepGame.Core.ShopRowView.ProductArtwork Artwork(string id,string sprite,string colour)
        =>new SheepGame.Core.ShopRowView.ProductArtwork{productId=id,sprite=S(sprite),background=C(colour)};
    static void ButtonStyle(Transform tr,string colour,float size=0)
    {
        if(!tr)return;
        if(tr.parent.name.EndsWith("Slot")) Stretch(tr,0);
        var image=tr.GetComponent<UnityEngine.UI.Image>();
        Skin(image,S("Button/Button01_175_"+colour+".png"),Color.white);image.raycastTarget=true;
        var button=tr.GetComponent<UnityEngine.UI.Button>();
        var colours=ColorBlock.defaultColorBlock;colours.pressedColor=new Color(.8f,.88f,1,1); colours.disabledColor=new Color(.52f,.57f,.65f,.85f);colours.fadeDuration=.1f;
        button.colors=colours;
        var icon=tr.Find("Icon");
        if(icon && (tr.name.Contains("Replay") || tr.name.Contains("Restart")))
            Skin(icon.GetComponent<UnityEngine.UI.Image>(),S("Icon_PictoIcons/128/Pictoicon_Refresh.Png"),Color.white,false);
        foreach(var t in tr.GetComponentsInChildren<TextMeshProUGUI>(true)) {t.color=Color.white;if(size>0)t.fontSize=size;}
    }
    // The pack's nine-slice corners are drawn at a fixed pixel size, which on a 900-wide card
    // or an 850-wide button reads as a square corner. A multiplier under 1 draws the same
    // corner larger. Panels take it evenly. A Button01 is almost all top and bottom border, so
    // its corner height is fixed by the art and only the width grows -- hence a milder value.
    const float PanelCornerScale = 0.45f;
    const float ButtonCornerScale = 0.6f;

    static void RoundCorners()
    {
        foreach (var image in canvas.GetComponentsInChildren<UnityEngine.UI.Image>(true))
        {
            if (image.type != UnityEngine.UI.Image.Type.Sliced || image.sprite == null) continue;
            string n = image.sprite.name;
            if (n.StartsWith("BorderFrame"))
            {
                // Its corners are big, so a small plaque cannot take the full rounding: the
                // multiplier is raised until both pairs of borders fit inside the rect, or
                // Unity squeezes the corners flat.
                var border = image.sprite.border;
                var size = ((RectTransform)image.transform).rect.size;
                float fit = 0f;
                if (size.y > 1f) fit = Mathf.Max(fit, (border.y + border.w) / (size.y * 0.95f));
                if (size.x > 1f) fit = Mathf.Max(fit, (border.x + border.z) / (size.x * 0.95f));
                image.pixelsPerUnitMultiplier = Mathf.Max(PanelCornerScale, fit);
            }
            else if (n.StartsWith("Popup01") || n.StartsWith("BasicFrame_Round") || n.StartsWith("CardFrame"))
                image.pixelsPerUnitMultiplier = PanelCornerScale;
            else if (n.StartsWith("Button01"))
                image.pixelsPerUnitMultiplier = ButtonCornerScale;
            else continue;
            EditorUtility.SetDirty(image);
        }
    }

    static void Hide(Transform parent,string name) { var t=parent.Find(name); if(t) t.gameObject.SetActive(false); }
    static void Body(Transform t)
    {
        if(!t)return; var text=t.GetComponent<TextMeshProUGUI>(); if(!text)return;
        UseBodyFont(text);
        text.fontSize=38; text.color=Muted; text.alignment=TextAlignmentOptions.Center;
        text.textWrappingMode=TextWrappingModes.Normal;
    }
    static void UseBodyFont(TextMeshProUGUI text)
    {
        if(!text || !bodyFont)return;
        text.font=bodyFont;
        text.fontSharedMaterial=bodyFont.material;
        EditorUtility.SetDirty(text);
    }
    // A square icon button and a wide one, together exactly as wide as the primary above them.
    static void SecondaryRow(Transform card,string square,string wide)
    {
        Place(card.Find(square),.5f,0,-360+65,SecondaryRowY,130,130);
        Place(card.Find(wide),.5f,0,-360+130+24+283,SecondaryRowY,566,130);
    }
    static void Label(Transform t,string value) { t.GetComponentInChildren<TextMeshProUGUI>(true).text=value; }
    static Sprite S(string path) { var s=AssetDatabase.LoadAssetAtPath<Sprite>(Pack+"Sprites/Components/"+path); if(!s)throw new InvalidOperationException("Missing sprite: "+path); return s; }
    static Color C(string hex) { ColorUtility.TryParseHtmlString("#"+hex,out var c);return c; }
    static void Skin(UnityEngine.UI.Image image,Sprite sprite,Color colour,bool sliced=true)
    {if(!image)return; image.sprite=sprite;image.color=colour;image.type=sliced?UnityEngine.UI.Image.Type.Sliced:UnityEngine.UI.Image.Type.Simple;image.preserveAspect=!sliced;}
    static RectTransform Node(Transform parent,string name)
    {
        var old=parent.Find(name);if(old)return (RectTransform)old;
        var go=new GameObject(name,typeof(RectTransform));Undo.RegisterCreatedObjectUndo(go,"UI design detail");go.transform.SetParent(parent,false);return (RectTransform)go.transform;
    }
    static RectTransform Image(Transform parent,string name,string sprite,Color colour)
    {var tr=Node(parent,name);var im=tr.GetComponent<UnityEngine.UI.Image>()??tr.gameObject.AddComponent<UnityEngine.UI.Image>();Skin(im,S(sprite),colour);im.raycastTarget=false;return tr;}
    static void Text(Transform parent,string name,string value,float size,Color colour,float x,float y,float w,float h)
    {
        var tr=Node(parent,name);var t=tr.GetComponent<TextMeshProUGUI>()??tr.gameObject.AddComponent<TextMeshProUGUI>();
        t.font=font;t.fontSharedMaterial=font.material;t.text=value;t.fontSize=size;t.color=colour;t.alignment=TextAlignmentOptions.Center;t.raycastTarget=false;
        Place(tr,.5f,.5f,x,y,w,h);
    }
    static void Place(Transform tr,float ax,float ay,float x,float y,float w,float h)
    {var r=(RectTransform)tr;r.anchorMin=r.anchorMax=new Vector2(ax,ay);r.pivot=new Vector2(.5f,.5f);r.sizeDelta=new Vector2(w,h);r.anchoredPosition=new Vector2(x,y);r.localScale=Vector3.one;}
    static void Stretch(Transform tr,float inset=0)
    {var r=(RectTransform)tr;r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=Vector2.one*inset;r.offsetMax=-Vector2.one*inset;r.localScale=Vector3.one;}

    static void Preview(string panel)
    {
        if(!canvas)canvas=GameObject.Find("MainSystemn/UICanvas").transform;
        foreach(string n in new[]{"SafeArea","MainMenu","WinPanel","LosePanel","PausePanel","ShopPanel","SettingsPanel","PrivacyChoicesPanel","ModeBanner"})
        {var t=canvas.Find(n);t.gameObject.SetActive(n==panel || (n=="SafeArea" && panel!="MainMenu"));var g=t.GetComponent<CanvasGroup>();if(g)g.alpha=1;}
        EditorApplication.ExecuteMenuItem("Window/General/Game");
    }
    [MenuItem("SheepGame/UI/Preview/Main Menu")] public static void PreviewMenu()=>Preview("MainMenu");
    [MenuItem("SheepGame/UI/Preview/HUD")] public static void PreviewHud()=>Preview("SafeArea");
    [MenuItem("SheepGame/UI/Preview/Win")] public static void PreviewWin()=>Preview("WinPanel");
    [MenuItem("SheepGame/UI/Preview/Lose")] public static void PreviewLose()=>Preview("LosePanel");
    [MenuItem("SheepGame/UI/Preview/Pause")] public static void PreviewPause()=>Preview("PausePanel");
    [MenuItem("SheepGame/UI/Preview/Shop")] public static void PreviewShop()=>Preview("ShopPanel");
    [MenuItem("SheepGame/UI/Preview/Settings")] public static void PreviewSettings()=>Preview("SettingsPanel");
    [MenuItem("SheepGame/UI/Preview/Privacy Choices")] public static void PreviewPrivacy()=>Preview("PrivacyChoicesPanel");

    [MenuItem("SheepGame/UI/Capture Review")]
    public static void CaptureReview()=>CaptureReviewAt(2340);
    [MenuItem("SheepGame/UI/Capture Review 16x9")]
    public static void CaptureReviewShort()=>CaptureReviewAt(1920);
    static void CaptureReviewAt(int height)
    {
        canvas=GameObject.Find("MainSystemn/UICanvas").transform;
        var cv=canvas.GetComponent<Canvas>(); var camera=Camera.main;
        var mode=cv.renderMode; var worldCamera=cv.worldCamera; float distance=cv.planeDistance;
        var oldTarget=camera.targetTexture;float aspect=camera.aspect;var oldActive=RenderTexture.active;
        var rt=new RenderTexture(1080,height,24,RenderTextureFormat.ARGB32);
        var image=new Texture2D(1080,height,TextureFormat.RGB24,false);
        var cameraData=camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
        bool post=cameraData && cameraData.renderPostProcessing;
        string folder=System.IO.Path.GetFullPath("Temp/UIReview/"+height);System.IO.Directory.CreateDirectory(folder);
        try
        {
            // Screen-overlay UI normally bypasses camera colour grading.
            if(cameraData)cameraData.renderPostProcessing=false;
            camera.targetTexture=rt;camera.aspect=1080f/height;
            cv.renderMode=RenderMode.ScreenSpaceCamera;cv.worldCamera=camera;cv.planeDistance=camera.nearClipPlane+1;
            foreach(string panel in new[]{"MainMenu","WinPanel","LosePanel","PausePanel","ShopPanel","SettingsPanel","PrivacyChoicesPanel","ModeBanner","SafeArea"})
            {
                Preview(panel);
                foreach(var pop in canvas.GetComponentsInChildren<SheepGame.Core.UIPopIn>(true)) pop.transform.localScale=Vector3.one;
                Canvas.ForceUpdateCanvases();camera.Render();
                RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1080,height),0,0);image.Apply();
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder,panel+".png"),image.EncodeToPNG());
            }
        }
        finally
        {
            cv.renderMode=mode;cv.worldCamera=worldCamera;cv.planeDistance=distance;
            if(cameraData)cameraData.renderPostProcessing=post;
            camera.targetTexture=oldTarget;camera.aspect=aspect;RenderTexture.active=oldActive;
            UnityEngine.Object.DestroyImmediate(image);rt.Release();UnityEngine.Object.DestroyImmediate(rt);
            Preview("MainMenu");Canvas.ForceUpdateCanvases();
        }
        Debug.Log("UI review rendered to "+folder);
    }

    [MenuItem("SheepGame/UI/Check Runtime Navigation")]
    public static void CheckRuntimeNavigation()
    {
        if(!Application.isPlaying)throw new InvalidOperationException("Runtime check needs Play mode.");
        canvas=GameObject.Find("MainSystemn/UICanvas").transform;
        var menu=canvas.Find("MainMenu").GetComponent<SheepGame.Core.MainMenuPanel>();
        var bindings=canvas.GetComponent<SheepGame.Core.UIBindings>();

        // First-launch privacy has no Close button. Make a real choice and assert that the
        // modal gets out of the way immediately, before the SDK's next-frame initialization.
        if(menu.privacyPanel.root.activeSelf)
        {
            menu.privacyPanel.limitedButton.onClick.Invoke();
            if(menu.privacyPanel.root.activeSelf)throw new InvalidOperationException("Privacy choice did not close immediately.");
        }

        bool sound=SheepGame.Core.Meta.GameState.SoundOn;
        bool music=SheepGame.Core.Meta.GameState.MusicOn;
        bool vibration=SheepGame.Core.Meta.GameState.HapticsOn;
        menu.settingsButton.onClick.Invoke();
        if(!menu.settingsPanel.IsOpen)throw new InvalidOperationException("Settings did not open.");
        menu.settingsPanel.soundButton.onClick.Invoke();
        menu.settingsPanel.musicButton.onClick.Invoke();
        menu.settingsPanel.vibrationButton.onClick.Invoke();
        if(SheepGame.Core.Meta.GameState.SoundOn==sound || SheepGame.Core.Meta.GameState.MusicOn==music || SheepGame.Core.Meta.GameState.HapticsOn==vibration)
            throw new InvalidOperationException("One or more settings did not change.");
        menu.settingsPanel.soundButton.onClick.Invoke();
        menu.settingsPanel.musicButton.onClick.Invoke();
        menu.settingsPanel.vibrationButton.onClick.Invoke();
        menu.settingsPanel.privacyButton.onClick.Invoke();
        if(!menu.privacyPanel.root.activeSelf)throw new InvalidOperationException("Privacy Choices did not open from Settings.");
        menu.privacyPanel.closeButton.onClick.Invoke();
        if(menu.privacyPanel.root.activeSelf || !menu.settingsPanel.IsOpen)
            throw new InvalidOperationException("Privacy Choices did not return to Settings.");
        menu.settingsPanel.closeButton.onClick.Invoke();
        if(menu.settingsPanel.IsOpen)throw new InvalidOperationException("Settings did not close.");
        menu.settingsButton.onClick.Invoke();
        if(!menu.settingsPanel.IsOpen)throw new InvalidOperationException("Settings did not reopen.");
        menu.settingsPanel.closeButton.onClick.Invoke();

        menu.Show();menu.playButton.onClick.Invoke();
        if(menu.IsOpen || !menu.hudRoot.activeSelf)throw new InvalidOperationException("Play navigation failed.");
        bindings.pauseButton.onClick.Invoke();
        if(!bindings.pausePanel.IsOpen)throw new InvalidOperationException("Pause navigation failed.");
        bindings.pausePanel.resumeButton.onClick.Invoke();
        if(bindings.pausePanel.IsOpen)throw new InvalidOperationException("Resume navigation failed.");
        menu.Show();menu.shopButton.onClick.Invoke();
        if(!menu.shopPanel.IsOpen)throw new InvalidOperationException("Shop navigation failed.");
        Canvas.ForceUpdateCanvases();
        Debug.Log("UI navigation passed: Settings toggles, Privacy return, reopen; Play > HUD > Pause > Resume; Menu > Shop. Products: "+menu.shopPanel.listParent.childCount);
    }
}
