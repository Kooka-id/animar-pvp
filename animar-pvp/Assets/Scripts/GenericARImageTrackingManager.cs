using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

[Serializable]
public class TextMapping
{
    public string imageName;
    public GameObject textObject;
}

public class GenericARImageTrackingManager : MonoBehaviour
{
    [Header("Configuration")]
    [SerializeField] private ARTrackingConfig config;

    [Header("Scene UI References")]
    [SerializeField] private GameObject playButton;
    [SerializeField] private GameObject scanText;
    [SerializeField] private List<TextMapping> textMappings = new List<TextMapping>();

    [Header("Tracking Stability")]
    [Tooltip("Delay before deactivating lost objects (prevents flickering)")]
    [SerializeField] private float deactivationDelay = 0.5f;
    [Tooltip("Smooth position/rotation changes")]
    [SerializeField] private bool enableSmoothing = true;
    [Tooltip("Position smoothing speed")]
    [SerializeField] private float positionSmoothSpeed = 10f;
    [Tooltip("Minimum tracking quality to activate object")]
    [SerializeField] private bool requireLimitedTracking = true;

    private ARTrackedImageManager _arTrackedImageManager;
    private Dictionary<string, GameObject> _arObjects = new Dictionary<string, GameObject>();
    private Dictionary<string, GameObject> _textPrefabs = new Dictionary<string, GameObject>();
    private Dictionary<string, Quaternion> _currentManualRotations = new Dictionary<string, Quaternion>();
    
    // Anti-flickering
    private Dictionary<string, float> _lostTimestamps = new Dictionary<string, float>();
    private Dictionary<string, Vector3> _targetPositions = new Dictionary<string, Vector3>();
    private Dictionary<string, Quaternion> _targetRotations = new Dictionary<string, Quaternion>();

    private Animator _currentAnimator;
    private string _currentAnimationState;
    private int _activeImageCount = 0;

    private bool _isRotating = false;
    private Vector2 _lastTouchPosition;
    private GameObject _currentRotatingObject;
    private Camera _arCamera;

    private float _lastTapTime = 0f;
    private Vector2 _lastTapPosition;
    private GameObject _lastTappedObject;

    #region Unity Lifecycle

    private void Awake()
    {
        ValidateConfiguration();
        InitializeComponents();
        SetupARCamera();
    }

    private void Start()
    {
        _arTrackedImageManager.trackedImagesChanged += OnTrackedImageChanged;
        InitializeUI();
        InstantiateARObjects();
    }

    private void Update()
    {
        if (config.enableManualRotation && _arCamera != null)
        {
            HandleRotationInput();
        }

        // ✅ Cuma smooth kalau ga lagi rotate
        if (enableSmoothing && !_isRotating)
        {
            SmoothObjectTransforms();
        }

        CheckDeactivationDelays();
    }

    private void OnDestroy()
    {
        if (_arTrackedImageManager != null)
        {
            _arTrackedImageManager.trackedImagesChanged -= OnTrackedImageChanged;
        }
    }

    #endregion

    #region Initialization

    private void ValidateConfiguration()
    {
        if (config == null)
        {
            Debug.LogError($"[{name}] ARTrackingConfig is not assigned! Using default values.");
            config = ScriptableObject.CreateInstance<ARTrackingConfig>();
        }
    }

    private void InitializeComponents()
    {
        _arTrackedImageManager = GetComponent<ARTrackedImageManager>();

        if (_arTrackedImageManager == null)
        {
            Debug.LogError($"[{name}] ARTrackedImageManager component not found!");
        }
    }

    private void SetupARCamera()
    {
        _arCamera = Camera.main;

        if (_arCamera == null)
        {
            ARCameraManager arCameraManager = FindObjectOfType<ARCameraManager>();
            if (arCameraManager != null)
            {
                _arCamera = arCameraManager.GetComponent<Camera>();
            }
        }

        if (_arCamera == null)
        {
            _arCamera = FindObjectOfType<Camera>();
        }

        if (_arCamera == null)
        {
            Debug.LogError($"[{name}] AR Camera not found! Manual rotation will not work.");
        }
        else if (config.enableDebugLogs)
        {
            Debug.Log($"[{name}] AR Camera found: {_arCamera.name}");
        }
    }

