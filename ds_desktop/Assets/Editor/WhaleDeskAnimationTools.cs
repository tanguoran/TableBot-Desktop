using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using WhaleDesk;

// Authoring and real-play verification for the imported Quaternius humanoid clips.
[InitializeOnLoad]
public static class WhaleDeskAnimationTools
{
    const string Source = "Assets/WhaleDesk/Animation/UAL1_Standard.fbx";
    const string ControllerPath = "Assets/WhaleDesk/Animation/WhaleLocomotion.controller";
    const string Request = "Temp/WhaleDeskAnimation.request";
    const string Report = "Temp/WhaleDeskAnimation.report.json";
    const string TestPending = "WhaleDesk.AnimationTest";
    static AnimationChecks checks;

    static WhaleDeskAnimationTools() { EditorApplication.update += Poll; }

    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (EditorApplication.isPlaying && SessionState.GetBool(TestPending, false) && Time.time > 1.5f)
        {
            try
            {
                if (checks == null) checks = new AnimationChecks();
                if (checks.Tick()) { checks = null; SessionState.SetBool(TestPending, false); }
            }
            catch (Exception error)
            {
                checks = null; SessionState.SetBool(TestPending, false);
                File.WriteAllText(Report, JsonUtility.ToJson(new Failure { error = error.ToString() }, true)); Debug.LogException(error);
            }
        }
        if (!File.Exists(Request)) return;
        string command;
        try { command = File.ReadAllText(Request).Trim(); File.Delete(Request); }
        catch (IOException) { return; }
        try
        {
            if (command == "inspect") Inspect();
            else if (command == "install") Install();
            else if (command == "test")
            {
                if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit play mode first.");
                SessionState.SetBool(TestPending, true); EditorApplication.isPlaying = true;
            }
        }
        catch (Exception error) { File.WriteAllText(Report, JsonUtility.ToJson(new Failure { error = error.ToString() }, true)); Debug.LogException(error); }
    }

    [Serializable] class Failure { public string error; }
    [Serializable] class ClipInfo { public string name; public float length; public bool humanoid, loop; public Vector3 averageSpeed; }
    [Serializable] class Inspection
    {
        public bool bakeAxis, validSourceAvatar, validTargetAvatar;
        public string animationType, motionNode;
        public string[] roots, takes;
        public ClipInfo[] clips;
    }

    static void Inspect()
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(Source);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(Source);
        var avatar = AssetDatabase.LoadAllAssetsAtPath(Source).OfType<Avatar>().FirstOrDefault();
        var walker = UnityEngine.Object.FindFirstObjectByType<DesktopWalker>();
        var targetAnimator = walker.visual.GetComponent<Animator>();
        var result = new Inspection {
            bakeAxis = importer.bakeAxisConversion, animationType = importer.animationType.ToString(),
            motionNode = importer.motionNodeName, validSourceAvatar = avatar && avatar.isValid && avatar.isHuman,
            validTargetAvatar = targetAnimator.avatar && targetAnimator.avatar.isValid && targetAnimator.avatar.isHuman,
            roots = model.GetComponentsInChildren<Transform>(true).Where(t => t.name.Equals("root", StringComparison.OrdinalIgnoreCase))
                .Select(t => AnimationUtility.CalculateTransformPath(t, model.transform)).ToArray(),
            takes = importer.defaultClipAnimations.Select(c => c.name).ToArray(),
            clips = AssetDatabase.LoadAllAssetsAtPath(Source).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__"))
                .Select(c => new ClipInfo { name = c.name, length = c.length, humanoid = c.isHumanMotion, loop = c.isLooping, averageSpeed = c.averageSpeed }).ToArray()
        };
        File.WriteAllText(Report, JsonUtility.ToJson(result, true));
    }

    [MenuItem("Whale Desk/Install Humanoid Animations")]
    public static void Install()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit play mode first.");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/WhaleDesk.unity") throw new InvalidOperationException("Open WhaleDesk.unity first.");
        var importer = (ModelImporter)AssetImporter.GetAtPath(Source);
        if (!importer) throw new FileNotFoundException(Source);
        importer.bakeAxisConversion = true;
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.motionNodeName = "Armature/root";
        var clips = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
        foreach (var clip in clips)
        {
            if (clip.name.EndsWith("_Loop", StringComparison.OrdinalIgnoreCase)) clip.loopTime = true;
            if (clip.name == "Armature|Idle_Loop" || clip.name == "Armature|Walk_Loop")
            {
                clip.loopPose = true;
                clip.lockRootRotation = true; clip.lockRootHeightY = true; clip.lockRootPositionXZ = true;
                clip.keepOriginalOrientation = true; clip.keepOriginalPositionXZ = true;
                clip.keepOriginalPositionY = false; clip.heightFromFeet = true;
            }
        }
        importer.clipAnimations = clips;
        importer.SaveAndReimport();
        var sourceAvatar = AssetDatabase.LoadAllAssetsAtPath(Source).OfType<Avatar>().FirstOrDefault();
        if (!sourceAvatar || !sourceAvatar.isValid || !sourceAvatar.isHuman) throw new InvalidOperationException("Invalid animation source Avatar.");
        var imported = AssetDatabase.LoadAllAssetsAtPath(Source).OfType<AnimationClip>().ToArray();
        var idleClip = imported.First(c => c.name == "Armature|Idle_Loop");
        var walkClip = imported.First(c => c.name == "Armature|Walk_Loop");
        if (!idleClip.isHumanMotion || !walkClip.isHumanMotion) throw new InvalidOperationException("Clips must be humanoid.");

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (!controller) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        if (!controller.parameters.Any(p => p.name == "IsWalking")) controller.AddParameter("IsWalking", AnimatorControllerParameterType.Bool);
        if (!controller.parameters.Any(p => p.name == "WalkSpeed"))
            controller.AddParameter(new AnimatorControllerParameter { name = "WalkSpeed", type = AnimatorControllerParameterType.Float, defaultFloat = 1 });
        var machine = controller.layers[0].stateMachine;
        var idle = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == "Idle") ?? machine.AddState("Idle", new Vector3(250, 90));
        var walk = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == "Walk") ?? machine.AddState("Walk", new Vector3(500, 90));
        idle.motion = idleClip; walk.motion = walkClip; machine.defaultState = idle;
        walk.speedParameter = "WalkSpeed"; walk.speedParameterActive = true;
        idle.writeDefaultValues = false; walk.writeDefaultValues = false;
        ConfigureTransition(idle, walk, AnimatorConditionMode.If);
        ConfigureTransition(walk, idle, AnimatorConditionMode.IfNot);
        string[] actionNames = { "SitEnter", "SitIdle", "StandUp", "JumpStart", "JumpLoop", "JumpLand" };
        string[] clipNames = { "Sitting_Enter", "Sitting_Idle_Loop", "Sitting_Exit", "Jump_Start", "Jump_Loop", "Jump_Land" };
        float[] speeds = { 1.5f, 1, 1.5f, 2, 1, 1.5f };
        for (int i = 0; i < actionNames.Length; i++)
        {
            var state = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == actionNames[i]) ??
                machine.AddState(actionNames[i], new Vector3(250 + i % 3 * 220, 240 + i / 3 * 100));
            state.motion = imported.First(c => c.name == "Armature|" + clipNames[i]);
            state.speed = speeds[i]; state.writeDefaultValues = false;
            EditorUtility.SetDirty(state);
        }

        var walker = UnityEngine.Object.FindFirstObjectByType<DesktopWalker>();
        if (!walker || !walker.visual) throw new InvalidOperationException("Install the walking system first.");
        var animator = BindExistingController(walker);
        if (!animator || !animator.avatar || !animator.avatar.isValid || !animator.avatar.isHuman)
            throw new InvalidOperationException("Whale girl's original humanoid Avatar is not valid.");
        walker.walkAnimationReferenceSpeed = .34f;
        var actions = walker.GetComponent<DesktopActionRunner>();
        if (!actions) actions = walker.gameObject.AddComponent<DesktopActionRunner>();
        actions.walker = walker; BindActionClips(actions); EditorUtility.SetDirty(actions);
        // The mannequin is a source preview, not the desktop companion. Preserve it but hide it.
        foreach (var item in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)))
            if (PrefabUtility.IsAnyPrefabInstanceRoot(item.gameObject) &&
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(item.gameObject) == Source)
                item.gameObject.SetActive(false);
        EditorUtility.SetDirty(animator); EditorUtility.SetDirty(walker); EditorUtility.SetDirty(controller);
        EditorUtility.SetDirty(machine); EditorUtility.SetDirty(idle); EditorUtility.SetDirty(walk);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = walker.visual.gameObject;
        File.WriteAllText(Report, JsonUtility.ToJson(new Installed {
            controller = ControllerPath, idle = idleClip.name, walk = walkClip.name,
            loopingClips = clips.Count(c => c.loopTime), avatar = animator.avatar.name,
            bakeAxis = importer.bakeAxisConversion, motionNode = importer.motionNodeName,
            applyRootMotion = animator.applyRootMotion
        }, true));
        Debug.Log("Whale girl Idle/Walk humanoid animations installed.");
    }

    [Serializable] class Installed { public string controller, idle, walk, avatar, motionNode; public int loopingClips; public bool bakeAxis, applyRootMotion; }

    public static void BindActionClips(DesktopActionRunner actions)
    {
        var clips = AssetDatabase.LoadAllAssetsAtPath(Source).OfType<AnimationClip>().ToArray();
        actions.sitEnter = clips.FirstOrDefault(c => c.name == "Armature|Sitting_Enter");
        actions.sitIdle = clips.FirstOrDefault(c => c.name == "Armature|Sitting_Idle_Loop");
        actions.sitExit = clips.FirstOrDefault(c => c.name == "Armature|Sitting_Exit");
        actions.jumpStart = clips.FirstOrDefault(c => c.name == "Armature|Jump_Start");
        actions.jumpLoop = clips.FirstOrDefault(c => c.name == "Armature|Jump_Loop");
        actions.jumpLand = clips.FirstOrDefault(c => c.name == "Armature|Jump_Land");
    }

    public static Animator BindExistingController(DesktopWalker walker)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        var animator = walker.visual ? walker.visual.GetComponent<Animator>() : null;
        if (controller && animator)
        {
            animator.runtimeAnimatorController = controller; animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; walker.animator = animator;
            EditorUtility.SetDirty(animator);
        }
        return animator;
    }

    static void ConfigureTransition(AnimatorState from, AnimatorState to, AnimatorConditionMode condition)
    {
        var transition = from.transitions.FirstOrDefault(t => t.destinationState == to) ?? from.AddTransition(to);
        transition.hasExitTime = false; transition.hasFixedDuration = true; transition.duration = .15f;
        transition.conditions = new[] { new AnimatorCondition { mode = condition, parameter = "IsWalking", threshold = 0 } };
        transition.interruptionSource = TransitionInterruptionSource.SourceThenDestination;
        EditorUtility.SetDirty(transition);
    }

    [Serializable] class TestResult
    {
        public bool passed;
        public string[] checks;
        public float legSwingDegrees, minimumFootHeight, maximumIdleDrift, walkCycles, maximumVisualDrift;
    }

    sealed class AnimationChecks
    {
        readonly DesktopWalker walker = UnityEngine.Object.FindFirstObjectByType<DesktopWalker>();
        Animator animator;
        Transform leg, foot;
        Quaternion initialLeg;
        Vector3 initialRoot, visualLocal;
        float start, idleSince, maxSwing, minFoot = float.PositiveInfinity, maxDrift, cycles, visualDrift;
        bool walkingObserved;
        int stage;

        static void Require(bool valid, string message) { if (!valid) throw new Exception("Animation check: " + message); }

        public bool Tick()
        {
            Require(walker && walker.IsReady, "walker ready");
            if (stage == 0)
            {
                animator = walker.animator;
                Require(animator && animator.avatar.isValid && animator.isHuman, "valid whale humanoid Animator");
                Require(!animator.applyRootMotion, "navigation owns root motion");
                Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"), "initial Idle state");
                leg = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                foot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                initialLeg = leg.localRotation; initialRoot = walker.transform.position; visualLocal = walker.visual.localPosition;
                start = Time.time; stage = 1;
            }
            visualDrift = Mathf.Max(visualDrift, Vector3.Distance(visualLocal, walker.visual.localPosition));
            minFoot = Mathf.Min(minFoot, foot.position.y - walker.navigation.SurfaceHeight);
            Require(float.IsFinite(foot.position.y), "finite bone pose");
            if (stage == 1)
            {
                maxDrift = Mathf.Max(maxDrift, Vector3.Distance(walker.transform.position, initialRoot));
                if (Time.time - start > 3)
                {
                    Require(maxDrift < .002f, "looping Idle must not drift");
                    Require(walker.TryMoveTo(new Vector3(1.33f, walker.navigation.SurfaceHeight, -.47f)), "walking target reachable");
                    start = Time.time; stage = 2;
                }
            }
            if (stage == 2)
            {
                var state = animator.GetCurrentAnimatorStateInfo(0);
                if (state.IsName("Walk") && !animator.IsInTransition(0))
                {
                    walkingObserved = true;
                    cycles = Mathf.Max(cycles, state.normalizedTime);
                    maxSwing = Mathf.Max(maxSwing, Quaternion.Angle(initialLeg, leg.localRotation));
                    Require(animator.GetCurrentAnimatorClipInfo(0).Any(c => c.clip.name == "Armature|Walk_Loop"), "actual imported walk clip");
                }
                Require(walker.State != WalkState.Failed, "navigation during animation: " + walker.LastError);
                if (walker.State == WalkState.Arrived) { idleSince = Time.time; initialRoot = walker.transform.position; stage = 3; }
                Require(Time.time - start < 16, "arrival timeout");
            }
            if (stage == 3 && Time.time - idleSince > 3)
            {
                maxDrift = Mathf.Max(maxDrift, Vector3.Distance(walker.transform.position, initialRoot));
                Require(walkingObserved && cycles > 1 && maxSwing > 5, "humanoid walk must loop and move actual leg bones");
                Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle") && !animator.GetBool("IsWalking"), "returns to Idle after arrival");
                Require(maxDrift < .002f && visualDrift < .002f, "no root or visual transform drift");
                Require(minFoot > -.035f, "feet must not sink into desktop");
                File.WriteAllText(Report, JsonUtility.ToJson(new TestResult {
                    passed = true, legSwingDegrees = maxSwing, minimumFootHeight = minFoot,
                    maximumIdleDrift = maxDrift, walkCycles = cycles, maximumVisualDrift = visualDrift,
                    checks = new[] { "Valid original whale Avatar", "Looping Idle without drift", "Imported Walk clip loops and animates legs",
                        "Agent reaches target around book", "Returns to Idle after arrival", "No transform drift or foot sinking" }
                }, true));
                Debug.Log("Whale humanoid animation checks passed.");
                return true;
            }
            return false;
        }
    }
}
