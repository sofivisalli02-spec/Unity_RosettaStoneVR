using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.UI;

// Builds the whole Rosetta Stone scene: menu Rosetta > Build Scene.
// Running it again rebuilds the scene from scratch (manual changes to the scene are lost).
public static class RosettaSceneBuilder
{
    const string Root = "Assets/Rosetta";
    const string MatDir = Root + "/Materials";
    const string ModelDir = Root + "/Models";
    const string ScenePath = Root + "/RosettaStone.unity";
    const string SamplesDir = "Assets/Samples/XR Interaction Toolkit/3.5.1";

    // Real size of the Rosetta Stone (British Museum): 112.3 x 75.7 x 28.4 cm
    const float RealHeight = 1.123f;
    const float RealWidth = 0.757f;
    const float RealDepth = 0.284f;
    const float StoneScale = 1.3f;     // the stone is shown 30% larger than the real one, to read it better in VR
    const float PedestalHeight = 0.6f;
    // Base of the stone relative to the visitor's start (x right, y up from the floor, z forward); the museum mode changes it
    static Vector3 StoneBase = new Vector3(0f, PedestalHeight, 1.6f);
    const float ModelYaw = 0f; // set to 180 if the inscription of the model faces away from the visitor

    // Bands of the inscription, as fractions of the stone height measured from the top
    // (hieroglyphic top, demotic middle, Greek bottom). Measured on the British Museum model.
    static float hieroTop = 0.14f, hieroBottom = 0.37f, demoticBottom = 0.64f, greekBottom = 0.97f;

    [MenuItem("Rosetta/Import TMP Essentials")]
    public static void ImportTMP()
    {
        if (AssetDatabase.FindAssets("t:TMP_Settings").Length == 0)
            AssetDatabase.ImportPackage("Packages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage", false);
    }

    // The stone in its glass case in a gallery inspired by Room 4 (Egyptian sculpture) of the British Museum
    [MenuItem("Rosetta/Build British Museum Room")]
    public static void Build() => BuildExhibit(false);
    const string BritishMuseumOutPath = Root + "/RosettaBritishMuseum.unity";

    // Places the exhibit inside the teacher's VR_Museum showroom (saved as a copy, VR_Museum stays untouched)
    [MenuItem("Rosetta/Build in VR Museum")]
    public static void BuildInMuseum() => BuildExhibit(true);

    const string MuseumScenePath = "Assets/Scenes/VR_Museum.unity";
    const string MuseumOutPath = Root + "/RosettaMuseum.unity";

    static void BuildExhibit(bool museum)
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (museum) EditorSceneManager.OpenScene(MuseumScenePath, OpenSceneMode.Single);
        else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        string outPath = museum ? MuseumOutPath : BritishMuseumOutPath;

        // In the British Museum room the stone is further away, behind the front glass of the case
        StoneBase = new Vector3(0f, PedestalHeight, museum ? 1.6f : 2.4f);
        caseBlocker = null;
        var exhibitPos = Vector3.zero;
        var exhibitRot = Quaternion.identity;
        float platformY = 0f;
        var rig = museum ? GameObject.Find("XR Origin (XR Rig)") : null;
        if (museum && rig != null)
            AnalyzePodium(rig.transform, out exhibitPos, out exhibitRot, out platformY);

        // Everything is built around the visitor at (0,0,0) looking along +Z, then the root is moved into place
        var exhibit = new GameObject("Rosetta Exhibit").transform;

        // ---------- Materials ----------
        var floorMat = MakeMat("Floor", new Color(0.55f, 0.48f, 0.38f), false, false);
        var wallMat = MakeMat("Wall", new Color(0.16f, 0.2f, 0.26f), false, false);
        var pedestalMat = MakeMat("Pedestal", new Color(0.12f, 0.12f, 0.13f), false, false);
        var hologramMat = MakeMat("Hologram", new Color(0.35f, 0.8f, 1f, 0.45f), true, true);
        var lineMat = MakeMat("HologramLines", new Color(0.7f, 0.95f, 1f, 0.8f), true, true);
        // Light veils on the inscription, so the text stays readable, plus a thin golden frame
        var zoneNormal = MakeMat("ZoneNormal", new Color(1f, 0.9f, 0.6f, 0.03f), true, true);
        var zoneHover = MakeMat("ZoneHover", new Color(1f, 0.92f, 0.7f, 0.12f), true, true);
        var zoneHighlight = MakeMat("ZoneHighlight", new Color(1f, 0.8f, 0.35f, 0.22f), true, true);
        zoneOutline = MakeMat("ZoneOutline", new Color(0.95f, 0.76f, 0.3f, 0.9f), true, true);

        // ---------- Light and room (only for the stand-alone scene; the museum has its own) ----------
        if (!museum)
        {
            BuildGallery();
            BuildCase(exhibit);
        }
        else if (StoneBase.y - platformY > 0.05f)
        {
            // Pedestal on the showroom podium, so the stone stands at a comfortable height
            float ph = StoneBase.y - platformY;
            Box("Pedestal", exhibit, new Vector3(0, platformY + ph / 2f, StoneBase.z), new Vector3(0.9f, ph, 0.45f), pedestalMat);
        }

        // ---------- Rosetta Stone ----------
        var stone = new GameObject("Rosetta Stone").transform;
        stone.SetParent(exhibit, false);
        stone.position = StoneBase;
        float w = RealWidth * StoneScale, h = RealHeight * StoneScale, d = RealDepth * StoneScale;

        var modelAsset = FindModel();
        if (modelAsset != null)
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
            model.name = "Rosetta Stone Model";
            model.transform.SetParent(stone, false);
            model.transform.localRotation = Quaternion.Euler(0f, ModelYaw, 0f) * model.transform.localRotation;
            var b = Bounds(model);
            float s = h / b.size.y;
            model.transform.localScale *= s;
            b = Bounds(model);
            model.transform.position += new Vector3(StoneBase.x - b.center.x, StoneBase.y - b.min.y, StoneBase.z - b.center.z);
            b = Bounds(model);
            w = b.size.x; h = b.size.y; d = b.size.z;