    private void InitializeUI()
    {
        if (playButton != null)
        {
            playButton.SetActive(false);
        }

        if (scanText != null)
        {
            scanText.SetActive(true);
        }
    }

    private void InstantiateARObjects()
    {
        if (config.objectMappings == null || config.objectMappings.Count == 0)
        {
            Debug.LogWarning($"[{name}] No object mappings configured!");
            return;
        }

        foreach (ARObjectMapping mapping in config.objectMappings)
        {
            if (string.IsNullOrEmpty(mapping.imageName))
            {
                Debug.LogWarning($"[{name}] Image name is empty. Skipping.");
                continue;
            }

            if (mapping.prefab == null)
            {
                Debug.LogWarning($"[{name}] Prefab for '{mapping.imageName}' is null. Skipping.");
                continue;
            }

            GameObject arObject = Instantiate(mapping.prefab, Vector3.zero, Quaternion.identity);
            arObject.name = mapping.imageName;
            arObject.SetActive(false);

            if (config.autoAddColliders)
            {
                EnsureColliderExists(arObject);
            }

            _arObjects.Add(mapping.imageName, arObject);
            _currentManualRotations.Add(mapping.imageName, Quaternion.identity);
            _lostTimestamps.Add(mapping.imageName, -1f);
            _targetPositions.Add(mapping.imageName, Vector3.zero);
            _targetRotations.Add(mapping.imageName, Quaternion.identity);

            if (config.enableDebugLogs)
            {
                Debug.Log($"[{name}] Initialized AR object: {mapping.imageName}");
            }
        }

        foreach (TextMapping textMapping in textMappings)
        {
            if (!string.IsNullOrEmpty(textMapping.imageName) && textMapping.textObject != null)
            {
                _textPrefabs.Add(textMapping.imageName, textMapping.textObject);
                textMapping.textObject.SetActive(false);
            }
        }
    }

    private void EnsureColliderExists(GameObject arObject)
    {
        Collider mainCollider = arObject.GetComponent<Collider>();

        if (mainCollider == null)
        {
            Collider[] childColliders = arObject.GetComponentsInChildren<Collider>();

            if (childColliders.Length == 0)
            {
                BoxCollider boxCollider = arObject.AddComponent<BoxCollider>();
                Renderer[] renderers = arObject.GetComponentsInChildren<Renderer>();

                if (renderers.Length > 0)
                {
                    Bounds bounds = new Bounds(arObject.transform.position, Vector3.zero);
                    foreach (Renderer renderer in renderers)
                    {
                        bounds.Encapsulate(renderer.bounds);
                    }

                    Vector3 center = arObject.transform.InverseTransformPoint(bounds.center);
                    Vector3 size = bounds.size * config.colliderSizeMultiplier;

                    boxCollider.center = center;
                    boxCollider.size = size;
                }
                else
                {
                    boxCollider.size = config.defaultColliderSize;
                }

                if (config.enableDebugLogs)
                {
                    Debug.Log($"[{name}] Added BoxCollider to {arObject.name}");
                }
            }
        }
    }

    private GameObject GetPlayButton() => playButton;
    private GameObject GetScanText() => scanText;

    #endregion

    #region AR Tracking

    private void OnTrackedImageChanged(ARTrackedImagesChangedEventArgs args)
    {
        foreach (ARTrackedImage trackedImage in args.added)
        {
            UpdateTrackedImage(trackedImage);
        }

        foreach (ARTrackedImage trackedImage in args.updated)
        {
            UpdateTrackedImage(trackedImage);
        }

        foreach (ARTrackedImage trackedImage in args.removed)
        {
            HandleImageRemoved(trackedImage);
        }
    }

    private void UpdateTrackedImage(ARTrackedImage trackedImage)
    {
        string imageName = trackedImage.referenceImage.name;

        if (!_arObjects.ContainsKey(imageName))
        {
            if (config.enableDebugLogs)
            {
                Debug.LogWarning($"[{name}] AR object not found for image: {imageName}");
            }
            return;
        }

        GameObject arObject = _arObjects[imageName];

        // Check tracking quality
        bool isGoodTracking = trackedImage.trackingState == TrackingState.Tracking;
        
        if (requireLimitedTracking)
        {
            isGoodTracking = isGoodTracking || trackedImage.trackingState == TrackingState.Limited;
        }

        if (isGoodTracking)
        {
            // Cancel deactivation if was scheduled
            _lostTimestamps[imageName] = -1f;
            
            HandleImageTracking(trackedImage, arObject, imageName);
        }
        else
        {
            HandleImageLost(arObject, imageName);
        }
    }

