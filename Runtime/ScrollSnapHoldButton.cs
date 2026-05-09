using UnityEngine;
using UnityEngine.EventSystems;

namespace LightScrollSnap
{
    /// <summary>
    /// Pointer hold support for ScrollSnap navigation buttons.
    /// Keeps movement active while pointer is down and stops immediately on release.
    /// </summary>
    public class ScrollSnapHoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [SerializeField] private ScrollSnap scrollSnap;
        [SerializeField] private int direction = 1; // +1 next, -1 previous
        [SerializeField] private float itemsPerSecond = 2.5f;
        [SerializeField] private float suppressClickAfterHoldSeconds = 0.08f;

        bool held;
        float holdStartedAt;

        public void Configure(ScrollSnap snap, int dir, float itemRate, float suppressClickAfterSeconds)
        {
            scrollSnap = snap;
            direction = dir >= 0 ? 1 : -1;
            itemsPerSecond = Mathf.Max(0.2f, itemRate);
            suppressClickAfterHoldSeconds = Mathf.Max(0f, suppressClickAfterSeconds);
        }

        void Update()
        {
            if (!held || scrollSnap == null)
                return;

            scrollSnap.HoldScrollByItems(direction * itemsPerSecond * Time.unscaledDeltaTime);
        }

        void Release()
        {
            if (!held)
                return;

            held = false;
            if (scrollSnap != null)
                scrollSnap.SetExternalPointerPressed(false);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (scrollSnap == null)
                return;

            held = true;
            holdStartedAt = Time.unscaledTime;
            scrollSnap.SetExternalPointerPressed(true);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            var heldDuration = Time.unscaledTime - holdStartedAt;
            if (eventData != null && heldDuration >= suppressClickAfterHoldSeconds)
                eventData.eligibleForClick = false;

            Release();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (eventData != null)
                eventData.eligibleForClick = false;

            Release();
        }

        void OnDisable() => Release();
    }
}
