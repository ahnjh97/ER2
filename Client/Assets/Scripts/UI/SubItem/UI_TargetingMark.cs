using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;


public class UI_TargetingMark : MonoBehaviour
{
    private Coroutine _lifetimeCoroutine;
    [SerializeField] private Image image;
    private Canvas canvas; // SerializeField 제거

    private Transform _currentTarget;
    private bool _isTracking = false;

    private void Awake()
    {
        if (image == null)
            image = GetComponentInChildren<Image>();

        if (canvas == null)
            canvas = GetComponentInParent<Canvas>();
    }

    private void Update()
    {
        
    }

    void OnEnable()
    {
        Canvas.willRenderCanvases += UpdatePosition;
    }

    void OnDisable()
    {
        image.enabled = false;
        Canvas.willRenderCanvases -= UpdatePosition;
    }

    private void UpdatePosition()
    {
        if (_isTracking && _currentTarget != null)
        {
            if (canvas == null)
                return;

            Vector3 targetWorldPos = _currentTarget.position + new Vector3(0, 1.8f, 0);
            RectTransform rectTransform = transform as RectTransform;

            if (rectTransform == null)
                return;

            if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                Vector3 screenPos = Camera.main.WorldToScreenPoint(targetWorldPos);

                if (screenPos.z < 0)
                {
                    image.enabled = false;
                    return;
                }

                image.enabled = true;

                Vector2 screenDirection = Vector2.up;

                float screenOffset = 115f;
                Vector2 newTagScreenPos = new Vector2(screenPos.x, screenPos.y) + (screenDirection * screenOffset);

                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvas.GetComponent<RectTransform>(),
                    newTagScreenPos,
                    null,
                    out Vector2 localPoint
                );

                rectTransform.anchoredPosition = localPoint;
            }
            else if (canvas.renderMode == RenderMode.ScreenSpaceCamera)
            {
                Vector3 screenPos = Camera.main.WorldToScreenPoint(targetWorldPos);

                if (screenPos.z < 0)
                {
                    image.enabled = false;
                    return;
                }

                image.enabled = true;

                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvas.GetComponent<RectTransform>(),
                    screenPos,
                    canvas.worldCamera,
                    out Vector2 localPoint
                );

                rectTransform.anchoredPosition = localPoint;
            }
            else
            {
                transform.position = targetWorldPos;
                if (Camera.main != null)
                    transform.rotation = Quaternion.LookRotation(transform.position - Camera.main.transform.position);
            }
        }
    }

    public void Show(GameObject target, float duration, Action onComplete)
    {
        _currentTarget = target.transform;
        _isTracking = true;

        if (_lifetimeCoroutine != null)
            StopCoroutine(_lifetimeCoroutine);

        _lifetimeCoroutine = StartCoroutine(RenderForTime(duration));
    }
    IEnumerator RenderForTime(float duration)
    {
        if (image != null)
        {
            image.enabled = true;
        }

        yield return new WaitForSeconds(duration);

        if (image != null)
            image.enabled = false;

        _isTracking = false;
        _currentTarget = null;
    }

    public void Hide()
    {
        if (_lifetimeCoroutine != null)
        {
            StopCoroutine(_lifetimeCoroutine);
            _lifetimeCoroutine = null;
        }

        _isTracking = false;
        _currentTarget = null;

        if (image != null)
            image.enabled = false;
    }
}