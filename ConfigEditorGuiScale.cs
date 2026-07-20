using BepInEx;
using UnityEngine;
using static TradersExtended.TradersExtended;

namespace TradersExtended
{
    internal sealed class ConfigEditorGuiScale
    {
        private bool temporaryUnity6000Window = true;

        internal float ScaleFactor
        {
            get
            {
                float scale = Mathf.Clamp(configEditorUiScale?.Value ?? 1f, 0.25f, 4f);
                if (configEditorUseValheimGuiScale?.Value != false)
                    scale *= GetValheimGuiScale();
                return Mathf.Max(0.01f, scale);
            }
        }

        internal int PhysicalWidth => temporaryUnity6000Window ? GetDisplayWidth() : Mathf.Max(1, Screen.width);
        internal int PhysicalHeight => temporaryUnity6000Window ? GetDisplayHeight() : Mathf.Max(1, Screen.height);
        internal float LogicalWidth => PhysicalWidth / ScaleFactor;
        internal float LogicalHeight => PhysicalHeight / ScaleFactor;
        internal Matrix4x4 Matrix => Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(ScaleFactor, ScaleFactor, 1f));

        internal void MarkGameWindowReady() => temporaryUnity6000Window = false;

        internal Vector2 GetLogicalMousePosition()
        {
            Vector3 mouse = Matrix.inverse.MultiplyPoint(UnityInput.Current.mousePosition);
            return new Vector2(mouse.x, LogicalHeight - mouse.y);
        }

        private float GetValheimGuiScale()
        {
            try
            {
                float widthFactor = (float)PhysicalWidth / GuiScaler.m_minWidth;
                float heightFactor = (float)PhysicalHeight / GuiScaler.m_minHeight;
                return Mathf.Max(0.01f, Mathf.Min(widthFactor, heightFactor) * GuiScaler.m_largeGuiScale);
            }
            catch
            {
                return 1f;
            }
        }

        private static int GetDisplayWidth()
        {
            try
            {
                return Display.main != null ? Mathf.Max(1, Display.main.systemWidth) : Mathf.Max(1, Screen.width);
            }
            catch
            {
                return Mathf.Max(1, Screen.width);
            }
        }

        private static int GetDisplayHeight()
        {
            try
            {
                return Display.main != null ? Mathf.Max(1, Display.main.systemHeight) : Mathf.Max(1, Screen.height);
            }
            catch
            {
                return Mathf.Max(1, Screen.height);
            }
        }
    }
}
