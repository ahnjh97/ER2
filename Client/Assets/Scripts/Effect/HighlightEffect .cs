using System.Collections.Generic;
using UnityEngine;

namespace Assets.Scripts.Highlight
{
    // 이거 쓰려면 스키닝 메시 + 아웃라인 머터리얼 + 충돌 캡슐 필요
    public class HighlightEffect : MonoBehaviour
    {
        private Renderer myRenderer;
        private Material outlineMaterial;
        private Material[] originalMaterials;

        // 커서
        private Texture2D _cursorDefault;
        private Texture2D _cursorEnemy;

        void Start()
        {
            myRenderer = GetComponentInChildren<Renderer>();
            _cursorDefault = Managers.Resource.Load<Texture2D>("Cursor/Cursor_01");
            _cursorEnemy = Managers.Resource.Load<Texture2D>("Cursor/Cursor_05");

            outlineMaterial = Resources.Load<Material>("materials/Outline/Outline");
            if (outlineMaterial == null || myRenderer == null)
            {
                Debug.LogError("outline Material || myRenderer 이 null 이다.");
                return;
            }

            originalMaterials = myRenderer.sharedMaterials;

            outlineMaterial = new Material(Shader.Find("Custom/Outline_Shader"));
        }

        void OnMouseEnter()
        {
            if (myRenderer == null) return;

            List<Material> newMaterials = new List<Material>(originalMaterials);

            newMaterials.Add(outlineMaterial);
            Cursor.SetCursor(_cursorEnemy, Vector2.zero, CursorMode.Auto);

            myRenderer.materials = newMaterials.ToArray();
        }

        void OnMouseExit()
        {
            if (myRenderer == null) return;

            myRenderer.materials = originalMaterials;
            Cursor.SetCursor(_cursorDefault, Vector2.zero, CursorMode.Auto);
        }

        void OnDestroy()
        {
            if (outlineMaterial != null)
                Destroy(outlineMaterial);
        }
    }
}