    private void HandleImageTracking(ARTrackedImage trackedImage, GameObject arObject, string imageName)
    {
        if (!arObject.activeInHierarchy)
        {
            arObject.SetActive(true);
            _activeImageCount++;
            InitializeObjectRotation(arObject, imageName);

            if (config.enableDebugLogs)
            {
                Debug.Log($"[{name}] Activated AR object: {imageName}");
            }
        }

        Vector3 markerPosition = trackedImage.transform.position;
        float upDot = Vector3.Dot(trackedImage.transform.up, Vector3.up);

        Vector3 finalPosition = CalculateObjectPosition(markerPosition, upDot);
        
        if (enableSmoothing)
        {
            _targetPositions[imageName] = finalPosition;
        }
        else
        {
            arObject.transform.position = finalPosition;
        }

        if (!_isRotating || _currentRotatingObject != arObject)
        {
            if (enableSmoothing)
            {
                CalculateTargetRotation(arObject, imageName);
            }
            else
            {
                ApplyRotationToObject(arObject, imageName);
            }
        }

        UpdateCurrentAnimator(arObject, imageName);
        ShowTextPrefab(imageName);
    }

    private Vector3 CalculateObjectPosition(Vector3 markerPosition, float upDot)
    {
        if (upDot > config.horizontalThreshold)
        {
            return new Vector3(
                markerPosition.x,
                markerPosition.y,
                markerPosition.z + config.nearPanelOffset
            );
        }
        else
        {
            return new Vector3(
                markerPosition.x,
                markerPosition.y + config.yOffset,
                markerPosition.z
            );
        }
    }

    private void HandleImageLost(GameObject arObject, string imageName)
    {
        // Schedule deactivation instead of immediate
        if (_lostTimestamps[imageName] < 0 && arObject.activeInHierarchy)
        {
            _lostTimestamps[imageName] = Time.time;
            
            if (config.enableDebugLogs)
            {
                Debug.Log($"[{name}] Scheduled deactivation for: {imageName}");
            }
        }
    }

    private void CheckDeactivationDelays()
    {
        List<string> toDeactivate = new List<string>();

        foreach (var kvp in _lostTimestamps)
        {
            if (kvp.Value > 0 && Time.time - kvp.Value >= deactivationDelay)
            {
                toDeactivate.Add(kvp.Key);
            }
        }

        foreach (string imageName in toDeactivate)
        {
            if (_arObjects.ContainsKey(imageName))
            {
                GameObject arObject = _arObjects[imageName];
                
                if (arObject.activeInHierarchy)
                {
                    arObject.SetActive(false);
                    _activeImageCount--;

                    if (_currentRotatingObject == arObject)
                    {
                        EndRotation();
                    }

                    // FIX: Hide text untuk object ini
                    if (_textPrefabs.ContainsKey(imageName) && _textPrefabs[imageName] != null)
                    {
                        _textPrefabs[imageName].SetActive(false);
                    }

                    if (config.enableDebugLogs)
                    {
                        Debug.Log($"[{name}] Deactivated AR object: {imageName}");
                    }
                }
            }

            _lostTimestamps[imageName] = -1f;
        }

        // FIX: Reset UI hanya pas semua object udah mati
        if (_activeImageCount <= 0)
        {
            if (scanText != null)
            {
                scanText.SetActive(true);
            }
            ResetAnimatorState();
        }
    }

    private void HandleImageRemoved(ARTrackedImage trackedImage)
    {
        string imageName = trackedImage.referenceImage.name;

        if (_arObjects.ContainsKey(imageName))
        {
            GameObject arObject = _arObjects[imageName];

            if (arObject.activeInHierarchy)
            {
                arObject.SetActive(false);
                _activeImageCount--;
                _lostTimestamps[imageName] = -1f;
                
                // FIX: Hide text untuk object ini
                if (_textPrefabs.ContainsKey(imageName) && _textPrefabs[imageName] != null)
                {
                    _textPrefabs[imageName].SetActive(false);
                }
            }
        }

        // FIX: Reset UI hanya pas semua object udah mati
        if (_activeImageCount <= 0)
        {
            if (scanText != null)
            {
                scanText.SetActive(true);
            }
            ResetAnimatorState();
        }
    }

