using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.UIElements;

namespace LightScrollSnap
{
    [RequireComponent(typeof(ScrollRect))]
    public class ScrollSnap : MonoBehaviour
    {
        #region INSPECTOR PROPERTIES

        [SerializeField] private DeltaTimeMode deltaTimeMode = DeltaTimeMode.Unscaled;

        [Header("Scroll Settings")]
        [SerializeField]
        private Scrollbar scrollbar;

        [SerializeField][Range(0, 1)] private float initialPos;
        public bool autoScrollToClickedItem = true;
        public float smoothScrollDuration = 0.35f;
        public float smoothSnapDuration = 0.25f;

        [Header("Hold Button Settings")]
        public bool enableHoldButtonScroll = true;
        /// <summary>
        /// Seconds between two item scrolls while a navigation button is held down. Also gates the click
        /// the button itself fires, so holding advances one item per interval no matter which of the two
        /// asks for it.
        /// </summary>
        [Range(0.05f, 5f)] public float holdRepeatInterval = 1f;
        /// <summary>
        /// Whether a held button jumps to the other end once it runs out of items.
        /// </summary>
        public bool holdWrapAround = true;
        [Range(0f, 0.5f)] public float holdSuppressClickAfterSeconds = 0.08f;

        [Header("Snap Settings")]
        [SerializeField]
        private float snapDelayDuration = 0.15f;

        [SerializeField] private float snapDistanceThreshold = 0.001f;
        /// <summary>
        /// made public so effect(s) such as ScaleEfect can be added/removed dynamically:
        /// while moving (e.g. sliding to screen) displacement in OnItemUpdated seems to be alternating between some extremes only resulting in flickering for ScaleEffect
        /// </summary>
        [Header("Effect Settings")]
        public List<BaseScrollSnapEffect> effects;

        #endregion

        #region PRIVATE PROPERTIES

        private float _scrollPos;
        private float[] _posses;
        private float _distance;
        private int _itemCount;
        private Coroutine _smoothScrollingCoroutine;
        private Coroutine _snapToNearestCoroutine;
        private bool _smoothScrolling;
        private int _selectedItemIndex;
        private float DeltaTime => deltaTimeMode == DeltaTimeMode.Scaled ? Time.deltaTime : Time.unscaledDeltaTime;
        private ScrollRect _scrollRect;
        private bool _snapping;
        private bool _externalPointerPressed;
        private bool _holdButtonScrollEnabled;
        private bool _holdScrolling;
        private float _holdReleasedAt = float.NegativeInfinity;
        private float _lastItemScrollAt = float.NegativeInfinity;
        /// <summary>
        /// A held button asks for a step every frame and fires a click of its own on release; both go
        /// through here, so only the first one per <see cref="holdRepeatInterval"/> moves the list.
        /// The grace period covers the click, which arrives just after the pointer is released.
        /// </summary>
        private bool HoldRepeatBlocked => (_externalPointerPressed || Time.unscaledTime - _holdReleasedAt < holdReleaseClickGrace)
                                          && Time.unscaledTime - _lastItemScrollAt < holdRepeatInterval;
        private const float holdReleaseClickGrace = 0.2f;
        private bool Snapped => Mathf.Abs(_nearestPos - scrollbar.value) <= snapDistanceThreshold;
        private List<RectTransform> _items;
        private List<ScrollItemClickHandler> _itemClickHandlers;
        private int _nearestIndex;
        private float _nearestPos;
        private bool HasItem => _items != null && _items.Count > 0;

        #endregion

        #region PUBLIC EVENTS

        [Header("Unity Events")] public UnityEvent<RectTransform, int> OnItemSelected;
        public UnityEvent<RectTransform, int> OnItemDeSelected;
        public UnityEvent<int, RectTransform> OnItemClicked;
        public UnityEvent<int, RectTransform> OnItemDown;

        #endregion

        #region PUBLIC PROPERTIES

        public int SelectedItemIndex => _selectedItemIndex;
        public int NearestItemIndex => _nearestIndex;
        public RectTransform Content => _scrollRect.content;
        public RectTransform NearestItem => _items[_nearestIndex];
        public RectTransform SelectedItem => _items[_selectedItemIndex];
        public List<RectTransform> Items => _items;

        #endregion

        #region UNITY METHODS

        protected virtual void Awake() => Setup();

