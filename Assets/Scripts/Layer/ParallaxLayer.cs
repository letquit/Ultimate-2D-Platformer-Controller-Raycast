using System;
using UnityEngine;

public class ParallaxLayer : MonoBehaviour
{
    [Header("Settings")]
    public float ParallaxSpeed = 100;
    public bool LockY = false;

    private Transform _camTrans;
    private Vector3 _lastCamPos;

    private void Start()
    {
        _camTrans = Camera.main.transform;
        _lastCamPos = _camTrans.position;
    }

    private void LateUpdate()
    {
        Vector3 camDelta = _camTrans.position - _lastCamPos;
        float parallaxFactor = (100f - ParallaxSpeed) / 100f;
        
        float moveX = camDelta.x * parallaxFactor;
        float moveY = LockY ? 0 : (camDelta.y * parallaxFactor);
        
        transform.localPosition += new Vector3(moveX, moveY, 0);
        
        _lastCamPos = _camTrans.position;
    }
}