    private void SmoothObjectTransforms()
    {
        foreach (var kvp in _arObjects)
        {
            string imageName = kvp.Key;
            GameObject arObject = kvp.Value;

            if (!arObject.activeInHierarchy) continue;

            // Smooth position
            if (_targetPositions.ContainsKey(imageName))
            {
                arObject.transform.position = Vector3.Lerp(
                    arObject.transform.position,
                    _targetPositions[imageName],
                    positionSmoothSpeed * Time.deltaTime
                );
            }

            // Smooth rotation (only if not manually rotating)
            if (_targetRotations.ContainsKey(imageName) && 
                (_currentRotatingObject != arObject || !_isRotating))
            {
                arObject.transform.rotation = Quaternion.Slerp(
                    arObject.transform.rotation,
                    _targetRotations[imageName],
                    positionSmoothSpeed * Time.deltaTime
                );
            }
        }
    }

    private void CalculateTargetRotation(GameObject arObject, string objectName)
    {
        if (_arCamera == null) return;

        Vector3 directionToCamera = _arCamera.transform.position - arObject.transform.position;
        directionToCamera.y = 0;

        Quaternion lookAtRotation = Quaternion.identity;
        if (directionToCamera != Vector3.zero)
        {
            lookAtRotation = Quaternion.LookRotation(directionToCamera.normalized);
        }

        _targetRotations[objectName] = lookAtRotation * _currentManualRotations[objectName];
    }

    #endregion

    #region Rotation Handling

    private void HandleRotationInput()
    {
        if (Input.touchCount == 1)
        {
            Touch touch = Input.GetTouch(0);

            switch (touch.phase)
            {
                case TouchPhase.Began:
                    HandleTouchBegan(touch.position);
                    break;
                case TouchPhase.Moved:
                    if (_isRotating && _currentRotatingObject != null)
                    {
                        RotateObject(touch.position);
                    }
                    break;
                case TouchPhase.Ended:
                case TouchPhase.Canceled:
                    EndRotation();
                    break;
            }
        }

#if UNITY_EDITOR
        HandleEditorInput();
#endif
    }

#if UNITY_EDITOR
    private void HandleEditorInput()
    {
        if (Input.GetMouseButtonDown(0))
        {
            HandleTouchBegan(Input.mousePosition);
        }
        else if (Input.GetMouseButton(0) && _isRotating && _currentRotatingObject != null)
        {
            RotateObject(Input.mousePosition);
        }
        else if (Input.GetMouseButtonUp(0))
        {
            EndRotation();
        }
    }
#endif

    private void HandleTouchBegan(Vector2 screenPosition)
    {
        if (_arCamera == null) return;

        Ray ray = _arCamera.ScreenPointToRay(screenPosition);
        RaycastHit[] hits = Physics.RaycastAll(ray, Mathf.Infinity);

        Array.Sort(hits, (hit1, hit2) => hit1.distance.CompareTo(hit2.distance));

        if (config.enableDebugLogs && !config.logOnlySignificantMovements)
        {
            Debug.Log($"[{name}] Touch at {screenPosition}, hits: {hits.Length}");
        }

        foreach (RaycastHit hit in hits)
        {
            GameObject tappedARObject = FindARObjectFromHit(hit.collider.gameObject);

            if (tappedARObject != null && tappedARObject.activeInHierarchy)
            {
                if (config.enableDoubleTapReset && CheckForDoubleTap(screenPosition, tappedARObject))
                {
                    ResetObjectRotation(tappedARObject.name);
                    return;
                }

                StartRotation(screenPosition, tappedARObject);
                return;
            }
        }
    }

    private GameObject FindARObjectFromHit(GameObject hitObject)
    {
        foreach (var kvp in _arObjects)
        {
            if (hitObject == kvp.Value) return kvp.Value;
        }

        Transform parent = hitObject.transform;
        while (parent != null)
        {
            foreach (var kvp in _arObjects)
            {
                if (parent.gameObject == kvp.Value) return kvp.Value;
            }
            parent = parent.parent;
        }

        return null;
    }

