using UnityEngine;
using ToyFactory.Runtime.World;
using ToyFactory.UI;

namespace ToyFactory.Player
{
    /// <summary>
    /// Tells the HUD and the score what only the player's own systems know: how many wind-up
    /// toys are carried, that a shot hit something, which room the player stands in, and how many
    /// shots were fired and hit (for the results accuracy). The HUD reads everything else itself,
    /// and has entry points for these (<see cref="HudPresenter.SetToys"/>, <see cref="HudPresenter.ReportHit"/>,
    /// <see cref="HudPresenter.SetRoom"/>, <see cref="ScoreManager.ReportShots"/>). The prompt line
    /// is fed by <see cref="PlayerInteractor"/>, which knows what is in reach.
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
        string _room = string.Empty;
        HudPresenter _roomHud;
        int _shotsFired;
        int _shotsHit;

        void Awake()
        {
            _toys = GetComponent<PlayerToyThrower>();
            _blaster = GetComponent<PlayerBlaster>();
        }

        void OnEnable()
        {
            if (_blaster != null)
            {
                _blaster.OnHitLanded += HandleHitLanded;
                _blaster.OnFired += HandleFired;
            }
        }

        void OnDisable()
        {
            if (_blaster != null)
            {
                _blaster.OnHitLanded -= HandleHitLanded;
                _blaster.OnFired -= HandleFired;
            }
        }

        // The HUD may be built after the player, or rebuilt with a scene, so what it was last
        // told is kept per HUD and sent again whenever it is a different one.
        void LateUpdate()
        {
            HudPresenter hud = HudPresenter.Current;
            if (hud == null)
                return;

            FeedRoom(hud);
            if (_toys == null)
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

        // The room name is sent when the player crosses into another room, and again to a new HUD.
        void FeedRoom(HudPresenter hud)
        {
            Vector3 p = transform.position;
            string room = FactoryRooms.At(p.x, p.z, _room);
            if (room == _room && hud == _roomHud)
                return;

            _room = room;
            _roomHud = hud;
            hud.SetRoom(room);
        }

        void HandleFired()
        {
            _shotsFired++;
            ReportShots();
        }

        void HandleHitLanded()
        {
            _shotsHit++;
            ReportShots();

            HudPresenter hud = HudPresenter.Current;
            if (hud != null)
                hud.ReportHit();
        }

        void ReportShots()
        {
            ScoreManager score = ScoreManager.Current;
            if (score != null)
                score.ReportShots(_shotsFired, _shotsHit);
        }
    }
}
