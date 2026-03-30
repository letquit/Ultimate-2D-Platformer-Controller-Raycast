using UnityEditor;
using UnityEngine;
using UnityEngine.XR;

[CustomEditor(typeof(PlayerMovementStats))]
public class PlayerMovementStatsEditor : Editor
{
    private float _radius = 100f;
    private float _centerOffset = 130f;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        PlayerMovementStats stats = (PlayerMovementStats)target;

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.Space(10);

        Rect rect = GUILayoutUtility.GetRect(200, 260);
        Vector2 center = new Vector2(rect.x + rect.width / 2, rect.y + _centerOffset);

        if (Event.current.type == EventType.Repaint)
        {
            DrawDashVisualization(center, stats);
        }
        
        EditorGUILayout.EndVertical();
        serializedObject.ApplyModifiedProperties();
    }

    private void DrawDashVisualization(Vector2 center, PlayerMovementStats stats)
    {
        float upTol = stats.DashUpwardAngleTolerance;
        float downTol = stats.DashDownwardAngleTolerance;
        float horizTol = stats.DashHorizontalAngleTolerance;

        Handles.color = new Color(0.1f, 0.1f, 0.1f, 0.5f);
        Handles.DrawSolidDisc(center, Vector3.forward, _radius);

        Color cardinalColor = new Color(0.35f, 0.6f, 0.85f, 0.5f);
        Color diagColor = new Color(0f, 0.5f, 1f, 0.4f);

        DrawSector(center, -horizTol, horizTol, "RIGHT", cardinalColor);
        DrawSector(center, 180f - horizTol, 180f + horizTol, "LEFT", cardinalColor);
        DrawSector(center, 90f - upTol, 90f + upTol, "UP", cardinalColor);
        DrawSector(center, 270f - downTol, 270f + downTol, "DOWN", cardinalColor);
        DrawSector(center, horizTol, 90f - upTol, "UP-RIGHT", diagColor);
        DrawSector(center, 90f + upTol, 180f - horizTol, "UP-LEFT", diagColor);
        DrawSector(center, 180f + horizTol, 270f - downTol, "DOWN-LEFT", diagColor);
        DrawSector(center, 270f + downTol, 360f - horizTol, "DOWN-RIGHT", diagColor);

        Handles.color = new Color(1f, 1f, 1f, 0.1f);
        Handles.DrawLine(center + Vector2.up * -_radius, center + Vector2.up * _radius);
        Handles.DrawLine(center + Vector2.right * -_radius, center + Vector2.right * _radius);
    }

    private void DrawSector(Vector2 center, float startAngle, float endAngle, string label, Color color)
    {
        float sweep = endAngle - startAngle;
        if (sweep <= 0.001f) return;

        Handles.color = color;
        Handles.DrawSolidArc(center, Vector3.back, AngleToVector(startAngle), sweep, _radius);

        float midAngle = startAngle + (sweep / 2f);
        DrawLabel(center, midAngle, label);
    }

    private void DrawLabel(Vector2 center, float angle, string text)
    {
        Vector2 dir = AngleToVector(angle);
        Vector2 pos = center + (dir * (_radius + 22f));

        GUIStyle style = new GUIStyle(EditorStyles.boldLabel);
        style.alignment = TextAnchor.MiddleCenter;
        style.normal.textColor = Color.white;
        style.fontSize = 10;

        Vector2 size = style.CalcSize(new GUIContent(text));
        Rect labelRect = new Rect(pos.x - (size.x / 2), pos.y - (size.y / 2), size.x + 6, size.y + 2);

        EditorGUI.DrawRect(labelRect, new Color(0, 0, 0, 0.75f));
        GUI.Label(labelRect, text, style);
    }

    private Vector3 AngleToVector(float angleDeg)
    {
        float rad = angleDeg * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(rad), -Mathf.Sin(rad), 0);
    }
}