    private bool CheckForDoubleTap(Vector2 currentTapPosition, GameObject currentTappedObject)
    {
        float currentTime = Time.time;

        if (currentTime - _lastTapTime <= config.doubleTapTimeWindow)
        {
            if (_lastTappedObject == currentTappedObject)
            {
                float distance = Vector2.Distance(currentTapPosition, _lastTapPosition);

                if (distance <= config.tapPositionThreshold)
                {
                    _lastTapTime = 0f;
                    _lastTappedObject = null;

                    if (config.enableDebugLogs)
                    {
                        Debug.Log($"[{name}] Double tap detected on {currentTappedObject.name}");
                    }

                    return true;
                }
            }
        }

        _lastTapTime = currentTime;
        _lastTapPosition = currentTapPosition;
        _lastTappedObject = currentTappedObject;

        return false;
    }

    private void StartRotation(Vector2 screenPosition, GameObject arObject)
    {
        _isRotating = true;
        _currentRotatingObject = arObject;
        _lastTouchPosition = screenPosition;

        if (config.enableDebugLogs)
        {
            Debug.Log($"[{name}] Started rotating: {arObject.name}");
        }
    }

    private void RotateObject(Vector2 currentTouchPosition)
    {
        if (_currentRotatingObject == null) return;

        Vector2 deltaPosition = currentTouchPosition - _lastTouchPosition;
        _lastTouchPosition = currentTouchPosition;

        float deltaTime = config.useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        float sensitivity = config.rotationSpeed * config.rotationSensitivityMultiplier * deltaTime;

        float rotationY = deltaPosition.x * sensitivity;
        float rotationX = config.lockXRotation ? 0f : -deltaPosition.y * sensitivity;

        string objectName = _currentRotatingObject.name;
        Quaternion rotationDelta = Quaternion.Euler(rotationX, -rotationY, 0);
        _currentManualRotations[objectName] = _currentManualRotations[objectName] * rotationDelta;

        // ALWAYS apply directly pas rotate (ignore smoothing)
        ApplyRotationToObject(_currentRotatingObject, objectName);
        
        // Update target buat smooth transition setelah rotate selesai
        if (enableSmoothing)
        {
            _targetRotations[objectName] = _currentRotatingObject.transform.rotation;
        }

        if (config.enableDebugLogs &&
            (!config.logOnlySignificantMovements || deltaPosition.magnitude > config.significantMovementThreshold))
        {
            Debug.Log($"[{name}] Rotating {objectName}: delta=({rotationX:F2}, {rotationY:F2})");
        }
    }

    private void EndRotation()
    {
        if (_isRotating && _currentRotatingObject != null && config.enableDebugLogs)
        {
            Debug.Log($"[{name}] Ended rotating: {_currentRotatingObject.name}");
        }

        _isRotating = false;
        _currentRotatingObject = null;
    }

    private void InitializeObjectRotation(GameObject arObject, string objectName)
    {
        if (_arCamera == null) return;

        Vector3 directionToCamera = _arCamera.transform.position - arObject.transform.position;
        directionToCamera.y = 0;

        if (directionToCamera != Vector3.zero)
        {
            Quaternion lookAtRotation = Quaternion.LookRotation(directionToCamera.normalized);
            _currentManualRotations[objectName] = Quaternion.identity;
            
            if (enableSmoothing)
            {
                _targetRotations[objectName] = lookAtRotation;
                arObject.transform.rotation = lookAtRotation; // Instant first time
            }
            else
            {
                arObject.transform.rotation = lookAtRotation;
            }

            if (config.enableDebugLogs)
            {
                Debug.Log($"[{name}] Initialized {objectName} to face camera");
            }
        }
    }

    private void ApplyRotationToObject(GameObject arObject, string objectName)
    {
        if (_arCamera == null) return;

        Vector3 directionToCamera = _arCamera.transform.position - arObject.transform.position;
        directionToCamera.y = 0;

        Quaternion lookAtRotation = Quaternion.identity;
        if (directionToCamera != Vector3.zero)
        {
            lookAtRotation = Quaternion.LookRotation(directionToCamera.normalized);
        }

        Quaternion finalRotation = lookAtRotation * _currentManualRotations[objectName];
        arObject.transform.rotation = finalRotation;
    }

    #endregion

    #region Animation Control

