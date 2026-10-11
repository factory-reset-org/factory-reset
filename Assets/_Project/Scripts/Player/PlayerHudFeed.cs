using UnityEngine;
using ToyFactory.UI;

namespace ToyFactory.Player
{
    /// <summary>
    /// Tells the HUD what only the player's own systems know: how many wind-up toys are
    /// carried, and that a shot hit something. The HUD reads everything else itself, and has
    /// entry points for these two (<see cref="HudPresenter.SetToys"/>, <see cref="HudPresenter.ReportHit"/>).
    /// This lives with the player, in the default assembly, which can see the UI assembly, so
    /// no shared interface is needed.
    /// </summary>
    public sealed class PlayerHudFeed : MonoBehaviour
    {
        PlayerToyThrower _toys;
        PlayerBlaster _blaster;
        HudPresenter _fedHud;
        int _fedCarried = -1;
        int _fedMax = -1;

        void Awake()
        {
            _toys = GetComponent<PlayerToyThrower>();
            _blaster = GetComponent<PlayerBlaster>();
        }

        void OnEnable()
        {
            if (_blaster != null)
                _blaster.OnHitLanded += HandleHitLanded;
        }

        void OnDisable()
        {
            if (_blaster != null)
                _blaster.OnHitLanded -= HandleHitLanded;
        }

        // The HUD may be built after the player, or rebuilt with a scene, so what it was last
        // told is kept per HUD and sent again whenever it is a different one.
        void LateUpdate()
        {
            HudPresenter hud = HudPresenter.Current;
            if (hud == null || _toys == null)
                return;

            int carried = _toys.ToysCarried;
            int max = _toys.MaxToys;
            if (hud == _fedHud && carried == _fedCarried && max == _fedMax)
                return;

            hud.SetToys(carried, max);
            _fedHud = hud;
            _fedCarried = carried;
            _fedMax = max;
        }

        static void HandleHitLanded()
        {
            HudPresenter hud = HudPresenter.Current;
            if (hud != null)
                hud.ReportHit();
        }
    }
}
