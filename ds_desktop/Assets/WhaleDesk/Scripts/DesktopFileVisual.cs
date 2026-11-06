using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace WhaleDesk
{
    public sealed class DesktopFileVisual : MonoBehaviour
    {
        Material accentMaterial, imageMaterial;
        Texture2D thumbnail;
        public bool HasThumbnail => thumbnail;

        public static DesktopFileBinding Create(DesktopFileSync owner, DesktopFileEntry entry, Vector3 position)
        {
            var definition = owner.catalog.Find(entry.kind);
            if (definition == null || !definition.prefab) throw new InvalidOperationException("No file asset for " + entry.kind);
            var root = new GameObject("Desktop File - " + entry.name);
            root.SetActive(false); root.transform.SetParent(owner.transform, false); root.transform.position = position;
            var visual = root.AddComponent<DesktopFileVisual>();
            var content = new GameObject("Physical Asset").transform; content.SetParent(root.transform, false);
            var model = Instantiate(definition.prefab, content); model.transform.localRotation = Quaternion.Euler(0, definition.yaw, 0);
            foreach (var collider in model.GetComponentsInChildren<Collider>(true)) { collider.enabled = false; Destroy(collider); }
            foreach (var body in model.GetComponentsInChildren<Rigidbody>(true)) { body.detectCollisions = false; body.isKinematic = true; Destroy(body); }
            foreach (var renderer in model.GetComponentsInChildren<Renderer>())
            {
                renderer.lightmapIndex = -1;
                if (renderer is MeshRenderer mesh) mesh.receiveGI = ReceiveGI.LightProbes;
            }
            var bounds = BoundsOf(content);
            float scale = Mathf.Min(definition.maximumWidth / Mathf.Max(bounds.size.x, .001f),
                definition.maximumDepth / Mathf.Max(bounds.size.z, .001f), definition.maximumHeight / Mathf.Max(bounds.size.y, .001f));
            model.transform.localScale *= scale;
            bounds = BoundsOf(content);
            model.transform.position += new Vector3(position.x - bounds.center.x, position.y + .004f - bounds.min.y, position.z - bounds.center.z);
            bounds = BoundsOf(content);
            var accent = entry.extension == ".pdf" ? new Color(.72f, .12f, .13f) : definition.accent;
            visual.accentMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            visual.accentMaterial.SetColor("_BaseColor", accent); visual.accentMaterial.SetFloat("_Smoothness", .32f);
            if (entry.kind != "image" && entry.kind != "audio")
            {
                var ribbon = GameObject.CreatePrimitive(PrimitiveType.Cube); ribbon.name = "Format Ribbon";
                ribbon.transform.SetParent(content, true); ribbon.transform.position = new Vector3(bounds.center.x, bounds.max.y + .0015f, bounds.center.z);
                ribbon.transform.localScale = new Vector3(bounds.size.x * .74f, .002f, bounds.size.z * .17f);
                var collider = ribbon.GetComponent<Collider>(); collider.enabled = false; Destroy(collider);
                ribbon.GetComponent<Renderer>().sharedMaterial = visual.accentMaterial;
            }
            if (entry.kind == "image") visual.CreateImagePlane(content, bounds);
            bounds = BoundsOf(content);
            var selection = root.AddComponent<BoxCollider>();
            selection.center = root.transform.InverseTransformPoint(bounds.center); selection.size = bounds.size + Vector3.one * .006f;
            var perceived = root.AddComponent<DesktopWorldObject>(); perceived.physicalRoot = content;
            if (definition.blocksNavigation)
            {
                var obstacle = root.AddComponent<DesktopObstacle>(); obstacle.navigation = owner.navigation; obstacle.physicalRoot = content;
            }
            var binding = root.AddComponent<DesktopFileBinding>(); binding.owner = owner; binding.physicalRoot = content; binding.Bind(entry);
            root.SetActive(true); return binding;
        }

        void CreateImagePlane(Transform parent, Bounds bounds)
        {
            var plane = GameObject.CreatePrimitive(PrimitiveType.Quad); plane.name = "File Image Preview";
            var collider = plane.GetComponent<Collider>(); collider.enabled = false; Destroy(collider);
            plane.transform.SetParent(parent, true);
            plane.transform.position = new Vector3(bounds.center.x, bounds.center.y, bounds.min.z - .002f);
            plane.transform.rotation = Quaternion.identity;
            plane.transform.localScale = new Vector3(bounds.size.x * .72f, bounds.size.y * .71f, 1);
            imageMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            imageMaterial.SetColor("_BaseColor", new Color(.83f, .88f, .91f));
            imageMaterial.SetFloat("_Cull", 0);
            plane.GetComponent<Renderer>().sharedMaterial = imageMaterial;
        }

        public static Bounds BoundsOf(Transform content)
        {
            bool found = false; var bounds = new Bounds(content.position, Vector3.zero);
            foreach (var renderer in content.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled || renderer is ParticleSystemRenderer) continue;
                if (!found) { bounds = renderer.bounds; found = true; } else bounds.Encapsulate(renderer.bounds);
            }
            return bounds;
        }

        public bool SetImage(byte[] bytes)
        {
            if (!imageMaterial || !SafeDimensions(bytes)) return false;
            var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!ImageConversion.LoadImage(decoded, bytes, false)) return false;
                int width = Mathf.Min(512, decoded.width), height = Mathf.Max(1, Mathf.RoundToInt(decoded.height * width / (float)decoded.width));
                if (height > 512) { width = Mathf.Max(1, Mathf.RoundToInt(width * 512f / height)); height = 512; }
                var input = decoded.GetPixels32(); var pixels = new Color32[width * height];
                for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                    pixels[y * width + x] = input[Mathf.Min(decoded.height - 1, y * decoded.height / height) * decoded.width + Mathf.Min(decoded.width - 1, x * decoded.width / width)];
                var next = new Texture2D(width, height, TextureFormat.RGBA32, false);
                next.SetPixels32(pixels); next.Apply(false, true);
                if (thumbnail) Destroy(thumbnail); thumbnail = next;
                imageMaterial.SetTexture("_BaseMap", thumbnail); imageMaterial.SetColor("_BaseColor", Color.white);
                return true;
            }
            catch (Exception) { return false; }
            finally { Destroy(decoded); }
        }

        public static bool SafeDimensions(byte[] data)
        {
            if (data == null || data.Length < 24 || data.Length > 16 * 1024 * 1024) return false;
            int width = 0, height = 0;
            if (data[0] == 137 && data[1] == 80 && data[2] == 78 && data[3] == 71)
            {
                width = (data[16] << 24) | (data[17] << 16) | (data[18] << 8) | data[19];
                height = (data[20] << 24) | (data[21] << 16) | (data[22] << 8) | data[23];
            }
            else if (data[0] == 255 && data[1] == 216)
            {
                int offset = 2;
                while (offset + 8 < data.Length)
                {
                    if (data[offset++] != 255) continue;
                    while (offset < data.Length && data[offset] == 255) offset++;
                    if (offset >= data.Length) return false;
                    int marker = data[offset++];
                    if (marker == 217 || marker == 218) break;
                    if (marker == 1 || marker >= 208 && marker <= 215) continue;
                    if (offset + 2 >= data.Length) return false;
                    int length = (data[offset] << 8) | data[offset + 1];
                    if (length < 2 || offset + length > data.Length) return false;
                    if (new[] { 192, 193, 194, 195, 197, 198, 199, 201, 202, 203, 205, 206, 207 }.Contains(marker))
                    { height = (data[offset + 3] << 8) | data[offset + 4]; width = (data[offset + 5] << 8) | data[offset + 6]; break; }
                    offset += length;
                }
            }
            return width > 0 && height > 0 && width <= 8192 && height <= 8192 && (long)width * height <= 16777216;
        }

        void OnDestroy()
        {
            if (accentMaterial) Destroy(accentMaterial);
            if (imageMaterial) Destroy(imageMaterial);
            if (thumbnail) Destroy(thumbnail);
        }
    }
}
