using UnityEngine;
using UnityEngine.Profiling;

/// <summary>
/// Feeds per-frame FPS to the GameRebellion SDK -- the one metric only the engine
/// can measure. Memory is sampled by the SDK itself, on every platform, so that the
/// number means the same thing here as it does in the Unreal SDK.
/// Attach this to a GameObject in your scene or have it auto-created by SDK.
/// </summary>
public class GRMetricsCollector : MonoBehaviour
{
    [Header("FPS Tracking")]
    [Tooltip("Track FPS every frame (recommended)")]
    public bool trackFPS = true;
    
    [Header("Debug")]
    [Tooltip("Show metrics in console")]
    public bool logMetrics = false;
    
    [Tooltip("Log interval in seconds")]
    [Range(1f, 10f)]
    public float logInterval = 5f;
    
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
        
        // Memory is sampled by the SDK itself now. It reads the same quantity the
        // platform tools report (PSS on Android, phys_footprint on iOS, working set
        // on desktop); pushing Unity's allocator total from here made memory_peak
        // mean something different in Unity than in Unreal.
        
        // Debug logging
        if (logMetrics)
        {
            _logTimer += Time.deltaTime;
            if (_logTimer >= logInterval && _frameCount > 0)
            {
                float avgFps = _fpsSum / _frameCount;
                // Local diagnostic only -- the reported figure comes from the SDK.
                float allocatedMB = Profiler.GetTotalAllocatedMemoryLong() / (1024f * 1024f);

                Debug.Log($"[GR Metrics] Avg FPS: {avgFps:F1}, Unity allocated: {allocatedMB:F1} MB");
                
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
