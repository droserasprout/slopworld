using UnityEngine;
using Verse;

namespace SlopWorld
{
    public sealed class SlopCommandAction : Command_Action
    {
        readonly SlopWidgets.Btn _kind;

        public SlopCommandAction(SlopWidgets.Btn kind)
        {
            _kind = kind;
        }

        public override Texture2D BGTexture => BaseContent.ClearTex;

        public override Texture2D BGTextureShrunk => BaseContent.ClearTex;

        protected override GizmoResult GizmoOnGUIInt(Rect butRect, GizmoRenderParms parms)
        {
            return base.GizmoOnGUIInt(butRect, parms);
        }

        public override void DrawIcon(Rect rect, Material buttonMat, GizmoRenderParms parms)
        {
            bool over = !Disabled && Mouse.IsOver(rect);
            SlopWidgets.ActionButtonBackground(rect, _kind, !Disabled, over,
                over && Input.GetMouseButton(0));
            base.DrawIcon(rect, buttonMat, parms);
        }
    }
}
