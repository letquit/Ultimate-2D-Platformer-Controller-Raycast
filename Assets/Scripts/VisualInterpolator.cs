using System;
using UnityEngine;

public class VisualInterpolator : MonoBehaviour
{
    private Rigidbody2D _rb;

    private Vector3 _prevPos;
    private Vector3 _currPos;
    
    public Vector3 PivotOffset { get; set; }

    private void Start()
    {
        _rb = GetComponentInParent<Rigidbody2D>();

        transform.parent = null;
        _prevPos = _currPos = _rb.position;
    }

    private void LateUpdate()
    {
        float t = (Time.time - Time.fixedTime) / Time.fixedDeltaTime;
        t = Mathf.Clamp01(t);

        Vector3 newPos = Vector3.Lerp(_prevPos, _currPos, t);
        
        transform.position = newPos + PivotOffset;
    }

    public void UpdatePhysicsState()
    {
        _prevPos = _currPos;
        _currPos = _rb.position;
    }

    public void ForceTeleport(Vector3 pos)
    {
        _prevPos = _currPos = pos;
        transform.position = pos;
        _rb.position = pos;
    }
}
