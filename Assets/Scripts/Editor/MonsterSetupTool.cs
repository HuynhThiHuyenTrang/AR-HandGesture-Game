#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Công cụ tự động tạo Animator Controller và Prefab quái vật Slime chuẩn cho game AR
/// Xuất hiện trên thanh công cụ Unity: AR Monster Hunter -> Thiết lập quái vật Slime
/// </summary>
public class MonsterSetupTool
{
    [MenuItem("AR Monster Hunter/1. Tao Animator Controller va Monster Slime Prefab", false, 1)]
    public static void CreateSlimePrefabAndController()
    {
        string animationsFolder = "Assets/Animations";
        if (!AssetDatabase.IsValidFolder(animationsFolder))
        {
            AssetDatabase.CreateFolder("Assets", "Animations");
        }

        string prefabsFolder = "Assets/Prefabs";
        if (!AssetDatabase.IsValidFolder(prefabsFolder))
        {
            AssetDatabase.CreateFolder("Assets", "Prefabs");
        }

        // 1. Tạo Animator Controller
        string controllerPath = "Assets/Animations/SlimeAnimatorController.controller";
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);

        // 2. Thêm các tham số điều khiển (Parameters)
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Die", AnimatorControllerParameterType.Trigger);

        // 3. Tìm các clip animation từ file FBX gốc
        string fbxPath = "Assets/Level 1 Monster Pack/Models/Slime_Level_1.fbx";
        Object[] allAssets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);

        AnimationClip idleClip = null;
        AnimationClip moveClip = null;
        AnimationClip attackClip = null;
        AnimationClip dieClip = null;

        foreach (var asset in allAssets)
        {
            if (asset is AnimationClip clip)
            {
                if (clip.name == "slime_idle") idleClip = clip;
                else if (clip.name == "slime_move") moveClip = clip;
                else if (clip.name == "slime_attack") attackClip = clip;
                else if (clip.name == "slime_die") dieClip = clip;
            }
        }

        // 4. Tạo các State trong Animator Layer mặc định
        var rootStateMachine = controller.layers[0].stateMachine;

        var stateIdle = rootStateMachine.AddState("Idle");
        stateIdle.motion = idleClip;

        var stateMove = rootStateMachine.AddState("Move");
        stateMove.motion = moveClip;

        var stateAttack = rootStateMachine.AddState("Attack");
        stateAttack.motion = attackClip;

        var stateDie = rootStateMachine.AddState("Die");
        stateDie.motion = dieClip;

        // Trạng thái mặc định là Idle
        rootStateMachine.defaultState = stateIdle;

        // 5. Tạo Transition giữa Idle và Move dựa theo tham số Speed
        var idleToMove = stateIdle.AddTransition(stateMove);
        idleToMove.hasExitTime = false;
        idleToMove.duration = 0.15f;
        idleToMove.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");

        var moveToIdle = stateMove.AddTransition(stateIdle);
        moveToIdle.hasExitTime = false;
        moveToIdle.duration = 0.15f;
        moveToIdle.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");

        // Transition Any State -> Attack khi có Trigger "Attack"
        var anyToAttack = rootStateMachine.AddAnyStateTransition(stateAttack);
        anyToAttack.hasExitTime = false;
        anyToAttack.duration = 0.1f;
        anyToAttack.AddCondition(AnimatorConditionMode.If, 0, "Attack");

        // Sau khi Attack xong thì quay về Idle
        var attackToIdle = stateAttack.AddTransition(stateIdle);
        attackToIdle.hasExitTime = true;
        attackToIdle.exitTime = 0.9f;
        attackToIdle.duration = 0.2f;

        // Transition Any State -> Die khi có Trigger "Die"
        var anyToDie = rootStateMachine.AddAnyStateTransition(stateDie);
        anyToDie.hasExitTime = false;
        anyToDie.duration = 0.1f;
        anyToDie.AddCondition(AnimatorConditionMode.If, 0, "Die");

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        // 6. Tạo Monster Prefab từ Slime_Green
        string sourcePrefabPath = "Assets/Level 1 Monster Pack/Prefabs/Slime/Slime_Green.prefab";
        GameObject sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePrefabPath);

        if (sourcePrefab == null)
        {
            Debug.LogError($"[MonsterSetupTool] Không tìm thấy prefab nguồn tại: {sourcePrefabPath}");
            return;
        }

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(sourcePrefab);
        instance.name = "Monster_Slime";
        instance.transform.localScale = Vector3.one * 50.0f; // Phóng to 50x để quái vật đạt chiều cao ~1.3m, to lớn, oai vệ, dễ nhìn trong AR

        // Gán Animator Controller
        Animator anim = instance.GetComponent<Animator>();
        if (anim == null)
        {
            anim = instance.AddComponent<Animator>();
        }
        anim.runtimeAnimatorController = controller;
        anim.applyRootMotion = false; // Tắt root motion để code FSM kiểm soát di chuyển mượt mà

        // Thêm Collider để quái có thể nhận sát thương từ người chơi
        SphereCollider collider = instance.GetComponent<SphereCollider>();
        if (collider == null)
        {
            collider = instance.AddComponent<SphereCollider>();
        }
        collider.center = new Vector3(0, 0.013f, 0);
        collider.radius = 0.015f;

        // Thêm script điều khiển AI quái vật
        MonsterController monsterAI = instance.GetComponent<MonsterController>();
        if (monsterAI == null)
        {
            monsterAI = instance.AddComponent<MonsterController>();
        }

        // Lưu thành Prefab mới trong Assets/Prefabs/
        string targetPrefabPath = "Assets/Prefabs/Monster_Slime.prefab";
        PrefabUtility.SaveAsPrefabAsset(instance, targetPrefabPath);

        // Xóa đối tượng tạm trên Scene
        Object.DestroyImmediate(instance);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"<color=green><b>[AR Monster Hunter]</b> Tạo thành công Animator Controller tại {controllerPath} và Prefab Quái vật tại {targetPrefabPath}!</color>");
        EditorUtility.DisplayDialog("Thành Công", "Đã tạo xong:\n1. Assets/Animations/SlimeAnimatorController.controller\n2. Assets/Prefabs/Monster_Slime.prefab\n\nTiếp tục bấm menu: '2. Tu Dong Gan Component Vao XR Origin' để liên kết tự động!", "OK");
    }

    [MenuItem("AR Monster Hunter/2. Tu Dong Gan Component Vao XR Origin", false, 2)]
    public static void SetupXROriginComponents()
    {
        // 1. Tìm GameObject XR Origin trong Scene
        GameObject xrOriginObj = GameObject.Find("XR Origin");
        if (xrOriginObj == null)
        {
            Unity.XR.CoreUtils.XROrigin xrOriginComp = Object.FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (xrOriginComp != null)
                xrOriginObj = xrOriginComp.gameObject;
        }

        if (xrOriginObj == null)
        {
            EditorUtility.DisplayDialog("Lỗi", "Không tìm thấy GameObject 'XR Origin' trong Scene hiện tại!", "OK");
            return;
        }

        // 2. Gán Monster_Slime prefab vào PlaceObjectOnPlane
        string monsterPrefabPath = "Assets/Prefabs/Monster_Slime.prefab";
        GameObject monsterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(monsterPrefabPath);

        PlaceObjectOnPlane placeScript = xrOriginObj.GetComponent<PlaceObjectOnPlane>();
        if (placeScript != null && monsterPrefab != null)
        {
            SerializedObject serializedPlace = new SerializedObject(placeScript);
            SerializedProperty cubeProp = serializedPlace.FindProperty("cubePrefab");
            if (cubeProp != null)
            {
                cubeProp.objectReferenceValue = monsterPrefab;
                serializedPlace.ApplyModifiedProperties();
            }
        }

        // 3. Thêm GestureRecognizer nếu chưa có
        if (xrOriginObj.GetComponent<GestureRecognizer>() == null)
        {
            xrOriginObj.AddComponent<GestureRecognizer>();
        }

        // 4. Thêm PlayerCombatController nếu chưa có
        if (xrOriginObj.GetComponent<PlayerCombatController>() == null)
        {
            xrOriginObj.AddComponent<PlayerCombatController>();
        }

        // 5. Thêm ARCameraStreamBridge nếu chưa có
        ARCameraStreamBridge bridge = xrOriginObj.GetComponent<ARCameraStreamBridge>();
        if (bridge == null)
        {
            bridge = xrOriginObj.AddComponent<ARCameraStreamBridge>();
        }

        // Tự động tìm và liên kết ARCameraManager từ Main Camera
        UnityEngine.XR.ARFoundation.ARCameraManager camManager = Object.FindAnyObjectByType<UnityEngine.XR.ARFoundation.ARCameraManager>();
        if (camManager != null && bridge != null)
        {
            SerializedObject serializedBridge = new SerializedObject(bridge);
            SerializedProperty camProp = serializedBridge.FindProperty("arCameraManager");
            if (camProp != null)
            {
                camProp.objectReferenceValue = camManager;
                serializedBridge.ApplyModifiedProperties();
            }
        }

        // 6. Thêm MediaPipeHandTracker nếu chưa có
        if (xrOriginObj.GetComponent<MediaPipeHandTracker>() == null)
        {
            xrOriginObj.AddComponent<MediaPipeHandTracker>();
        }

        // 7. Thêm GameHUDController nếu chưa có
        if (xrOriginObj.GetComponent<GameHUDController>() == null)
        {
            xrOriginObj.AddComponent<GameHUDController>();
        }

        // 8. Thêm MonsterSpawner và gán prefab nếu chưa có
        MonsterSpawner spawner = xrOriginObj.GetComponent<MonsterSpawner>();
        if (spawner == null)
        {
            spawner = xrOriginObj.AddComponent<MonsterSpawner>();
        }
        if (monsterPrefab != null && spawner != null)
        {
            SerializedObject serializedSpawner = new SerializedObject(spawner);
            SerializedProperty prefabProp = serializedSpawner.FindProperty("monsterPrefab");
            if (prefabProp != null)
            {
                prefabProp.objectReferenceValue = monsterPrefab;
                serializedSpawner.ApplyModifiedProperties();
            }
        }

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(xrOriginObj.scene);
        Debug.Log("<color=green><b>[AR Monster Hunter]</b> Đã tự động cấu hình xong toàn bộ Component cho XR Origin!</color>");
        EditorUtility.DisplayDialog("Thành Công", "Đã cấu hình xong:\n1. Gán Monster_Slime vào PlaceObjectOnPlane (Scale 8x).\n2. Gắn GestureRecognizer (4 quy tắc Grab, Release, Shield, Push).\n3. Gắn PlayerCombatController.\n4. Gắn ARCameraStreamBridge (tạo nền 3D Webcam).\n5. Gắn MediaPipeHandTracker.\n6. Gắn GameHUDController & MonsterSpawner.\n\nBây giờ bạn chỉ cần bấm PLAY để trải nghiệm!", "OK");
    }

    [MenuItem("AR Monster Hunter/3. Tao Monster Bat Prefab (Cho Level 2+)", false, 3)]
    public static void CreateBatPrefab()
    {
        string batSourcePath = "Assets/Level 1 Monster Pack/Prefabs/Bat/Bat_Violet.prefab";
        GameObject batSource = AssetDatabase.LoadAssetAtPath<GameObject>(batSourcePath);
        if (batSource == null)
        {
            Debug.LogError($"[MonsterSetupTool] Không tìm thấy prefab nguồn tại {batSourcePath}");
            return;
        }

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(batSource);
        instance.name = "Monster_Bat";
        instance.transform.localScale = Vector3.one * 50.0f;

        SphereCollider collider = instance.GetComponent<SphereCollider>();
        if (collider == null)
            collider = instance.AddComponent<SphereCollider>();
        collider.center = new Vector3(0, 1.5f, 0);
        collider.radius = 2.5f;

        MonsterController monsterAI = instance.GetComponent<MonsterController>();
        if (monsterAI == null)
            monsterAI = instance.AddComponent<MonsterController>();

        string targetBatPath = "Assets/Prefabs/Monster_Bat.prefab";
        PrefabUtility.SaveAsPrefabAsset(instance, targetBatPath);
        Object.DestroyImmediate(instance);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // Tự động gán vào MonsterSpawner trong Scene nếu có
        MonsterSpawner spawner = Object.FindAnyObjectByType<MonsterSpawner>();
        if (spawner != null)
        {
            GameObject savedBat = AssetDatabase.LoadAssetAtPath<GameObject>(targetBatPath);
            SerializedObject serSpawner = new SerializedObject(spawner);
            SerializedProperty batProp = serSpawner.FindProperty("batPrefab");
            if (batProp != null)
            {
                batProp.objectReferenceValue = savedBat;
                serSpawner.ApplyModifiedProperties();
            }
        }

        Debug.Log($"<color=green><b>[AR Monster Hunter]</b> Tạo thành công Monster_Bat.prefab tại {targetBatPath}!</color>");
        EditorUtility.DisplayDialog("Thành Công", "Đã tạo xong: Assets/Prefabs/Monster_Bat.prefab và tự động liên kết vào MonsterSpawner cho Level 2+!", "OK");
    }
}
#endif
