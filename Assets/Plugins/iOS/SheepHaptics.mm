// The native half of SheepGame.Core.Haptics.
//
// Lives in Assets/Plugins/iOS because that is the path Unity recognises without any importer
// settings of its own; moving it under Assets/_Game with the rest of the game would need the
// platform ticking by hand in the inspector, and a plugin that silently applies to every
// platform is a Windows build that will not link.

#import <UIKit/UIKit.h>

// Generators are kept alive rather than made per call. Building one is not free, and — more
// to the point — prepare() only helps if the object that was prepared is the object that
// fires: a fresh generator each time is a generator that was never warmed.
static UIImpactFeedbackGenerator *_impact[5] = { nil, nil, nil, nil, nil };
static UINotificationFeedbackGenerator *_notice = nil;
static UISelectionFeedbackGenerator *_selection = nil;

static UIImpactFeedbackGenerator *ImpactGenerator(int style)
{
    if (style < 0 || style > 4) style = 0;
    if (_impact[style] == nil)
    {
        UIImpactFeedbackStyle mapped;
        switch (style)
        {
            case 1:  mapped = UIImpactFeedbackStyleMedium; break;
            case 2:  mapped = UIImpactFeedbackStyleHeavy;  break;
            // Soft and rigid arrived in iOS 13, which is this project's floor, so no
            // availability check is needed. Anything older would not have got this far.
            case 3:  mapped = UIImpactFeedbackStyleSoft;   break;
            case 4:  mapped = UIImpactFeedbackStyleRigid;  break;
            default: mapped = UIImpactFeedbackStyleLight;  break;
        }
        _impact[style] = [[UIImpactFeedbackGenerator alloc] initWithStyle:mapped];
    }
    return _impact[style];
}

extern "C"
{
    // Warms whatever might be asked for next. Cheap, and iOS puts the engine back to sleep
    // on its own if nothing follows.
    void _SheepHapticsPrepare(void)
    {
        [ImpactGenerator(0) prepare];
        [ImpactGenerator(1) prepare];
    }

    void _SheepHapticsImpact(int style)
    {
        UIImpactFeedbackGenerator *g = ImpactGenerator(style);
        [g impactOccurred];
        // Re-prepared straight away: in this game one tap very often leads to the next.
        [g prepare];
    }

    void _SheepHapticsNotice(int kind)
    {
        if (_notice == nil) _notice = [[UINotificationFeedbackGenerator alloc] init];

        UINotificationFeedbackType type;
        switch (kind)
        {
            case 1:  type = UINotificationFeedbackTypeWarning; break;
            case 2:  type = UINotificationFeedbackTypeError;   break;
            default: type = UINotificationFeedbackTypeSuccess; break;
        }
        [_notice notificationOccurred:type];
    }

    void _SheepHapticsSelection(void)
    {
        if (_selection == nil) _selection = [[UISelectionFeedbackGenerator alloc] init];
        [_selection selectionChanged];
        [_selection prepare];
    }
}
