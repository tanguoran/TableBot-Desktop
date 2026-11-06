using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace WhaleDesk
{
    public sealed class DesktopClickToMove : MonoBehaviour
    {
        public DesktopWalker walker;
        public Camera viewCamera;
        public bool showPath = true;
        public DesktopFileSync files;
        LineRenderer path, marker;
        Material lineMaterial, markerMaterial;
        float markerUntil;

        void Start()
        {
            lineMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            lineMaterial.SetColor("_BaseColor", new Color(.25f, .83f, 1));
            path = MakeLine("Walking Route", .005f);
            marker = MakeLine("Destination Ring", .007f);
            markerMaterial = new Material(lineMaterial);
            marker.sharedMaterial = markerMaterial;
            marker.loop = true;
        }

        LineRenderer MakeLine(string label, float width)
        {
            var go = new GameObject(label); go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = lineMaterial; line.widthMultiplier = width;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false; line.useWorldSpace = true; line.positionCount = 0;
            return line;
        }

        void Update()
        {
            if (!walker || !viewCamera) return;
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame &&
                !(EventSystem.current && EventSystem.current.IsPointerOverGameObject()))
                TryMoveAtScreenPoint(Mouse.current.position.ReadValue());
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame && !DesktopChatUI.IsTyping)
            {
                var actions = walker.GetComponent<DesktopActionRunner>();
                if (actions) actions.NotifyManualInput(); else walker.CancelMove();
            }
            if (path)
            {
                var corners = showPath && walker.IsMoving ? walker.GetPathCorners() : System.Array.Empty<Vector3>();
                path.positionCount = corners.Length;
                for (int i = 0; i < corners.Length; i++) path.SetPosition(i, corners[i] + Vector3.up * .012f);
            }
            if (marker && !walker.IsMoving && Time.time > markerUntil) marker.positionCount = 0;
        }

        public bool TryMoveAtScreenPoint(Vector2 point)
        {
            if (DesktopChatUI.BlocksPointer(point)) return false;
            if (!walker || !viewCamera || !viewCamera.pixelRect.Contains(point)) return false;
            if (files && files.isActiveAndEnabled && files.TryHandlePointer(point)) return true;
            var plane = new Plane(Vector3.up, new Vector3(0, walker.navigation.SurfaceHeight, 0));
            var ray = viewCamera.ScreenPointToRay(point);
            if (!plane.Raycast(ray, out float distance)) return false;
            var target = ray.GetPoint(distance);
            var resolvedTarget = target;
            var actions = walker.GetComponent<DesktopActionRunner>();
            bool accepted;
            if (actions)
            {
                accepted = walker.TryResolveDestination(target, out resolvedTarget, out _);
                if (accepted)
                {
                    actions.NotifyManualInput(true);
                    accepted = actions.TryStart(new ActionCommand { name = "move_to", x = target.x, z = target.z }, _ => {});
                }
            }
            else { accepted = walker.TryMoveTo(target); resolvedTarget = walker.Destination; }
            ShowMarker(accepted ? resolvedTarget : target, accepted);
            return accepted;
        }

        void ShowMarker(Vector3 target, bool accepted)
        {
            if (!marker) return;
            markerMaterial.SetColor("_BaseColor", accepted ? Color.cyan : new Color(1, .25f, .2f));
            marker.positionCount = 40;
            for (int i = 0; i < 40; i++)
            {
                float a = i * Mathf.PI * 2 / 40;
                marker.SetPosition(i, target + new Vector3(Mathf.Cos(a) * .045f, .014f, Mathf.Sin(a) * .045f));
            }
            markerUntil = Time.time + 1.3f;
        }

        void OnDestroy()
        {
            if (lineMaterial) Destroy(lineMaterial);
            if (markerMaterial) Destroy(markerMaterial);
        }
    }
}
