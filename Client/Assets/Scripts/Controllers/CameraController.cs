using UnityEngine;
using System;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.Universal;
using System.Collections;
using TMPro;

public class CameraController : MonoBehaviour
{
    [SerializeField]
    Define.CameraMode _mode = Define.CameraMode.QuaterView;

    Vector3 _farDelta = new Vector3(-4.0f, 15.0f, 5.0f);
    Vector3 _nearDelta = new Vector3(-4.0f, 6.0f, 5.0f);
    Vector3 _delta;
    float _lastZoom = 0f;
    float[] _zoomSteps = { 11f, 9f, 6f, 4f };
    int _currentStep = 0;
    float _currentZoom = 0f;
    float _targetZoom = 0f;
    bool _isLerpComplete = true;
    float _zoomSpeed = 0f;
    float _lerpSpeed = 0f;

    [SerializeField]
    GameObject _player = null;
    private Camera _mainCamera;
    private Camera _mapCamera;
    private Camera _uiCamera;

    public Action LateUpdateAction = null;

    public void SetPlayer(GameObject player) { _player = player; }

    void Start()
    {
        _mainCamera = Camera.main;

        if (GetComponent<PhysicsRaycaster>() == null)
            gameObject.AddComponent<PhysicsRaycaster>();

        _currentZoom = _zoomSteps[_currentStep];
        _targetZoom = _currentZoom;
        _lastZoom = _zoomSteps[_zoomSteps.Length - 1];
        _zoomSpeed = 10f;
        _lerpSpeed = 18f;

        SetupLayerCameras_URP();
    }

    void SetupLayerCameras_URP()
    {
        var mainCamData = _mainCamera.gameObject.GetOrAddComponent<UniversalAdditionalCameraData>();
        mainCamData.renderType = CameraRenderType.Base;
        mainCamData.cameraStack.Clear();
        _mainCamera.clearFlags = CameraClearFlags.SolidColor;

        int uiLayer = LayerMask.NameToLayer("IndicatorUI");

        int everythingMask = ~0;
        int layersToExclude = (1 << uiLayer) | (1 << LayerMask.NameToLayer("FogTeam1")) | (1 << LayerMask.NameToLayer("FogTeam2"));
        _mainCamera.cullingMask = everythingMask & ~layersToExclude;

        mainCamData.requiresDepthTexture = true;

        GameObject uiCamObj = new GameObject("UICamera");
        uiCamObj.transform.SetParent(this.transform);
        _uiCamera = uiCamObj.AddComponent<Camera>();
        _uiCamera.CopyFrom(_mainCamera);

        _uiCamera.clearFlags = CameraClearFlags.Nothing;
        _uiCamera.cullingMask = (1 << uiLayer);
        _uiCamera.depth = 10;

        var uiCamData = _uiCamera.gameObject.GetOrAddComponent<UniversalAdditionalCameraData>();
        uiCamData.renderType = CameraRenderType.Overlay;

        mainCamData.cameraStack.Add(_uiCamera);
    }

    void Update()
    {
        if (!_bSkillCam)
            DefaultMode();
    }
   
    void LateUpdate()
    {
        if (!_bSkillCam)
            LateDefaultMode();

        LateUpdateAction?.Invoke();
    }

