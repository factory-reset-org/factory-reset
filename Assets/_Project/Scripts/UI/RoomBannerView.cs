using UnityEngine;
using UnityEngine.UI;

namespace ToyFactory.UI
{
    /// <summary>
    /// The room's name at the top centre, shown for <see cref="HudMath.RoomBannerSeconds"/> when the
    /// player enters a room. Showing the same name again while it is up does nothing.
    /// </summary>
    internal sealed class RoomBannerView
    {
        CanvasGroup _group;
        Text _text;
        string _room = string.Empty;
        float _shownAt = float.NegativeInfinity;

        /// <summary>The room last shown.</summary>
        public string Room => _room;

        /// <summary>True while the banner is on screen.</summary>
        public bool IsShowing => Time.unscaledTime - _shownAt < HudMath.RoomBannerSeconds;

        /// <summary>The banner's opacity.</summary>
        public float CurrentAlpha => _group != null ? _group.alpha : 0f;

        /// <summary>The text on the banner, for tests.</summary>
        public string Text => _text != null ? _text.text : string.Empty;

        public void Build(Transform parent)
        {
            Image plate = HudWidgets.Plate("Room", parent);
            HudWidgets.Place(plate.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -32f), new Vector2(600f, 76f));
            _group = plate.gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;

            _text = HudWidgets.Label("Name", plate.transform, string.Empty, 40, HudWidgets.Ink, TextAnchor.MiddleCenter);
            _text.rectTransform.offsetMin = new Vector2(10f, 4f);
            _text.rectTransform.offsetMax = new Vector2(-10f, -4f);
        }

        /// <summary>Announces a room. Ignored if it is the room already announced and still up.</summary>
        public void Show(string room)
        {
            if (string.IsNullOrEmpty(room) || _group == null)
                return;
            if (room == _room && IsShowing)
                return;

            _room = room;
            _text.text = room.ToUpperInvariant();
            _shownAt = Time.unscaledTime;
            _group.alpha = 0f;
        }

        /// <summary>Hides the banner at once.</summary>
        public void Hide()
        {
            _shownAt = float.NegativeInfinity;
            if (_group != null)
                _group.alpha = 0f;
        }

        /// <summary>Fades the banner; call every frame.</summary>
        public void Tick()
        {
            if (_group == null)
                return;

            float alpha = HudMath.RoomBannerAlpha(Time.unscaledTime - _shownAt);
            if (!Mathf.Approximately(alpha, _group.alpha))
                _group.alpha = alpha;
        }
    }
}
