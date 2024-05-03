using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace LightScrollSnap
{
    public class ScrollItemDownHandler : MonoBehaviour, IPointerDownHandler
    {
        private event Action _downListener;
        private void OnDestroy() => RemoveAllListeners();

        void IPointerDownHandler.OnPointerDown(PointerEventData eventData) => _downListener?.Invoke();

        public void AddDownListener(Action downListener) => _downListener += downListener;

        public void RemoveDownListener(Action downListener) => _downListener -= downListener;

        public void RemoveAllListeners() => _downListener = null;
    }
}