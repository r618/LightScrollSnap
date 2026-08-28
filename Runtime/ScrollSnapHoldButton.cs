using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace LightScrollSnap
{
    /// <summary>
    /// Pointer hold support for ScrollSnap navigation buttons.
    /// Steps one item at a time while the pointer is down and stops immediately on release; the pace is
    /// ScrollSnap's holdRepeatInterval, which also gates the button's own click so the two cannot stack.
    /// </summary>
    public class ScrollSnapHoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [SerializeField] private ScrollSnap scrollSnap;
        [SerializeField] private int direction = 1; // +1 next, -1 previous
        [SerializeField] private float suppressClickAfterHoldSeconds = 0.08f;

        /// <summary>
        /// Held per pointer id, so a synthetic pointer releasing does not cancel a hold that another
        /// pointer (real mouse, second touch) is still driving.
        /// </summary>
        readonly HashSet<int> heldPointers = new HashSet<int>();
        float holdStartedAt;

        bool Held => heldPointers.Count > 0;

        public void Configure(ScrollSnap snap, int dir, float suppressClickAfterSeconds)
        {
            scrollSnap = snap;
            direction = dir >= 0 ? 1 : -1;
            suppressClickAfterHoldSeconds = Mathf.Max(0f, suppressClickAfterSeconds);
        }

        void Update()
        {
            if (!Held || scrollSnap == null)
                return;

            scrollSnap.HoldScrollToAdjacentItem(direction);
        }

        void Release(int pointerId)
        {
            if (!heldPointers.Remove(pointerId) || Held)
                return;

            if (scrollSnap != null)
                scrollSnap.SetExternalPointerPressed(false);
        }

        void ReleaseAll()
        {
            if (!Held)
                return;

            heldPointers.Clear();
            if (scrollSnap != null)
                scrollSnap.SetExternalPointerPressed(false);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (scrollSnap == null || eventData == null)
                return;

            if (!Held)
                holdStartedAt = Time.unscaledTime;

            heldPointers.Add(eventData.pointerId);
            scrollSnap.SetExternalPointerPressed(true);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData == null)
            {
                ReleaseAll();
                return;
            }

            if (Time.unscaledTime - holdStartedAt >= suppressClickAfterHoldSeconds)
                eventData.eligibleForClick = false;

            Release(eventData.pointerId);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (eventData == null)
            {
                ReleaseAll();
                return;
            }

            eventData.eligibleForClick = false;
            Release(eventData.pointerId);
        }

        void OnDisable() => ReleaseAll();
    }
}
