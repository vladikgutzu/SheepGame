using UnityEngine;

namespace SheepGame.Core
{
    /// <summary>
    /// The handful of device settings that have to be made in code because the project
    /// settings cannot express them. Runs once, before any scene loads.
    /// </summary>
    public static class AppSetup
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Configure()
        {
            // On iOS Unity caps at 30 unless told otherwise, and vSync has to be off for
            // targetFrameRate to mean anything at all. A tap game at 30 feels like lag in the
            // one place it is most obvious: the moment between the tap and the case moving.
            //
            // The figure itself is DeviceRuntime's to choose, because it is not a constant on a
            // phone: it comes down in Low Power Mode and is capped by what the panel can show.
            QualitySettings.vSyncCount = 0;
            DeviceRuntime.RefreshFrameRate(force: true);

            // The board is read before it is touched, and reading it takes longer than the
            // system's idle timer allows. A screen that dims while the player is working out
            // which case to take is the single most irritating thing a puzzle game can do.
            // Handed to DeviceRuntime so it can be let go of again while nobody is playing —
            // a pause panel left up on a table should not hold the display on.
            DeviceRuntime.KeepAwake = true;

            // Nothing here needs to run while backgrounded, and on a phone it would only cost
            // battery for a game that is paused anyway.
            Application.runInBackground = false;
        }
    }
}