    private void UpdateCurrentAnimator(GameObject arObject, string imageName)
    {
        _currentAnimator = arObject.GetComponent<Animator>();
        _currentAnimationState = imageName;

        if (playButton != null)
        {
            playButton.SetActive(true);
        }
    }

    private void ResetAnimatorState()
    {
        if (playButton != null)
        {
            playButton.SetActive(false);
        }

        _currentAnimator = null;
        _currentAnimationState = null;
    }

    public void PlayAnimation()
    {
        if (_currentAnimator != null && !string.IsNullOrEmpty(_currentAnimationState))
        {
            _currentAnimator.Play(_currentAnimationState, 0, 0f);

            if (config.enableDebugLogs)
            {
                Debug.Log($"[{name}] Playing animation: {_currentAnimationState}");
            }
        }
        else if (config.enableDebugLogs)
        {
            Debug.LogWarning($"[{name}] Cannot play animation - animator or state is null/empty");
        }
    }

    #endregion

    #region UI Management

    private void ShowTextPrefab(string imageName)
    {
        HideAllTextPrefabs();

        if (_textPrefabs.ContainsKey(imageName))
        {
            if (scanText != null)
            {
                scanText.SetActive(false);
            }

            _textPrefabs[imageName].SetActive(true);
        }
    }

    private void HideAllTextPrefabs()
    {
        if (scanText != null)
        {
            scanText.SetActive(true);
        }

        foreach (var kvp in _textPrefabs)
        {
            kvp.Value.SetActive(false);
        }
    }

    #endregion

    #region Public API

    public void ResetObjectRotation(string objectName)
    {
        if (_currentManualRotations.ContainsKey(objectName))
        {
            _currentManualRotations[objectName] = Quaternion.identity;

            if (_arObjects.ContainsKey(objectName) && _arObjects[objectName].activeInHierarchy)
            {
                GameObject arObject = _arObjects[objectName];
                InitializeObjectRotation(arObject, objectName);
                
                if (!enableSmoothing)
                {
                    ApplyRotationToObject(arObject, objectName);
                }
            }

            if (config.enableDebugLogs)
            {
                Debug.Log($"[{name}] Reset rotation for: {objectName}");
            }
        }
    }

    public void ResetAllRotations()
    {
        foreach (string objectName in _currentManualRotations.Keys.ToList())
        {
            ResetObjectRotation(objectName);
        }
    }

    public void SetRotationEnabled(bool enabled)
    {
        if (config != null)
        {
            config.enableManualRotation = enabled;

            if (!enabled && _isRotating)
            {
                EndRotation();
            }
        }
    }

    public void SetDoubleTapResetEnabled(bool enabled)
    {
        if (config != null)
        {
            config.enableDoubleTapReset = enabled;
        }
    }

    #endregion

    #region Debug Methods

    [ContextMenu("Debug AR Objects")]
    public void DebugARObjects()
    {
        Debug.Log($"=== [{name}] AR OBJECTS DEBUG ===");

        foreach (var kvp in _arObjects)
        {
            GameObject obj = kvp.Value;
            Debug.Log($"Object: {kvp.Key}\n" +
                     $"- Active: {obj.activeInHierarchy}\n" +
                     $"- Collider: {obj.GetComponent<Collider>() != null}\n" +
                     $"- Child Colliders: {obj.GetComponentsInChildren<Collider>().Length}\n" +
                     $"- Position: {obj.transform.position}\n" +
                     $"- Rotation: {obj.transform.rotation.eulerAngles}");
        }

        Debug.Log($"Camera: {(_arCamera != null ? _arCamera.name : "NULL")}");
        Debug.Log($"Rotating: {(_currentRotatingObject != null ? _currentRotatingObject.name : "NONE")}");
        Debug.Log($"Active Images: {_activeImageCount}");
    }

    [ContextMenu("Test Rotation")]
    public void TestRotation()
    {
        foreach (var kvp in _arObjects)
        {
            if (kvp.Value.activeInHierarchy)
            {
                _currentManualRotations[kvp.Key] = Quaternion.Euler(0, 45, 0);
                ApplyRotationToObject(kvp.Value, kvp.Key);
                Debug.Log($"[{name}] Test rotation applied to {kvp.Key}");
                return;
            }
        }

        Debug.LogWarning($"[{name}] No active objects to test rotation");
    }

    #endregion
}