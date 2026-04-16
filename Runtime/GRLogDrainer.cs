using UnityEngine;

/// <summary>
/// Periodically drains and displays SDK logs for debugging.
/// Attach this component to see background task logs in real-time.
/// </summary>
public class GRLogDrainer : MonoBehaviour
{
    [Header("Log Draining")]
    [Tooltip("How often to drain and display SDK logs")]
    [Range(0.5f, 10f)]
    public float drainInterval = 1.0f;
    
    [Tooltip("Enable to see SDK logs in Unity console")]
    public bool enableLogDraining = true;
    
    private float _timer = 0f;
    
    void Update()
    {
        if (!enableLogDraining || !GameRebellion.IsInitialized)
            return;
        
        _timer += Time.deltaTime;
        if (_timer >= drainInterval)
        {
            _timer = 0f;
            GameRebellion.DrainAndLogSDKLogs();
        }
    }
}
