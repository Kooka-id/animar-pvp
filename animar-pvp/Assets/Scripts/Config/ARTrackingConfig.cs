using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class ARObjectMapping
{
    [Tooltip("Name of the tracked image (must match AR Reference Image Library)")]
    public string imageName;
    [Tooltip("3D model prefab to spawn when image is detected")]
    public GameObject prefab;
    // HAPUS textPrefab dari sini
}

[CreateAssetMenu(fileName = "ARTrackingConfig", menuName = "Portal AR/Tracking Configuration")]
public class ARTrackingConfig : ScriptableObject
{
    [Header("AR Object Mappings")]
    [Tooltip("List of image-to-prefab mappings (3D models only)")]
    public List<ARObjectMapping> objectMappings = new List<ARObjectMapping>();

    // HAPUS playButton & scanText dari sini
    
    [Header("Position Settings")]
    public float yOffset = -0.15f;
    public float nearPanelOffset = -0.15f;
    [Range(0f, 1f)]
    public float horizontalThreshold = 0.85f;

    [Header("Rotation Settings")]
    public bool enableManualRotation = true;
    public float rotationSpeed = 10f;
    public bool lockXRotation = false;
    public bool enableDoubleTapReset = true;
    public float doubleTapTimeWindow = 0.5f;
    public float tapPositionThreshold = 50f;

    [Header("Collider Settings")]
    public bool autoAddColliders = true;
    public Vector3 defaultColliderSize = Vector3.one;
    [Range(0.5f, 3f)]
    public float colliderSizeMultiplier = 1f;

    [Header("Debug Settings")]
    public bool enableDebugLogs = true;
    public bool logOnlySignificantMovements = true;
    public float significantMovementThreshold = 2f;

    [Header("Performance Settings")]
    public bool useUnscaledTime = true;
    [Range(0.1f, 2f)]
    public float rotationSensitivityMultiplier = 0.5f;

    private void OnValidate()
    {
        HashSet<string> imageNames = new HashSet<string>();
        foreach (var mapping in objectMappings)
        {
            if (!string.IsNullOrEmpty(mapping.imageName))
            {
                if (!imageNames.Add(mapping.imageName))
                {
                    Debug.LogWarning($"Duplicate image name found: {mapping.imageName}");
                }
            }
        }
    }
}