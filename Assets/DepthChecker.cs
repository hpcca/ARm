using UnityEngine;
using UnityEngine.XR.ARFoundation;

public class DepthChecker : MonoBehaviour
{
    public ARCameraManager arCameraManager;
    [Range(0.1f, 5f)] public float fakeDepth = 1.0f; // 模拟 0.1-5米深度

    void Update()
    {
        // 直接用模拟深度，跳过真实获取逻辑
        Debug.Log($"✅ 模拟中心深度：{fakeDepth:F3} 米");
        
        // 这里可以继续写后续逻辑：YOLO框采样、模型缩放等
    }
}