            // The texture is exported next to the FBX: build the material here so it does not depend on embedded textures
            var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(ModelDir + "/RosettaStone_Albedo.jpg");
            if (albedo != null)
            {
                var stoneMat = MakeMat("RosettaStone", Color.white, false, false);
                stoneMat.SetTexture("_BaseMap", albedo);
                stoneMat.SetFloat("_Smoothness", 0.15f);
                foreach (var r in model.GetComponentsInChildren<Renderer>())
                    r.sharedMaterials = Enumerable.Repeat(stoneMat, r.sharedMaterials.Length).ToArray();
            }
        }
        else
        {
            // Placeholder hologram with the real proportions, used until the Sketchfab model is in Assets/Rosetta/Models
            var body = Box("Hologram (placeholder)", stone, StoneBase + new Vector3(0, h / 2f, 0), new Vector3(w, h, d), hologramMat);
            Object.DestroyImmediate(body.GetComponent<Collider>());
            AddTextLines(stone, w, h, d, lineMat);
            hieroTop = 0.03f; hieroBottom = 0.27f; demoticBottom = 0.60f; greekBottom = 0.97f;
        }
        float demoticMid = (hieroBottom + demoticBottom) / 2f;
        float greekMid = (demoticBottom + greekBottom) / 2f;

        // ---------- Text zones (front face, facing the visitor) ----------
        // Bands from the top: hieroglyphic (top, largely lost), demotic (middle), Greek (bottom).
        // Passage 0 = honours for Ptolemy V (lost in hieroglyphic), passage 1 = order to write the decree in three scripts.
        float frontZ = StoneBase.z - d / 2f - 0.008f;
        var zonesRoot = new GameObject("Text Zones").transform;
        zonesRoot.SetParent(stone, false);
        const float gap = 0.005f;
        MakeZone(zonesRoot, "P1 Hieroglyphic", 1, TextZone.Script.Hieroglyphic, hieroTop, hieroBottom - gap, w, h, frontZ, zoneNormal, zoneHover, zoneHighlight);
        MakeZone(zonesRoot, "P0 Demotic", 0, TextZone.Script.Demotic, hieroBottom + gap, demoticMid - gap, w, h, frontZ, zoneNormal, zoneHover, zoneHighlight);
        MakeZone(zonesRoot, "P1 Demotic", 1, TextZone.Script.Demotic, demoticMid + gap, demoticBottom - gap, w, h, frontZ, zoneNormal, zoneHover, zoneHighlight);
        MakeZone(zonesRoot, "P0 Greek", 0, TextZone.Script.Greek, demoticBottom + gap, greekMid - gap, w, h, frontZ, zoneNormal, zoneHover, zoneHighlight);
        MakeZone(zonesRoot, "P1 Greek", 1, TextZone.Script.Greek, greekMid + gap, greekBottom, w, h, frontZ, zoneNormal, zoneHover, zoneHighlight);

        // ---------- XR rig, simulator, interaction manager, event system ----------
        // The museum scene already has the rig and the simulator: reuse them
        if (rig == null)
        {
            var rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SamplesDir + "/Starter Assets/Prefabs/XR Origin (XR Rig).prefab");
            rig = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab);
            rig.transform.position = Vector3.zero;
        }
        var xrCamera = rig.GetComponentInChildren<Camera>();

        if (GameObject.Find("XR Interaction Simulator") == null)
        {
            var simPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SamplesDir + "/XR Interaction Simulator/XR Interaction Simulator.prefab");
            if (simPrefab != null) PrefabUtility.InstantiatePrefab(simPrefab);
        }

        if (Object.FindFirstObjectByType<XRInteractionManager>() == null)
            new GameObject("XR Interaction Manager").AddComponent<XRInteractionManager>();

        foreach (var old in Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None))
            Object.DestroyImmediate(old.gameObject);
        var es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<XRUIInputModule>();

        // ---------- Game manager ----------
        var gm = new GameObject("GameManager");
        var languageManager = gm.AddComponent<LanguageManager>();
        var translationManager = gm.AddComponent<TranslationManager>();
        translationManager.selectSound = gm.AddComponent<AudioSource>();
        translationManager.selectSound.playOnAwake = false;
        FillTexts(translationManager);

        // ---------- Translation panel (right of the stone, turned toward the visitor) ----------
        var panel = museum
            ? MakeCanvas("Translation Panel", new Vector3(1.05f, 1.45f, 1.3f), 38f, new Vector2(700, 820), xrCamera)
            : MakeCanvas("Translation Panel", new Vector3(1.35f, 1.5f, 1.15f), 35f, new Vector2(700, 820), xrCamera);
        Background(panel, new Color(0.04f, 0.06f, 0.09f, 0.62f));
        var panelText = Text(panel, "Text", translationManager.chooseText, 32, TextAlignmentOptions.TopLeft, new Vector2(620, 640), new Vector2(0, 50));
        translationManager.translationText = panelText;
        // Go back: reopen the language menu
        var back = MakeButton(panel, "< Lingua / Language", new Vector2(0, -345), new Vector2(380, 70), 28);
        UnityEventTools.AddVoidPersistentListener(back.onClick, languageManager.ShowMenu);

        // ---------- Script labels (right of the stone, as in the British Museum case) ----------
        var labels = MakeCanvas("Script Labels", new Vector3(w / 2f + 0.2f, StoneBase.y + h / 2f, frontZ), 0f, new Vector2(320, h * 1000f), xrCamera);
        float Band(float a, float b) => h * 1000f * (0.5f - (a + b) / 2f);
        var labelColor = museum ? Color.white : new Color(0.15f, 0.15f, 0.18f);
        Text(labels, "Hieroglyphic", "HIEROGLYPHS\n<size=20>sacred script</size>", 30, TextAlignmentOptions.Left, new Vector2(300, 110), new Vector2(0, Band(hieroTop, hieroBottom))).color = labelColor;
        Text(labels, "Demotic", "DEMOTIC\n<size=20>everyday Egyptian script</size>", 30, TextAlignmentOptions.Left, new Vector2(300, 110), new Vector2(0, Band(hieroBottom, demoticBottom))).color = labelColor;
        Text(labels, "Greek", "GREEK\n<size=20>language of the rulers</size>", 30, TextAlignmentOptions.Left, new Vector2(300, 110), new Vector2(0, Band(demoticBottom, greekBottom))).color = labelColor;
        if (!museum) BuildInfoPanels(exhibit, xrCamera);

        // ---------- Credit plaque on the pedestal ----------
        var plaque = MakeCanvas("Credit Plaque", new Vector3(0, StoneBase.y - 0.25f, StoneBase.z - 0.32f), 0f, new Vector2(900, 200), xrCamera);
        Background(plaque, new Color(0.9f, 0.85f, 0.7f, 1f));
        var plaqueText = Text(plaque, "Text",
            "<b>The Rosetta Stone</b>  ·  Memphis, 196 BC  ·  British Museum, EA 24\n<size=20>3D model: The British Museum on Sketchfab, CC BY-NC-SA 4.0. Shown 1.3× larger than the real stone (112 cm high).</size>",
            28, TextAlignmentOptions.Center, new Vector2(860, 180), Vector2.zero);
        plaqueText.color = new Color(0.15f, 0.12f, 0.08f);

        // ---------- Language menu (in front of the visitor at start) ----------
        var menu = MakeCanvas("Language Menu", new Vector3(0, 1.4f, 0.85f), 0f, new Vector2(720, 430), xrCamera);
        Background(menu, new Color(0.04f, 0.06f, 0.09f, 0.72f));
        Text(menu, "Title", "<color=#F2C14E>Scegli la tua lingua</color>\n<size=26>Choose your language · Elige tu idioma · Choisis ta langue · Wähle deine Sprache</size>", 40, TextAlignmentOptions.Center, new Vector2(680, 150), new Vector2(0, 125));
        MakeLanguageButton(menu, "Italiano", new Vector2(-220, -20), languageManager, 0);
        MakeLanguageButton(menu, "English", new Vector2(0, -20), languageManager, 1);
        MakeLanguageButton(menu, "Español", new Vector2(220, -20), languageManager, 2);
        MakeLanguageButton(menu, "Français", new Vector2(-110, -130), languageManager, 3);
        MakeLanguageButton(menu, "Deutsch", new Vector2(110, -130), languageManager, 4);
        languageManager.languageMenu = menu.gameObject;

        foreach (var t in new Transform[] { panel, labels, plaque, menu })
            t.SetParent(exhibit, true);

        // ---------- Museum: move the exhibit and the visitor in front of the podium ----------
        if (museum)
        {
            exhibit.SetPositionAndRotation(exhibitPos, exhibitRot);
            rig.transform.SetPositionAndRotation(exhibitPos, exhibitRot);
            var sim = GameObject.Find("XR Interaction Simulator");
            if (sim != null) sim.transform.SetPositionAndRotation(exhibitPos, exhibitRot);
            AddExhibitLights(exhibit);
        }
        else
        {
            AddExhibitLights(exhibit, false);
            // Bright, diffuse daylight from the skylights of the gallery
            var sun = new GameObject("Daylight").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.3f;
            sun.color = new Color(1f, 0.97f, 0.92f);
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(50f, 160f, 0f); // lights the front of the museum
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.72f, 0.74f, 0.78f);
        }

        // ---------- Desktop visitor (mouse + WASD) and the switch between desktop and VR ----------
        var simulator = GameObject.Find("XR Interaction Simulator");
        var player = new GameObject("Desktop Player");
        // In the British Museum scene the visit starts outside, in front of the museum
        if (museum) player.transform.SetPositionAndRotation(exhibit.position, exhibit.rotation);
        else player.transform.SetPositionAndRotation(ExteriorStart, Quaternion.identity);
        var cc = player.AddComponent<CharacterController>();
        cc.height = 1.7f;
        cc.radius = 0.25f;
        cc.center = new Vector3(0f, 0.85f, 0f);
        cc.stepOffset = 0.2f; // climbs the 15 cm steps of the museum
        var camGo = new GameObject("Desktop Camera");
        camGo.tag = "MainCamera";
        camGo.transform.SetParent(player.transform, false);
        camGo.transform.localPosition = new Vector3(0f, 1.6f, 0f);
        var desktopCam = camGo.AddComponent<Camera>();
        desktopCam.nearClipPlane = 0.05f;
        desktopCam.fieldOfView = 65f;
        camGo.AddComponent<AudioListener>();
        var desktopPlayer = player.AddComponent<DesktopPlayer>();
        desktopPlayer.playerCamera = desktopCam;
        desktopPlayer.languageManager = languageManager;

        var modeSwitch = gm.AddComponent<ModeSwitch>();
        modeSwitch.desktopMode = true;
        modeSwitch.desktopPlayer = player;
        modeSwitch.vrObjects = new[] { rig, simulator };
        modeSwitch.desktopOnly = museum || caseBlocker == null ? new GameObject[0] : new[] { caseBlocker };
        player.SetActive(true);
        rig.SetActive(false);
        if (simulator != null) simulator.SetActive(false);

        // ---------- Save ----------
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.SaveScene(scene, outPath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(outPath, true) };
        AssetDatabase.SaveAssets();

        Snapshots(exhibit, menu.gameObject, panelText, translationManager, outPath);
        Debug.Log("ROSETTA: scene built " + (modelAsset != null ? "with model " + modelAsset.name : "with placeholder hologram"));
    }

    // ================= British Museum, Room 4 =================
    // Simplified from photos of the gallery: long hall with white fluted columns, pale blue-grey walls,
    // light stone floor, coffered ceiling with skylights. The visitor starts at (0,0,0) looking along +Z.

    const float CaseWidth = 2.8f, CaseDepth = 1.5f, CaseHeight = 2.7f, CaseBaseHeight = 0.18f;

    static void BuildGallery()
    {
        var floorMat = MakeMat("BM_Floor", new Color(0.83f, 0.81f, 0.77f), false, false);
        floorMat.SetTexture("_BaseMap", TileTexture());
        floorMat.SetTextureScale("_BaseMap", new Vector2(13f / 1.2f, 26f / 1.2f)); // 1.2 m slabs
        floorMat.SetFloat("_Smoothness", 0.45f);
        var wallMat = MakeMat("BM_Wall", new Color(0.74f, 0.79f, 0.83f), false, false);
        var whiteMat = MakeMat("BM_White", new Color(0.95f, 0.94f, 0.91f), false, false);
        var fluteMat = MakeMat("BM_Flute", new Color(0.82f, 0.81f, 0.78f), false, false);
        var skyMat = MakeMat("BM_Skylight", new Color(1f, 1f, 0.97f), false, true);
        var stoneMat = MakeMat("BM_PlinthStone", new Color(0.72f, 0.70f, 0.66f), false, false);
        var granite = MakeMat("BM_Granite", new Color(0.13f, 0.13f, 0.14f), false, false);
        granite.SetFloat("_Smoothness", 0.35f);
        var sandstone = MakeMat("BM_Sandstone", new Color(0.62f, 0.52f, 0.40f), false, false);

        var hall = new GameObject("British Museum - Room 4").transform;
        float zMin = -8f, zMax = 18f, halfW = 6.5f, height = 7.5f, zMid = (zMin + zMax) / 2f, len = zMax - zMin;

        Box("Floor", hall, new Vector3(0, -0.05f, zMid), new Vector3(halfW * 2f, 0.1f, len), floorMat);
        Box("Wall Left", hall, new Vector3(-halfW, height / 2f, zMid), new Vector3(0.3f, height, len), wallMat);
        Box("Wall Right", hall, new Vector3(halfW, height / 2f, zMid), new Vector3(0.3f, height, len), wallMat);
        Box("Wall Back", hall, new Vector3(0, height / 2f, zMax), new Vector3(halfW * 2f, height, 0.3f), wallMat);
        // Front wall with the entrance door (3 m wide, 4 m high) from the portico of the museum
        float sideW = halfW - 1.5f;
        Box("Wall Front L", hall, new Vector3(-(1.5f + sideW / 2f), height / 2f, zMin), new Vector3(sideW, height, 0.3f), wallMat);
        Box("Wall Front R", hall, new Vector3(1.5f + sideW / 2f, height / 2f, zMin), new Vector3(sideW, height, 0.3f), wallMat);
        Box("Wall Front Top", hall, new Vector3(0, (height + 4f) / 2f, zMin), new Vector3(3f, height - 4f, 0.3f), wallMat);
        BuildExterior(zMin);
        Box("Skirting Left", hall, new Vector3(-halfW + 0.18f, 0.15f, zMid), new Vector3(0.06f, 0.3f, len), whiteMat);
        Box("Skirting Right", hall, new Vector3(halfW - 0.18f, 0.15f, zMid), new Vector3(0.06f, 0.3f, len), whiteMat);

        // Coffered ceiling with a skylight strip along the centre
        Box("Ceiling", hall, new Vector3(0, height + 0.1f, zMid), new Vector3(halfW * 2f, 0.2f, len), whiteMat);
        var sky = Box("Skylight", hall, new Vector3(0, height - 0.02f, zMid), new Vector3(5f, 0.02f, len - 2f), skyMat);
        Object.DestroyImmediate(sky.GetComponent<Collider>());
        for (float z = zMin + 2f; z < zMax; z += 2f)
            Box("Coffer Beam", hall, new Vector3(0, height - 0.15f, z), new Vector3(halfW * 2f, 0.3f, 0.25f), whiteMat);
        foreach (float x in new[] { -4.2f, -2.5f, 2.5f, 4.2f })
            Box("Coffer Beam", hall, new Vector3(x, height - 0.15f, zMid), new Vector3(0.25f, 0.3f, len), whiteMat);

        // Two rows of white fluted columns with base, capital and entablature
        foreach (float x in new[] { -4.2f, 4.2f })
        {
            for (float z = -6f; z <= 16f; z += 4f)
            {
                var col = new GameObject("Column").transform;
                col.SetParent(hall, false);
                Box("Base", col, new Vector3(x, 0.2f, z), new Vector3(1.0f, 0.4f, 1.0f), whiteMat);
                var shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                shaft.name = "Shaft";
                shaft.transform.SetParent(col, false);
                shaft.transform.position = new Vector3(x, 0.4f + 2.75f, z);
                shaft.transform.localScale = new Vector3(0.72f, 2.75f, 0.72f);
                shaft.GetComponent<Renderer>().sharedMaterial = whiteMat;
                for (int i = 0; i < 16; i++)
                {
                    float a = i * Mathf.PI * 2f / 16f;
                    var f = Box("Flute", col, new Vector3(x + Mathf.Cos(a) * 0.355f, 0.4f + 2.75f, z + Mathf.Sin(a) * 0.355f), new Vector3(0.03f, 5.3f, 0.03f), fluteMat);
                    Object.DestroyImmediate(f.GetComponent<Collider>());
                }
                Box("Capital", col, new Vector3(x, 5.9f + 0.18f, z), new Vector3(1.05f, 0.36f, 1.05f), whiteMat);
            }
            Box("Entablature", hall, new Vector3(x, 6.6f, zMid), new Vector3(1.1f, 0.6f, len), whiteMat);
        }

        // A few other Egyptian sculptures on plinths, kept abstract (dark granite and sandstone blocks)
        void Sculpture(string name, Vector3 pos, Vector3 plinth, Vector3 size, Material mat)
        {
            Box(name + " Plinth", hall, pos + new Vector3(0, plinth.y / 2f, 0), plinth, stoneMat);
            Box(name, hall, pos + new Vector3(0, plinth.y + size.y / 2f, 0), size, mat);
        }
        // Two real sculptures from Sketchfab (converted in Blender, in Assets/Rosetta/Statues), facing the centre of the hall
        void Statue(string label, string credit, string file, bool normalMap, Vector3 pos, float yaw, float plinthH, float height)
        {
            Box(label + " Plinth", hall, pos + new Vector3(0, plinthH / 2f, 0), new Vector3(1.0f, plinthH, 1.0f), stoneMat);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(StatueDir + "/" + file + ".fbx");
            if (asset == null) { Debug.LogWarning("ROSETTA: statue not found " + file); return; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            go.name = label;
            go.transform.SetParent(hall, false);
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * go.transform.rotation;
            var b = Bounds(go);
            go.transform.localScale *= height / b.size.y;
            b = Bounds(go);
            go.transform.position += new Vector3(pos.x - b.center.x, plinthH - b.min.y, pos.z - b.center.z);

            var m = MakeMat("Statue_" + file, Color.white, false, false);
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(StatueDir + "/" + file + "_Tex0.png"));
            m.SetFloat("_Smoothness", 0.2f);
            if (normalMap)
            {
                string np = StatueDir + "/" + file + "_Tex2.png";
                var ti = (TextureImporter)AssetImporter.GetAtPath(np);
                if (ti != null && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; ti.SaveAndReimport(); }
                m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(np));
                m.EnableKeyword("_NORMALMAP");
            }
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                r.sharedMaterials = Enumerable.Repeat(m, r.sharedMaterials.Length).ToArray();

            // Small label on the side of the plinth that faces the centre of the hall
            var dir = new Vector3(-Mathf.Sign(pos.x), 0f, 0f);
            var tag = MakeCanvas(label + " Label", pos + dir * 0.505f + new Vector3(0, plinthH * 0.65f, 0), yaw, new Vector2(800, 260), null);
            tag.SetParent(hall, true);
            Background(tag, new Color(0.97f, 0.97f, 0.95f, 1f));
            Text(tag, "Text", "<b>" + label + "</b>\n<size=22>" + credit + "</size>", 30, TextAlignmentOptions.Center, new Vector2(760, 230), Vector2.zero).color = new Color(0.12f, 0.12f, 0.14f);
        }
        Statue("Goddess Isis-Serqet (fragment)", "3D model: The British Museum on Sketchfab, CC BY-NC-SA 4.0", "IsisSerqet", false, new Vector3(-5.3f, 0, 4f), -90f, 1.1f, 0.8f);
        Statue("Egyptian bust", "3D model: jurandm on Sketchfab, CC BY-NC 4.0", "EgyptianBust", true, new Vector3(5.3f, 0, 4f), 90f, 1.1f, 0.75f);
        Sculpture("Sandstone Statue Block", new Vector3(-5.3f, 0, 10f), new Vector3(1.0f, 0.6f, 1.0f), new Vector3(0.7f, 2.0f, 0.7f), sandstone);
        Sculpture("Granite Sarcophagus", new Vector3(0f, 0, 11f), new Vector3(2.6f, 0.3f, 1.3f), new Vector3(2.3f, 1.0f, 1.0f), granite);
        Sculpture("Granite Head", new Vector3(5.3f, 0, 10f), new Vector3(1.2f, 1.2f, 1.2f), new Vector3(0.9f, 1.1f, 0.8f), granite);

        // Room sign, as in the gallery ("4" on a blue square)
        var sign = MakeCanvas("Room Sign", new Vector3(-2.9f, 3.0f, 1.2f), 0f, new Vector2(420, 220), null);
        sign.SetParent(hall, true);
        var signBg = new GameObject("Number", typeof(RectTransform)).AddComponent<Image>();
        signBg.transform.SetParent(sign, false);
        signBg.color = new Color(0.1f, 0.18f, 0.42f);
        signBg.rectTransform.sizeDelta = new Vector2(200, 200);
        signBg.rectTransform.anchoredPosition = new Vector2(-110, 0);
        Text(sign, "4", "4", 140, TextAlignmentOptions.Center, new Vector2(200, 200), new Vector2(-110, 0));
        Text(sign, "Name", "Egyptian\nsculpture", 44, TextAlignmentOptions.Left, new Vector2(220, 200), new Vector2(115, 0)).color = new Color(0.1f, 0.18f, 0.42f);
    }

    // Outside of the museum, inspired by the Greek-revival front of the British Museum:
    // a forecourt, steps, a portico of columns with a pediment and the entrance to Room 4.
    // The desktop visitor starts here (ExteriorStart) and walks in.
    static readonly Vector3 ExteriorStart = new Vector3(0f, -0.55f, -25f);

    static void BuildExterior(float zFront)
    {
        var portland = MakeMat("BM_PortlandStone", new Color(0.86f, 0.84f, 0.78f), false, false);
        var paving = MakeMat("BM_Paving", new Color(0.66f, 0.65f, 0.62f), false, false);
        paving.SetTexture("_BaseMap", TileTexture());
        paving.SetTextureScale("_BaseMap", new Vector2(25f, 20f));
        var lawn = MakeMat("BM_Lawn", new Color(0.33f, 0.47f, 0.27f), false, false);
        var dark = MakeMat("BM_DoorFrame", new Color(0.2f, 0.18f, 0.16f), false, false);
        var lamp = MakeMat("BM_LampGlow", new Color(1f, 0.92f, 0.75f), false, true);
        RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");

        var ext = new GameObject("British Museum - Exterior").transform;
        float zWall = zFront - 0.3f;         // outer face of the building
        float zPortico = zFront - 5.5f;      // front edge of the portico floor (y = 0)

        // Facade wall (wider than the gallery) with the doorway
        Box("Facade L", ext, new Vector3(-8.25f, 5.5f, zWall), new Vector3(13.5f, 11f, 0.3f), portland);
        Box("Facade R", ext, new Vector3(8.25f, 5.5f, zWall), new Vector3(13.5f, 11f, 0.3f), portland);
        Box("Facade Above Door", ext, new Vector3(0, 7.5f, zWall), new Vector3(3f, 7f, 0.3f), portland);
        Box("Door Frame L", ext, new Vector3(-1.6f, 2.1f, zWall - 0.16f), new Vector3(0.2f, 4.2f, 0.1f), dark);
        Box("Door Frame R", ext, new Vector3(1.6f, 2.1f, zWall - 0.16f), new Vector3(0.2f, 4.2f, 0.1f), dark);
        Box("Door Frame Top", ext, new Vector3(0, 4.1f, zWall - 0.16f), new Vector3(3.4f, 0.2f, 0.1f), dark);

        // Portico floor, steps down to the forecourt
        Box("Portico Floor", ext, new Vector3(0, -0.3f, (zPortico + zFront) / 2f), new Vector3(26f, 0.6f, zFront - zPortico), portland);
        for (int k = 1; k <= 3; k++)
        {
            float h = 0.6f - 0.15f * k;
            Box("Step " + k, ext, new Vector3(0, -0.6f + h / 2f, zPortico - 0.4f * k + 0.2f), new Vector3(26f, h, 0.4f), portland);
        }
        Box("Forecourt", ext, new Vector3(0, -0.7f, zPortico - 18f), new Vector3(50f, 0.2f, 36f), paving);
        Box("Lawn L", ext, new Vector3(-15f, -0.58f, zPortico - 14f), new Vector3(14f, 0.04f, 16f), lawn);
        Box("Lawn R", ext, new Vector3(15f, -0.58f, zPortico - 14f), new Vector3(14f, 0.04f, 16f), lawn);

        // Colonnade with fluted columns, entablature and pediment
        float colZ = zPortico + 0.8f;
        for (int i = 0; i < 8; i++)
        {
            float x = -8.75f + i * 2.5f;
            Box("Column Base", ext, new Vector3(x, 0.2f, colZ), new Vector3(1.3f, 0.4f, 1.3f), portland);
            var shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            shaft.name = "Column";
            shaft.transform.SetParent(ext, false);
            shaft.transform.position = new Vector3(x, 0.4f + 3.8f, colZ);
            shaft.transform.localScale = new Vector3(0.95f, 3.8f, 0.95f);
            shaft.GetComponent<Renderer>().sharedMaterial = portland;
            for (int f = 0; f < 12; f++)
            {
                float a = f * Mathf.PI * 2f / 12f;
                var fl = Box("Flute", ext, new Vector3(x + Mathf.Cos(a) * 0.47f, 4.2f, colZ + Mathf.Sin(a) * 0.47f), new Vector3(0.04f, 7.4f, 0.04f), dark);
                Object.DestroyImmediate(fl.GetComponent<Collider>());
            }
            Box("Capital", ext, new Vector3(x, 8.25f, colZ), new Vector3(1.4f, 0.5f, 1.4f), portland);
        }
        Box("Entablature", ext, new Vector3(0, 9.1f, (colZ - 0.8f + zWall) / 2f), new Vector3(21f, 1.2f, zWall - colZ + 1.6f), portland);
        Prism("Pediment", ext, new Vector3(0, 9.7f, colZ - 0.7f), 21f, 2.6f, 1.2f, portland);

        // Name on the frieze, sign and lamps in the forecourt
        var name = MakeCanvas("Frieze Inscription", new Vector3(0, 9.1f, colZ - 1.22f), 0f, new Vector2(16000, 1100), null);
        name.SetParent(ext, true);
        Text(name, "Text", "BRITISH MUSEUM", 800, TextAlignmentOptions.Center, new Vector2(16000, 1100), Vector2.zero).color = new Color(0.32f, 0.3f, 0.27f);

        Box("Sign Post", ext, new Vector3(3f, -0.1f, zPortico - 5f), new Vector3(0.12f, 1.0f, 0.12f), dark);
        var sign = MakeCanvas("Entrance Sign", new Vector3(3f, 0.7f, zPortico - 5f), -10f, new Vector2(900, 600), null);
        sign.SetParent(ext, true);
        Background(sign, new Color(0.1f, 0.18f, 0.42f, 0.95f));
        Text(sign, "Text", "<b>The British Museum</b>\n\n<size=44>Room 4 · Egyptian sculpture</size>\n<size=44><color=#F2C14E>The Rosetta Stone  ></color></size>", 60, TextAlignmentOptions.Center, new Vector2(820, 540), Vector2.zero);

        foreach (float x in new[] { -6f, 6f })
            foreach (float dz in new[] { -4f, -14f })
            {
                Box("Lamp Post", ext, new Vector3(x, 1.4f, zPortico + dz), new Vector3(0.12f, 4.2f, 0.12f), dark);
                var bulb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                bulb.name = "Lamp";
                bulb.transform.SetParent(ext, false);
                bulb.transform.position = new Vector3(x, 3.6f, zPortico + dz);
                bulb.transform.localScale = Vector3.one * 0.4f;
                bulb.GetComponent<Renderer>().sharedMaterial = lamp;
                Object.DestroyImmediate(bulb.GetComponent<Collider>());
            }
    }

    // Triangular prism (for the pediment): base centred on 'bottomCentre', pointing up, depth along Z
    static void Prism(string name, Transform parent, Vector3 bottomCentre, float width, float height, float depth, Material mat)
    {
        float w = width / 2f, d = depth / 2f;
        var v = new[]
        {
            new Vector3(-w, 0, -d), new Vector3(w, 0, -d), new Vector3(0, height, -d),
            new Vector3(-w, 0, d), new Vector3(w, 0, d), new Vector3(0, height, d)
        };
        var verts = new System.Collections.Generic.List<Vector3>();
        var tris = new System.Collections.Generic.List<int>();
        void Face(params int[] idx)
        {
            int s = verts.Count;
            foreach (int i in idx) verts.Add(v[i]);
            for (int i = 1; i < idx.Length - 1; i++) { tris.Add(s); tris.Add(s + i); tris.Add(s + i + 1); }
        }
        Face(0, 2, 1);       // front
        Face(3, 4, 5);       // back
        Face(0, 3, 5, 2);    // left slope
        Face(1, 2, 5, 4);    // right slope
        Face(0, 1, 4, 3);    // bottom
        var mesh = new Mesh { name = name };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        string meshPath = MatDir + "/" + name + "Mesh.asset";
        AssetDatabase.DeleteAsset(meshPath);
        AssetDatabase.CreateAsset(mesh, meshPath);

        // Double-sided copy of the material, so every face of the hand-made mesh is visible
        var twoSided = new Material(mat);
        twoSided.SetFloat("_Cull", 0f);
        string matPath = MatDir + "/" + name + "TwoSided.mat";
        AssetDatabase.DeleteAsset(matPath);
        AssetDatabase.CreateAsset(twoSided, matPath);

        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = bottomCentre;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = twoSided;
    }

    // Glass case on a low white base, with a sand-coloured pedestal inside (as in the photos)
    static void BuildCase(Transform exhibit)
    {
        var baseMat = MakeMat("Case_Base", new Color(0.93f, 0.93f, 0.91f), false, false);
        var glassMat = MakeMat("Case_Glass", new Color(0.86f, 0.95f, 0.95f, 0.10f), true, false);
        glassMat.SetFloat("_Smoothness", 0.6f);
        var edgeMat = MakeMat("Case_Edge", new Color(0.85f, 0.9f, 0.9f, 0.5f), true, false);
        var pedestalMat = MakeMat("Case_Pedestal", new Color(0.80f, 0.73f, 0.58f), false, false);

        var c = new GameObject("Glass Case").transform;
        c.SetParent(exhibit, false);
        float z = StoneBase.z, top = CaseBaseHeight + CaseHeight;

        // The base keeps the desktop visitor out of the case; the glass has no collider so the VR ray reaches the stone
        Box("Base", c, new Vector3(0, CaseBaseHeight / 2f, z), new Vector3(CaseWidth + 0.1f, CaseBaseHeight, CaseDepth + 0.1f), baseMat);
        Box("Pedestal", c, new Vector3(0, CaseBaseHeight + (PedestalHeight - CaseBaseHeight) / 2f, z), new Vector3(1.15f, PedestalHeight - CaseBaseHeight, 0.6f), pedestalMat);

        // Invisible wall around the case for the desktop visitor (switched off in VR mode, where it would block the ray)
        caseBlocker = Box("Case Blocker (desktop only)", c, new Vector3(0, 1.25f, z), new Vector3(CaseWidth + 0.1f, 2.5f, CaseDepth + 0.1f), baseMat);
        Object.DestroyImmediate(caseBlocker.GetComponent<MeshRenderer>());

        void Glass(string name, Vector3 pos, Vector3 size, Material m)
        {
            var g = Box(name, c, pos, size, m);
            Object.DestroyImmediate(g.GetComponent<Collider>());
        }
        float midY = CaseBaseHeight + CaseHeight / 2f;
        Glass("Glass Front", new Vector3(0, midY, z - CaseDepth / 2f), new Vector3(CaseWidth, CaseHeight, 0.012f), glassMat);
        Glass("Glass Back", new Vector3(0, midY, z + CaseDepth / 2f), new Vector3(CaseWidth, CaseHeight, 0.012f), glassMat);
        Glass("Glass Left", new Vector3(-CaseWidth / 2f, midY, z), new Vector3(0.012f, CaseHeight, CaseDepth), glassMat);
        Glass("Glass Right", new Vector3(CaseWidth / 2f, midY, z), new Vector3(0.012f, CaseHeight, CaseDepth), glassMat);
        Glass("Glass Top", new Vector3(0, top, z), new Vector3(CaseWidth, 0.012f, CaseDepth), glassMat);
        foreach (float x in new[] { -CaseWidth / 2f, CaseWidth / 2f })
            foreach (float dz in new[] { -CaseDepth / 2f, CaseDepth / 2f })
                Glass("Glass Edge", new Vector3(x, midY, z + dz), new Vector3(0.02f, CaseHeight, 0.02f), edgeMat);
        foreach (float dz in new[] { -CaseDepth / 2f, CaseDepth / 2f })
            Glass("Glass Edge", new Vector3(0, top, z + dz), new Vector3(CaseWidth, 0.02f, 0.02f), edgeMat);
    }

    // Two information panels inside the case, on stone stands, left and right of the stone (texts written for this project)
    static void BuildInfoPanels(Transform exhibit, Camera cam)
    {
        var standMat = MakeMat("Case_PanelStand", new Color(0.70f, 0.68f, 0.64f), false, false);
        float z = StoneBase.z + 0.35f;
        string[] titles = { "What is the Rosetta Stone?", "The key to Egyptian hieroglyphs" };
        string[] bodies =
        {
            "A broken part of a larger granodiorite stela, carved in 196 BC. It records a decree of the Egyptian priests honouring the young king Ptolemy V.\n\nThe same text is written three times: in hieroglyphs (top), in Demotic (middle) and in ancient Greek (bottom).\n\nFound in 1799 at Rashid (Rosetta), it has been in the British Museum since 1802.",
            "Because the Greek text could still be read, scholars compared it with the two Egyptian scripts.\n\nThomas Young recognised that the oval cartouches contained royal names such as Ptolemy.\n\nIn 1822 Jean-François Champollion showed that hieroglyphs also record sounds, and began to read them."
        };
        for (int i = 0; i < 2; i++)
        {
            float x = (i == 0 ? -1 : 1) * 1.1f;
            Box("Panel Stand", exhibit, new Vector3(x, CaseBaseHeight + 0.45f, z), new Vector3(0.36f, 0.9f, 0.3f), standMat);
            var p = MakeCanvas("Info Panel " + (i + 1), new Vector3(x, 1.62f, z - 0.05f), 0f, new Vector2(520, 760), cam);
            Background(p, new Color(0.97f, 0.97f, 0.95f, 1f));
            var t = Text(p, "Title", "<b>" + titles[i] + "</b>", 30, TextAlignmentOptions.Top, new Vector2(460, 90), new Vector2(0, 300));
            t.color = new Color(0.1f, 0.1f, 0.12f);
            var b = Text(p, "Body", bodies[i], 21, TextAlignmentOptions.TopLeft, new Vector2(440, 580), new Vector2(0, -50));
            b.color = new Color(0.15f, 0.15f, 0.17f);
            p.SetParent(exhibit, true);
        }
    }

    // Light stone slabs with thin joints, generated once and saved as an asset
    static Texture2D TileTexture()
    {
        string path = MatDir + "/BM_FloorTiles.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing != null) return existing;
        const int n = 256;
        var tex = new Texture2D(n, n, TextureFormat.RGB24, true);
        var rnd = new System.Random(4);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                bool joint = x < 2 || y < 2;
                float v = joint ? 0.72f : 0.96f + (float)(rnd.NextDouble() - 0.5) * 0.04f;
                tex.SetPixel(x, y, new Color(v, v, v));
            }
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Repeat;
        AssetDatabase.CreateAsset(tex, path);
        return tex;
    }

    // ================= helpers =================

    // Uses the "P_Podium_01" closest to the visitor (a round platform with a railing): the stone stands in its centre,
    // facing the centre of the showroom, and the visitor starts just outside the railing.
    static bool AnalyzePodium(Transform rig, out Vector3 exhibitPos, out Quaternion exhibitRot, out float platformY)
    {
        exhibitPos = Vector3.zero; exhibitRot = Quaternion.identity; platformY = 0f;
        var podium = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)
            .Where(t => t.name.StartsWith("P_Podium_01") && t.GetComponentsInChildren<Renderer>().Length > 0)
            .OrderBy(t => Vector3.Distance(t.position, rig.position))
            .FirstOrDefault();
        if (podium == null)
        {
            Debug.LogWarning("ROSETTA: no P_Podium_01 found, exhibit left at the origin");
            return false;
        }

        var pb = Bounds(podium.gameObject);
        var c = pb.center;
        float floorY = rig.position.y;

        // Highest point near the centre of the podium = surface of the platform (the railing is further out)
        float top = floorY;
        foreach (var mf in podium.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            var m = mf.transform.localToWorldMatrix;
            foreach (var v in mf.sharedMesh.vertices)
            {
                var p = m.MultiplyPoint3x4(v);
                if (new Vector2(p.x - c.x, p.z - c.z).magnitude < 0.4f) top = Mathf.Max(top, p.y);
            }
        }
        platformY = top - floorY;

        float radius = Mathf.Max(pb.extents.x, pb.extents.z);
        float dist = Mathf.Clamp(radius + 0.4f, 1.4f, 3.5f);
        var dir = new Vector3(c.x, 0f, c.z);
        if (dir.sqrMagnitude < 0.01f) dir = c - rig.position;
        dir.y = 0f;
        dir.Normalize();

        exhibitRot = Quaternion.LookRotation(dir, Vector3.up);
        exhibitPos = new Vector3(c.x, floorY, c.z) - dir * dist;
        StoneBase = new Vector3(0f, Mathf.Max(PedestalHeight, platformY), dist);
        Debug.Log("ROSETTA: podium " + podium.name + " centre " + c + ", radius " + radius + ", platform height " + platformY + ", visitor distance " + dist);
        return true;
    }

    // The showroom's baked lighting is not in the package: light the exhibit like a museum display
    static void AddExhibitLights(Transform exhibit, bool pointLights = true)
    {
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.6f);
        var stoneCenter = Bounds(exhibit.Find("Rosetta Stone").gameObject).center;

        var spot = new GameObject("Exhibit Spotlight").AddComponent<Light>();
        spot.type = LightType.Spot;
        spot.color = new Color(1f, 0.95f, 0.85f);
        spot.intensity = 6f;
        spot.range = 7f;
        spot.spotAngle = 60f;
        spot.transform.SetParent(exhibit, false);
        spot.transform.position = stoneCenter + exhibit.rotation * new Vector3(0f, 1.4f, -1.2f);
        spot.transform.LookAt(stoneCenter);

        if (!pointLights) return; // the gallery has its own daylight

        var fill = new GameObject("Exhibit Fill Light").AddComponent<Light>();
        fill.type = LightType.Point;
        fill.color = new Color(0.9f, 0.92f, 1f);
        fill.intensity = 4f;
        fill.range = 10f;
        fill.transform.SetParent(exhibit, false);
        fill.transform.localPosition = new Vector3(0f, 2.6f, 0.6f);

        // Soft light over the visitor, so the panels and the room around are readable too
        var room = new GameObject("Room Light").AddComponent<Light>();
        room.type = LightType.Point;
        room.color = Color.white;
        room.intensity = 3f;
        room.range = 12f;
        room.transform.SetParent(exhibit, false);
        room.transform.localPosition = new Vector3(0f, 2.8f, -1f);
    }

    static GameObject FindModel()
    {
        if (!AssetDatabase.IsValidFolder(ModelDir)) return null;
        return AssetDatabase.FindAssets("t:GameObject", new[] { ModelDir })
            .Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g)))
            .FirstOrDefault(go => go != null && go.GetComponentsInChildren<Renderer>().Length > 0);
    }

    static Bounds Bounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    static Material MakeMat(string name, Color c, bool transparent, bool unlit)
    {
        string path = MatDir + "/" + name + ".mat";
        var m = new Material(Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", c);
        if (transparent)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
        }
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 size, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, true);
        go.transform.position = pos;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    static void AddTextLines(Transform stone, float w, float h, float d, Material mat)
    {
        var lines = new GameObject("Inscription Lines").transform;
        lines.SetParent(stone, false);
        float z = StoneBase.z - d / 2f - 0.002f;
        void Band(float top, float bottom, int count, float width)
        {
            for (int i = 0; i < count; i++)
            {
                float f = Mathf.Lerp(top, bottom, (i + 0.5f) / count);
                var l = Box("Line", lines, new Vector3(0, StoneBase.y + h * (1f - f), z), new Vector3(w * width, 0.006f, 0.002f), mat);
                Object.DestroyImmediate(l.GetComponent<Collider>());
            }
        }
        Band(0.05f, 0.26f, 7, 0.55f);  // hieroglyphic: short, broken lines
        Band(0.29f, 0.59f, 12, 0.85f); // demotic
        Band(0.62f, 0.96f, 16, 0.85f); // Greek
    }

    static void MakeZone(Transform parent, string name, int passage, TextZone.Script script, float top, float bottom,
        float w, float h, float frontZ, Material normal, Material hover, Material highlight)
    {
        float yTop = StoneBase.y + h * (1f - top);
        float yBottom = StoneBase.y + h * (1f - bottom);
        var go = Box(name, parent, new Vector3(StoneBase.x, (yTop + yBottom) / 2f, frontZ), new Vector3(w * 0.92f, yTop - yBottom, 0.01f), normal);

        var zone = go.AddComponent<TextZone>();
        zone.passageID = passage;
        zone.script = script;
        zone.normalMaterial = normal;
        zone.hoverMaterial = hover;
        zone.highlightMaterial = highlight;

        // Thin golden frame around the zone (4 bars), hidden until hover or selection
        var outline = new GameObject("Outline").transform;
        outline.SetParent(go.transform.parent, false);
        float zw = w * 0.92f, zh = yTop - yBottom, t = 0.006f, cy = (yTop + yBottom) / 2f, z = frontZ - 0.006f;
        var bars = new[]
        {
            (new Vector3(StoneBase.x, yTop, z), new Vector3(zw, t, t)),
            (new Vector3(StoneBase.x, yBottom, z), new Vector3(zw, t, t)),
            (new Vector3(StoneBase.x - zw / 2f, cy, z), new Vector3(t, zh, t)),
            (new Vector3(StoneBase.x + zw / 2f, cy, z), new Vector3(t, zh, t)),
        };
        foreach (var (pos, size) in bars)
        {
            var bar = Box("Bar", outline, pos, size, zoneOutline);
            Object.DestroyImmediate(bar.GetComponent<Collider>());
        }
        outline.SetParent(go.transform, true);
        outline.gameObject.SetActive(false);
        zone.outline = outline.gameObject;

        var interactable = go.AddComponent<XRSimpleInteractable>();
        UnityEventTools.AddVoidPersistentListener(interactable.selectEntered, zone.OnSelected);
        UnityEventTools.AddVoidPersistentListener(interactable.hoverEntered, zone.OnHoverEnter);
        UnityEventTools.AddVoidPersistentListener(interactable.hoverExited, zone.OnHoverExit);
    }

    static RectTransform MakeCanvas(string name, Vector3 pos, float yaw, Vector2 sizePx, Camera cam)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = cam;
        go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
        go.AddComponent<TrackedDeviceGraphicRaycaster>();
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = sizePx;
        rt.localScale = Vector3.one * 0.001f; // 1000 px = 1 m
        rt.position = pos;
        rt.rotation = Quaternion.Euler(0, yaw, 0);
        return rt;
    }

    static Material zoneOutline;
    const string StatueDir = Root + "/Statues";
    static GameObject caseBlocker;

    static void Background(RectTransform canvas, Color c)
    {
        var bg = new GameObject("Background", typeof(RectTransform)).AddComponent<Image>();
        bg.transform.SetParent(canvas, false);
        bg.color = c;
        // Rounded corners from Unity's built-in UI sprite
        bg.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        bg.type = Image.Type.Sliced;
        bg.pixelsPerUnitMultiplier = 0.3f;
        var rt = bg.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    static TMP_Text Text(RectTransform parent, string name, string text, float size, TextAlignmentOptions align, Vector2 sizePx, Vector2 pos)
    {
        var t = new GameObject(name, typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        t.transform.SetParent(parent, false);
        t.text = text;
        t.fontSize = size;
        t.alignment = align;
        t.color = Color.white;
        t.textWrappingMode = TextWrappingModes.Normal;
        t.rectTransform.sizeDelta = sizePx;
        t.rectTransform.anchoredPosition = pos;
        return t;
    }

    static Button MakeButton(RectTransform parent, string label, Vector2 pos, Vector2 size, float fontSize)
    {
        var go = TMP_DefaultControls.CreateButton(new TMP_DefaultControls.Resources());
        go.name = "Button " + label;
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        var img = go.GetComponent<Image>();
        img.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        img.type = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = 0.5f;
        img.color = new Color(0.95f, 0.76f, 0.3f, 0.92f);
        var t = go.GetComponentInChildren<TMP_Text>();
        t.text = label;
        t.fontSize = fontSize;
        t.color = new Color(0.1f, 0.08f, 0.05f);

        // Collider so the desktop visitor can click it with the crosshair (DesktopPlayer)
        var col = go.AddComponent<BoxCollider>();
        col.size = new Vector3(size.x, size.y, 10f);
        return go.GetComponent<Button>();
    }

    static void MakeLanguageButton(RectTransform menu, string label, Vector2 pos, LanguageManager lm, int index)
    {
        var b = MakeButton(menu, label, pos, new Vector2(200, 90), 32);
        UnityEventTools.AddIntPersistentListener(b.onClick, lm.SetLanguage, index);
    }

    static void FillTexts(TranslationManager tm)
    {
        // Order of the languages: Italiano, English, Español, Français, Deutsch (same as the menu buttons)
        tm.chooseText = "Scegli una lingua\nChoose a language";
        tm.introTexts = new[]
        {
            "Tocca una zona dell'iscrizione per leggerla nella tua lingua.\n\nLe zone evidenziate mostrano lo stesso passaggio nelle altre scritture.",
            "Touch an area of the inscription to read it in your language.\n\nThe highlighted areas show the same passage in the other scripts.",
            "Toca una zona de la inscripción para leerla en tu idioma.\n\nLas zonas resaltadas muestran el mismo pasaje en las otras escrituras.",
            "Touche une zone de l'inscription pour la lire dans ta langue.\n\nLes zones mises en évidence montrent le même passage dans les autres écritures.",
            "Berühre einen Bereich der Inschrift, um ihn in deiner Sprache zu lesen.\n\nDie hervorgehobenen Bereiche zeigen dieselbe Passage in den anderen Schriften."
        };
        tm.passages = new[]
        {
            new TranslationManager.Passage
            {
                titles = new[]
                {
                    "Onori per il re Tolomeo V",
                    "Honours for King Ptolemy V",
                    "Honores para el rey Ptolomeo V",
                    "Honneurs pour le roi Ptolémée V",
                    "Ehren für König Ptolemaios V."
                },
                translations = new[]
                {
                    "I sacerdoti elencano le buone azioni del giovane re: ha fatto doni ai templi, cancellato debiti fiscali e ridotto le tasse, liberato prigionieri e sconfitto i ribelli a Licopoli. In cambio decidono di onorarlo con una sua statua in ogni tempio e con feste ogni anno.",
                    "The priests list the good deeds of the young king: he gave gifts to the temples, cancelled tax debts and reduced taxes, freed prisoners and defeated the rebels at Lycopolis. In return they decide to honour him with a statue in every temple and with yearly festivals.",
                    "Los sacerdotes enumeran las buenas obras del joven rey: hizo donaciones a los templos, perdonó deudas fiscales y redujo los impuestos, liberó a prisioneros y venció a los rebeldes en Licópolis. A cambio deciden honrarlo con una estatua en cada templo y con fiestas cada año.",
                    "Les prêtres énumèrent les bienfaits du jeune roi : il a fait des dons aux temples, annulé des dettes fiscales et réduit les impôts, libéré des prisonniers et vaincu les rebelles à Lycopolis. En échange, ils décident de l'honorer d'une statue dans chaque temple et de fêtes chaque année.",
                    "Die Priester zählen die guten Taten des jungen Königs auf: Er beschenkte die Tempel, erließ Steuerschulden und senkte die Steuern, ließ Gefangene frei und besiegte die Aufständischen bei Lykopolis. Dafür beschließen sie, ihn mit einer Statue in jedem Tempel und mit jährlichen Festen zu ehren."
                },
                scriptNotes = new[]
                {
                    "Conservato in demotico e in greco. La versione in geroglifico di questo passaggio è perduta: la parte alta della stele è spezzata.",
                    "Preserved in Demotic and Greek. The hieroglyphic version of this passage is lost: the top of the stone is broken.",
                    "Conservado en demótico y en griego. La versión jeroglífica de este pasaje se ha perdido: la parte superior de la piedra está rota.",
                    "Conservé en démotique et en grec. La version hiéroglyphique de ce passage est perdue : le haut de la pierre est brisé.",
                    "In Demotisch und Griechisch erhalten. Die hieroglyphische Fassung dieser Passage ist verloren: Der obere Teil des Steins ist abgebrochen."
                }
            },
            new TranslationManager.Passage
            {
                titles = new[]
                {
                    "Scriverlo in tre scritture",
                    "Write it in three scripts",
                    "Escribirlo en tres escrituras",
                    "L'écrire en trois écritures",
                    "In drei Schriften schreiben"
                },
                translations = new[]
                {
                    "Il decreto termina ordinando di inciderlo su una stele di pietra dura in scrittura sacra (geroglifico), nativa (demotico) e greca, e di collocarla nei templi di primo, secondo e terzo rango, accanto alla statua del re.",
                    "The decree ends by ordering that it be carved on a stela of hard stone in sacred (hieroglyphic), native (demotic) and Greek writing, and set up in the temples of the first, second and third rank, next to the statue of the king.",
                    "El decreto termina ordenando grabarlo en una estela de piedra dura en escritura sagrada (jeroglífica), nativa (demótica) y griega, y colocarla en los templos de primer, segundo y tercer rango, junto a la estatua del rey.",
                    "Le décret se termine en ordonnant de le graver sur une stèle de pierre dure en écriture sacrée (hiéroglyphique), indigène (démotique) et grecque, et de la placer dans les temples de premier, deuxième et troisième rang, à côté de la statue du roi.",
                    "Das Dekret endet mit dem Befehl, es auf eine Stele aus hartem Stein in heiliger (hieroglyphischer), einheimischer (demotischer) und griechischer Schrift zu meißeln und sie in den Tempeln ersten, zweiten und dritten Ranges neben der Statue des Königs aufzustellen."
                },
                scriptNotes = new[]
                {
                    "Questo passaggio è conservato in tutte e tre le scritture. Confrontandole, Champollion decifrò i geroglifici (1822).",
                    "This passage survives in all three scripts. By comparing them, Champollion deciphered hieroglyphs (1822).",
                    "Este pasaje se conserva en las tres escrituras. Comparándolas, Champollion descifró los jeroglíficos (1822).",
                    "Ce passage est conservé dans les trois écritures. En les comparant, Champollion a déchiffré les hiéroglyphes (1822).",
                    "Diese Passage ist in allen drei Schriften erhalten. Durch ihren Vergleich entzifferte Champollion die Hieroglyphen (1822)."
                }
            }
        };
    }

    // Renders two preview images into <project>/Snapshots to check the layout without opening the editor.
    static void Snapshots(Transform exhibit, GameObject menu, TMP_Text panelText, TranslationManager tm, string scenePath)
    {
        string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Snapshots");
        Directory.CreateDirectory(dir);
        var camGo = new GameObject("SnapshotCamera");
        var cam = camGo.AddComponent<Camera>();
        var target = exhibit.TransformPoint(new Vector3(0.25f, 0f, 1.6f));
        target.y = Bounds(exhibit.Find("Rosetta Stone").gameObject).center.y - 0.1f;
        cam.transform.position = exhibit.TransformPoint(new Vector3(0.15f, 1.6f, -0.4f));
        cam.transform.LookAt(target);
        cam.fieldOfView = 60f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;

        Shot(cam, Path.Combine(dir, "1_start_menu.png"));

        var camPos = cam.transform.position;
        var camRot = cam.transform.rotation;
        cam.transform.position = exhibit.TransformPoint(new Vector3(1.8f, 2.3f, -1.5f));
        cam.transform.LookAt(target);
        Shot(cam, Path.Combine(dir, "0_overview.png"));
        cam.transform.SetPositionAndRotation(camPos, camRot);

        menu.SetActive(false);
        var zones = Object.FindObjectsByType<TextZone>(FindObjectsSortMode.None);
        foreach (var z in zones)
        {
            z.GetComponent<Renderer>().sharedMaterial = z.passageID == 1 ? z.highlightMaterial : z.normalMaterial;
            if (z.outline != null) z.outline.SetActive(z.passageID == 1);
        }
        var p = tm.passages[1];
        panelText.text = "<color=#F2C14E><b>" + p.titles[1] + "</b></color>\n\n" + p.translations[1] + "\n\n<i><size=85%>" + p.scriptNotes[1] + "</size></i>";
        Shot(cam, Path.Combine(dir, "2_passage_selected.png"));

        // The visitor's first view: the front of the museum
        if (GameObject.Find("British Museum - Exterior") != null)
        {
            cam.transform.position = ExteriorStart + new Vector3(0f, 1.6f, 0f);
            cam.transform.rotation = Quaternion.Euler(-8f, 0f, 0f);
            cam.clearFlags = CameraClearFlags.Skybox;
            Shot(cam, Path.Combine(dir, "3_exterior.png"));
        }

        Object.DestroyImmediate(camGo);
        EditorSceneManager.OpenScene(scenePath); // discard the preview changes
    }

    static void Shot(Camera cam, string file)
    {
        var rtex = new RenderTexture(1280, 720, 24);
        cam.targetTexture = rtex;
        cam.Render();
        RenderTexture.active = rtex;
        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        tex.Apply();
        File.WriteAllBytes(file, tex.EncodeToPNG());
        RenderTexture.active = null;
        cam.targetTexture = null;
        Object.DestroyImmediate(rtex);
        Object.DestroyImmediate(tex);
    }
}
