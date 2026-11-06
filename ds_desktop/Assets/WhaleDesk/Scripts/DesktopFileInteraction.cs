using System;
using System.Linq;

namespace WhaleDesk
{
    public sealed class DesktopFileInteraction : DesktopObjectBehaviour
    {
        DesktopFileBinding Binding => GetComponent<DesktopFileBinding>();
        // Model-triggered opening is restricted to documents/media and folders. Manual double-click is unchanged.
        static readonly string[] Extensions = { ".txt", ".md", ".pdf", ".docx", ".xlsx", ".pptx", ".csv",
            ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".mp3", ".wav", ".mp4", ".mkv" };
        bool CanOpen => Binding && Binding.Entry != null && (Binding.Entry.directory || Extensions.Contains(Binding.Entry.extension.ToLowerInvariant()));
        public override ObjectValue[] State() => new[] { ObjectValue.Bool("last_open_succeeded", Binding && Binding.OpenedLastTime) };
        public override ObjectInteraction[] Interactions() => new[] { new ObjectInteraction {
            operation = "open_external", description = "Open with the Windows default application; does not read contents",
            available = CanOpen, allow_autonomous = false, unavailable_reason = CanOpen ? "" : "File type is not supported for model opening" } };
        public override bool Execute(string operation, out string error)
        {
            bool ok = operation == "open_external" && CanOpen && Binding.OpenExternal();
            error = ok ? "" : "File unavailable, changed, unsupported, or Windows could not open it";
            return ok;
        }
    }
}