    #region Default Mode
    private void DefaultMode()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll < 0f)
        {
            _currentStep = Mathf.Max(_currentStep - 1, 0);
            _targetZoom = _zoomSteps[_currentStep];
        }
        else if (scroll > 0f)
        {
            _currentStep = Mathf.Min(_currentStep + 1, _zoomSteps.Length - 1);
            _targetZoom = _zoomSteps[_currentStep];
        }

        if (_isLerpComplete)
        {
            _currentZoom = Mathf.MoveTowards(_currentZoom, _targetZoom, _zoomSpeed * Time.deltaTime);
        }
    }
    private void LateDefaultMode()
    {
        if (_mode == Define.CameraMode.QuaterView)
        {
            if (_player == null || !_player.activeSelf) // IsValid() 대신 null 또는 activeSelf 체크
            {
                return;
            }

            Vector3 targetDelta = (_currentZoom <= _lastZoom) ? _nearDelta : _farDelta;
            _delta = Vector3.MoveTowards(_delta, targetDelta, _lerpSpeed * Time.deltaTime);

            if (Vector3.Distance(_delta, targetDelta) < 0.01f)
                _isLerpComplete = true;
            else
                _isLerpComplete = false;

            Vector3 zoomedOffset = _delta.normalized * _currentZoom;
            transform.position = _player.transform.position + zoomedOffset;
            transform.LookAt(_player.transform.position + Vector3.up);
        }
    }
    #endregion

    #region Theodore D Skill Mode

    #region Variables
    public enum ScreenEdge
    {
        Left,
        Right,
        Top,
        Bottom
    }

    private const float VIEWPORT_PADDING = 0.1f;
    private Coroutine _zoomCoroutine = null;

    private float heightIncrease = 12.0f;

    private bool _bSkillCam = false;

    private Vector3 _originalPosition;
    private Quaternion _originalRotation;
    private Vector3 _originalDelta;
    #endregion

    public void StartAimMode(Transform playerTransform, ScreenEdge _direct)
    {
        if(_zoomCoroutine != null)
            StopCoroutine(_zoomCoroutine);

        _zoomCoroutine = StartCoroutine(CameraZoomOut(playerTransform, _direct));
    }

    private IEnumerator CameraZoomOut(Transform playerTransform, ScreenEdge targetEdge)
    {
        _bSkillCam = true;
        _originalPosition = transform.position;
        _originalRotation = transform.rotation;
        _originalDelta = _delta;

        switch (targetEdge)
        {
            case ScreenEdge.Left:
            case ScreenEdge.Right:
                heightIncrease = 7.0f;
                break;
            case ScreenEdge.Top:
            case ScreenEdge.Bottom:
                heightIncrease = 10.0f;
                break;
        }

        Vector3 toTarget = new Vector3();
        toTarget = transform.position;
        float targetYAxis = transform.position.y + heightIncrease;

        const float smoothFactor = 5.0f; 
        const float speed = 5.0f;
        while (_bSkillCam) 
        {
            Vector3 targetPosition = transform.position;
            targetPosition.y = targetYAxis; 

            targetPosition += playerTransform.forward * speed ;

            transform.position = Vector3.Lerp(
                transform.position,      
                targetPosition,          
                smoothFactor * Time.deltaTime 
            );

            Vector3 viewportPos = Camera.main.WorldToViewportPoint(playerTransform.position);
            if (viewportPos.x < VIEWPORT_PADDING || viewportPos.x > (1.0f - VIEWPORT_PADDING) ||
                viewportPos.y < VIEWPORT_PADDING || viewportPos.y > (1.0f - VIEWPORT_PADDING))
            {
                yield break;
            }
            yield return null;
        }
    }

    public void EndAimMode()
    {
        if (_zoomCoroutine != null)
            StopCoroutine(_zoomCoroutine);
        StartCoroutine(CameraZoomIn());
    }
    private IEnumerator CameraZoomIn()
    {
        Vector3 startPosition = transform.position;
        Quaternion startRotation = transform.rotation;
        const float ZOOM_DURATION = 0.8f;
        float elapsed = 0f;

        Transform playerTransform = Managers.Object.MyPlayer.transform;
        Vector3 finalTargetOffset = _originalDelta.normalized * _currentZoom;

        while (elapsed < ZOOM_DURATION)
        {
            elapsed += Time.deltaTime;

            float t = elapsed / ZOOM_DURATION;
            float smoothT = Mathf.SmoothStep(0f, 1f, t);

            Vector3 targetPosition = playerTransform.position + finalTargetOffset;

            transform.position = Vector3.Lerp(
                startPosition,  
                targetPosition,
                smoothT       
            );

            // 3. 회전 보간
            transform.rotation = Quaternion.Slerp(
                startRotation,
                _originalRotation,
                smoothT
            );

            yield return null;
        }

        Vector3 finalPosition = playerTransform.position + finalTargetOffset;

        transform.position = finalPosition;
        transform.rotation = _originalRotation;

        _delta = _originalDelta;
        _bSkillCam = false;
        _isLerpComplete = true;
    }
    #endregion
}