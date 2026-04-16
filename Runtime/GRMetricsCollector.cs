using UnityEngine;
using UnityEngine.Profiling;

/// <summary>
/// Automatically tracks FPS and memory metrics for GameRebellion SDK.
/// Attach this to a GameObject in your scene or have it auto-created by SDK.
/// </summary>
public class GRMetricsCollector : MonoBehaviour
{
    [Header("FPS Tracking")]
    [Tooltip("Track FPS every frame (recommended)")]
    public bool trackFPS = true;
    
    [Header("Memory Tracking")]
    [Tooltip("Track memory usage")]
    public bool trackMemory = true;
    
    [Tooltip("Memory sampling interval in seconds")]
    [Range(0.1f, 5.0f)]
    public float memorySampleInterval = 0.5f;
    
    [Header("Debug")]
    [Tooltip("Show metrics in console")]
    public bool logMetrics = false;
    
    [Tooltip("Log interval in seconds")]
    [Range(1f, 10f)]
    public float logInterval = 5f;
    
    private float _memoryTimer = 0f;
    private float _logTimer = 0f;
    private int _frameCount = 0;
    private float _fpsSum = 0f;
    
    private void Update()
    {
        if (!GameRebellion.IsInitialized)
            return;
        
        // Track FPS every frame
        if (trackFPS && Time.deltaTime > 0)
        {
            float fps = 1.0f / Time.deltaTime;
            GameRebellion.RecordFrame(fps);
            
            if (logMetrics)
            {
                _fpsSum += fps;
                _frameCount++;
            }
        }
        
        // Track memory at intervals
        if (trackMemory)
        {
            _memoryTimer += Time.deltaTime;
            if (_memoryTimer >= memorySampleInterval)
            {
                _memoryTimer = 0f;
                
                // Use Unity Profiler for accurate memory info
                long memoryBytes = Profiler.GetTotalAllocatedMemoryLong();
                float memoryMB = memoryBytes / (1024f * 1024f);
                
                GameRebellion.RecordMemory(memoryMB);
            }
        }
        
        // Debug logging
        if (logMetrics)
        {
            _logTimer += Time.deltaTime;
            if (_logTimer >= logInterval && _frameCount > 0)
            {
                float avgFps = _fpsSum / _frameCount;
                long memoryBytes = Profiler.GetTotalAllocatedMemoryLong();
                float memoryMB = memoryBytes / (1024f * 1024f);
                
                Debug.Log($"[GR Metrics] Avg FPS: {avgFps:F1}, Memory: {memoryMB:F1} MB");
                
                _logTimer = 0f;
                _frameCount = 0;
                _fpsSum = 0f;
            }
        }
    }
    
    private void OnApplicationQuit()
    {
        if (GameRebellion.IsInitialized)
        {
            Debug.Log("[GR Metrics] Application quit detected");
            GameRebellion.Shutdown("user_quit");
        }
    }
    
    private void OnApplicationPause(bool pauseStatus)
    {
        if (!GameRebellion.IsInitialized)
            return;

        if (pauseStatus)
        {
            Debug.Log("[GR Metrics] Application paused (backgrounded)");
            GameRebellion.SetPaused(true);
        }
        else
        {
            Debug.Log("[GR Metrics] Application resumed (foregrounded)");
            GameRebellion.SetPaused(false);
        }
    }
}
