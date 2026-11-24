using UnityEngine;
namespace LittleWitchCoop
{
    internal sealed class CapacityButton
    {
        private readonly HoldRepeat repeat = new HoldRepeat();
        private int control, lastFrame = -10;
        private bool holding;
        public int Draw(Rect area, string label, GUIStyle style, int value, int direction)
        {
            if (Time.frameCount > lastFrame + 1) Cancel();
            lastFrame = Time.frameCount;
            control = GUIUtility.GetControlID(FocusType.Passive);
            Event e = Event.current;
            bool hover = area.Contains(e.mousePosition);
            int delta = 0;
            if (!GUI.enabled || (holding && GUIUtility.hotControl != control)) Cancel();
            if (GUI.enabled && e.type == EventType.MouseDown && e.button == 0 && hover)
            {
                GUIUtility.hotControl = control; holding = true;
                delta = repeat.Begin(Time.realtimeSinceStartup); e.Use();
            }
            if (holding && e.type == EventType.MouseUp && e.button == 0) { Cancel(); e.Use(); }
            if (e.type == EventType.Repaint)
            {
                if (holding && !Input.GetMouseButton(0)) Cancel();
                if (holding && hover) delta += repeat.Poll(Time.realtimeSinceStartup);
                style.Draw(area, new GUIContent(label), hover, holding && hover, false, false);
            }
            return Mathf.Clamp(value + direction * delta, 2, 99);
        }
        private void Cancel()
        {
            if (holding && GUIUtility.hotControl == control) GUIUtility.hotControl = 0;
            holding = false; repeat.End();
        }
    }
}