        protected virtual void Update() => UpdateAll();

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!Application.isPlaying)
                OnInitialPosChanged();
        }
#endif

        #endregion

        #region PRIVATE METHODS

        private void Setup()
        {
            _scrollRect = GetComponent<ScrollRect>();
            SetupItems();
            SetupHoldButtons();
        }

        private void SetupItems()
        {
            _itemCount = Content.childCount;
            _posses = new float[_itemCount];

            _distance = _itemCount > 1 ? 1f / (_itemCount - 1f) : 1;
            _items = new List<RectTransform>(_itemCount);
            for (int i = 0; i < _itemCount; i++)
            {
                _items.Add(Content.GetChild(i).GetComponent<RectTransform>());
                _posses[i] = _distance * i;
            }

            SetupClickHandlers();
        }

        private void SetupClickHandlers()
        {
            _itemClickHandlers = new List<ScrollItemClickHandler>(_itemCount);
            for (int i = 0; i < _itemCount; i++)
            {
                var item = _items[i];
                var clickHandler = item.gameObject.AddComponent<ScrollItemClickHandler>();
                var index = i;
                clickHandler.AddClickListener(() => OnAnyItemClicked(index, item));
                _itemClickHandlers.Add(clickHandler);

                var downHandler = item.gameObject.AddComponent<ScrollItemDownHandler>();
                downHandler.AddDownListener(() => OnAnyItemDown(index, item));
            }
        }

        private void OnAnyItemClicked(int index, RectTransform item)
        {
            OnItemClicked?.Invoke(index, item);
            if (autoScrollToClickedItem)
                SmoothScrollToItem(index);
        }

        private void OnAnyItemDown(int index, RectTransform item)
        {
            OnItemDown?.Invoke(index, item);
        }

        private void OnInitialPosChanged()
        {
            if (scrollbar != null && scrollbar.value != initialPos)
                ScrollTo(initialPos);
        }

        private void UpdateNearest()
        {
            var nearest = GetNearestIndex();
            if (nearest != -1)
                _nearestIndex = nearest;

            _nearestPos = _posses[_nearestIndex];
        }

        private void UpdateAll()
        {
            UpdateItemsIfChanged();

            if (!HasItem)
                return;

            _scrollPos = scrollbar.value;
            UpdateNearest();

            var leftMouseButtonPressed = InputHelper.MouseButtonPressed(MouseButton.LeftMouse);
            // a hold driven scroll is allowed to finish - it is the pointer being down that started it
            if ((leftMouseButtonPressed || _externalPointerPressed) && !_holdScrolling)
            {
                ClearSmoothScrolling();
                _snapping = false;
            }
            else if (!_smoothScrolling && !_snapping && !Snapped)
                SnapToNearest();

            HandleItemsStates();
            ApplyEffects();
        }

        private void UpdateItemsIfChanged()
        {
            var childCount = Content.childCount;
            var childCountChanged = _itemCount != childCount;
            var contentChanged = childCountChanged;
            if (!childCountChanged && HasItem)
            {
                for (int i = 0; i < _itemCount; i++)
                {
                    var item = _items[i];
                    var child = Content.GetChild(i);
                    if (item != child)
                    {
                        contentChanged = true;
                        break;
                    }
                }
            }

            if (contentChanged)
            {
                SetupItems();
            }
        }

        private IEnumerator SnapToNearestCoroutine()
        {
            yield return new WaitForSecondsRealtime(snapDelayDuration);
            SmoothScrollTo(_nearestPos, smoothSnapDuration);
        }

        private void SnapToNearest()
        {
            _snapping = true;
            if (_snapToNearestCoroutine != null)
                StopCoroutine(_snapToNearestCoroutine);

            _snapToNearestCoroutine = StartCoroutine(SnapToNearestCoroutine());
        }

        private int GetNearestIndex()
        {
            if (_items.Count <= 1)
                return 0;

            for (int i = 0; i < _itemCount; i++)
            {
                var pos = _posses[i];
                if (Math.Abs(_scrollPos - pos) <= _distance / 2)
                    return i;
            }

            return -1;
        }

        private IEnumerator SmoothScroll(float ratio, float seconds)
        {
            _smoothScrolling = true;
            ratio = Mathf.Clamp01(ratio);

            float t = 0.0f;
            while (t <= 1.0f)
            {
                t += DeltaTime / seconds;
                scrollbar.value = Mathf.Lerp(scrollbar.value, ratio, Mathf.SmoothStep(0f, 1f, t));
                yield return null;
            }

            _smoothScrolling = false;

            OnSmoothScrollEnded();
        }

        private void OnSmoothScrollEnded()
        {
            _snapping = false;
            _holdScrolling = false;
        }

        private void HandleItemsStates()
        {
            var selected = _items[_nearestIndex];
            if (_nearestIndex != _selectedItemIndex)
            {
                SelectItem(selected, _nearestIndex);
                UnselectItems(_nearestIndex);
            }
        }

        private void ApplyEffects()
        {
            for (int i = 0; i < _itemCount; i++)
                ApplyItemEffects(_items[i], _posses[i]);
        }

        private void ApplyItemEffects(RectTransform item, float pos)
        {
            var count = effects.Count;
            for (int i = 0; i < count; i++)
            {
                var effect = effects[i];
                var displacement = GetEffectDisplacementBasedOnPos(pos, effect);
                effect.OnItemUpdated(item, displacement);
            }
        }

        private float GetEffectDisplacementBasedOnPos(float pos, BaseScrollSnapEffect effect)
        {
            var signedDist = (pos - _scrollPos) / (_distance * effect.effectedDistanceBasedOnItemSize);
            return Mathf.Clamp(signedDist, -1, 1);
        }

        private void UnselectItems(int selectedIntex)
        {
            for (int i = 0; i < _itemCount; i++)
            {
                if (i != selectedIntex)
                {
                    var unselected = _items[i];
                    UnselectItem(unselected, i);
                }
            }
        }

        private void SelectItem(RectTransform rt, int index)
        {
            _selectedItemIndex = index;
            OnItemSelected?.Invoke(rt, index);
        }

        private void UnselectItem(RectTransform rt, int index)
        {
            OnItemDeSelected?.Invoke(rt, index);
        }

        private void ClearSnapping()
        {
            _snapping = false;
            if (_snapToNearestCoroutine != null)
                StopCoroutine(_snapToNearestCoroutine);
        }

        /// <summary>
        /// Attaches <see cref="ScrollSnapHoldButton"/> to whatever component drives navigation, by looking
        /// for a UnityEvent whose persistent calls point back at this ScrollSnap. Not restricted to
        /// UnityEngine.UI.Button - custom buttons (Lean.Gui.LeanButton and friends) have their own click
        /// event field and would otherwise never get hold support.
        /// </summary>
        private void SetupHoldButtons()
        {
            if (_holdButtonScrollEnabled || !enableHoldButtonScroll)
                return;

            MonoBehaviour[] behaviours;
#if UNITY_2022_2_OR_NEWER
            behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            behaviours = FindObjectsOfType<MonoBehaviour>(true);
#endif
            var events = new List<UnityEventBase>(4);

            for (int i = 0; i < behaviours.Length; i++)
            {
                var behaviour = behaviours[i];
                if (behaviour == null || behaviour == this)
                    continue;

                CollectEvents(behaviour, events);

                for (int j = 0; j < events.Count; j++)
                {
                    var direction = GetHoldDirection(events[j]);
                    if (direction == 0)
                        continue;

                    var hold = behaviour.GetComponent<ScrollSnapHoldButton>();
                    if (hold == null)
                        hold = behaviour.gameObject.AddComponent<ScrollSnapHoldButton>();
                    hold.Configure(this, direction, holdSuppressClickAfterSeconds);
                    _holdButtonScrollEnabled = true;
                    break;
                }
            }
        }

        private void CollectEvents(MonoBehaviour behaviour, List<UnityEventBase> into)
        {
            into.Clear();

            for (var type = behaviour.GetType(); type != null && type != typeof(MonoBehaviour); type = type.BaseType)
            {
                var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                for (int i = 0; i < fields.Length; i++)
                {
                    if (!typeof(UnityEventBase).IsAssignableFrom(fields[i].FieldType))
                        continue;

                    var unityEvent = fields[i].GetValue(behaviour) as UnityEventBase;
                    if (unityEvent != null)
                        into.Add(unityEvent);
                }
            }
        }

        private int GetHoldDirection(UnityEventBase unityEvent)
        {
            var count = unityEvent.GetPersistentEventCount();
            for (int i = 0; i < count; i++)
            {
                if (unityEvent.GetPersistentTarget(i) != this)
                    continue;

                var method = unityEvent.GetPersistentMethodName(i);
                if (method == nameof(SmoothScrollToNextItem) || method == nameof(ScrollToNextItem))
                    return 1;
                if (method == nameof(SmoothScrollToPreviousItem) || method == nameof(ScrollToPreviousItem))
                    return -1;
            }

            return 0;
        }

        private bool IsIndexInRange(int index) => index >= 0 && index <= _items.Count - 1;

        #endregion

        #region PUBLIC METHODS

        public void ScrollTo(float ratio)
        {
            ClearSmoothScrolling();
            ClearSnapping();
            scrollbar.value = Mathf.Clamp01(ratio);
        }

        public void ScrollToNextItem() => ScrollToItem(_selectedItemIndex + 1);

        public void ScrollToPreviousItem() => ScrollToItem(_selectedItemIndex - 1);

        public void SmoothScrollToNextItem() => SmoothScrollToItem(_selectedItemIndex + 1);

        public void SmoothScrollToPreviousItem() => SmoothScrollToItem(_selectedItemIndex - 1);

        public void ScrollToItem(int itemIndex)
        {
            if (HoldRepeatBlocked || !IsIndexInRange(itemIndex))
                return;

            _lastItemScrollAt = Time.unscaledTime;
            ScrollTo(_posses[itemIndex]);
        }

        public void SmoothScrollToItem(int itemIndex, float duration)
        {
            if (HoldRepeatBlocked || !IsIndexInRange(itemIndex))
                return;

            _lastItemScrollAt = Time.unscaledTime;
            SmoothScrollTo(_posses[itemIndex], duration);
        }

        public void SmoothScrollToItem(int itemIndex)
        {
            SmoothScrollToItem(itemIndex, smoothScrollDuration);
        }

        /// <summary>
        /// One step of a held navigation button: scrolls to the neighbouring item, or does nothing while
        /// the previous step is still inside <see cref="holdRepeatInterval"/>.
        /// </summary>
        public void HoldScrollToAdjacentItem(int direction)
        {
            if (!HasItem || HoldRepeatBlocked)
                return;

            var itemIndex = _selectedItemIndex + (direction >= 0 ? 1 : -1);
            if (!IsIndexInRange(itemIndex))
            {
                if (!holdWrapAround)
                    return;

                itemIndex = itemIndex < 0 ? _items.Count - 1 : 0;
            }

            _lastItemScrollAt = Time.unscaledTime;
            SmoothScrollTo(_posses[itemIndex], smoothScrollDuration);
            _holdScrolling = true;
        }

        public void SmoothScrollTo(float ratio, float seconds)
        {
            ClearSmoothScrolling();
            ClearSnapping();

            _smoothScrollingCoroutine = StartCoroutine(SmoothScroll(ratio, seconds));
        }

        public void SmoothScrollTo(float ratio)
        {
            SmoothScrollTo(ratio, smoothScrollDuration);
        }

        public void ClearSmoothScrolling()
        {
            _smoothScrolling = false;
            _holdScrolling = false;
            if (_smoothScrollingCoroutine != null)
                StopCoroutine(_smoothScrollingCoroutine);
        }

        public void SetExternalPointerPressed(bool pressed)
        {
            // a press that starts well after the previous one was released is a new click, not the next
            // repeat of a hold, so it is never held back by the interval
            if (pressed && !_externalPointerPressed && Time.unscaledTime - _holdReleasedAt > holdReleaseClickGrace)
                _lastItemScrollAt = float.NegativeInfinity;

            _externalPointerPressed = pressed;
            if (!pressed)
            {
                _holdReleasedAt = Time.unscaledTime;
                if (_scrollRect != null)
                    _scrollRect.velocity = Vector2.zero;
            }
        }

        public float GetScrollPositionOfItem(int itemIndex) => _posses[itemIndex];

        public void AddItemClickListener(int itemIndex, Action clickListener)
        {
            if (IsIndexInRange(itemIndex))
                _itemClickHandlers[itemIndex].AddClickListener(clickListener);
        }

        public void RemoveItemClickListener(int itemIndex, Action clickListener)
        {
            if (IsIndexInRange(itemIndex))
                _itemClickHandlers[itemIndex].RemoveClickListener(clickListener);
        }

        #endregion
    }
}
