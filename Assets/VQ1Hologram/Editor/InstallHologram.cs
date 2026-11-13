using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.TextCore.LowLevel;

namespace VQ1Hologram.Editor
{
    public static class InstallHologram
    {
        const string Folder="Assets/VQ1Hologram/Generated";
        const string ScenePath=Folder+"/NEARBY_Hologram.unity";
        [MenuItem("Tools/VQ1 Hologram/Create experience in template")]
        public static void Install()
        {
            const string source="Assets/Hololuminescent/Character.unity";
            if(!File.Exists(source))throw new System.InvalidOperationException("Import this add-on into airi-utcn/Hologram-Screen. Original Character.unity was not found.");
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            if(File.Exists(ScenePath)&&!EditorUtility.DisplayDialog("Regenerate VQ1 scene?","This replaces only the generated VQ1 scene, materials and font. The original template scene stays intact.","Regenerate","Cancel"))return;
            Directory.CreateDirectory(Folder);AssetDatabase.Refresh();
            var scene=EditorSceneManager.OpenScene(source,OpenSceneMode.Single);
            EditorSceneManager.SaveScene(scene,ScenePath);
            foreach(var root in scene.GetRootGameObjects())
                if(root.name=="Character")Object.DestroyImmediate(root);
            foreach(var bootstrap in Object.FindObjectsByType<MonitorSelectionBootstrap>(FindObjectsSortMode.None))
            {
                var settings=new SerializedObject(bootstrap);
                settings.FindProperty("rememberSelection").boolValue=true;
                settings.FindProperty("skipIfSingleMonitor").boolValue=true;
                settings.ApplyModifiedPropertiesWithoutUndo();
            }
            var cam=Object.FindAnyObjectByType<Camera>();
            if(!cam)throw new System.InvalidOperationException("Template camera not found. Restore the Alcove prefab.");
            cam.tag="MainCamera";
            // Keep the alcove camera transform, FOV, mirror and clipping calibration.
            var obj=new GameObject("NEARBY • VQ1 + VN3");
            var show=obj.AddComponent<HologramExperience>();
            show.cyan=MakeMaterial("Ion Cyan",new Color(.08f,.65f,1),1.4f);
            show.amber=MakeMaterial("Solar Amber",new Color(1,.36f,.05f),1.4f);
            show.ice=MakeMaterial("Ice",new Color(.65f,.85f,1),.4f);
            show.dim=MakeMaterial("Faint Cyan",new Color(.015f,.09f,.13f),.1f);
            show.rockSurface=MakeMaterial("Asteroid Surface",new Color(.025f,.07f,.10f),.1f);
            show.font=MakeFont();
            // Local post-process settings: no writes to the source template volume profiles.
            var volumeObj=new GameObject("VQ1 • Display grade");var volume=volumeObj.AddComponent<Volume>();
            volume.isGlobal=true;volume.priority=100;
            var profile=new VolumeProfile();profile.name="VQ1 Display Grade";
            ConfigureProfile(profile);
            SaveAsset(profile,Folder+"/DisplayGrade.asset");
            foreach(var component in profile.components)AssetDatabase.AddObjectToAsset(component,profile);
            ConfigureCamera(cam);
            volume.sharedProfile=profile;
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            // Preserve existing build scenes; prepend this exhibit as launch scene.
            var builds=new System.Collections.Generic.List<EditorBuildSettingsScene>{new EditorBuildSettingsScene(ScenePath,true)};
            foreach(var entry in EditorBuildSettings.scenes)if(entry.path!=ScenePath)builds.Add(entry);
            EditorBuildSettings.scenes=builds.ToArray();AssetDatabase.SaveAssets();
            Selection.activeGameObject=obj;
            Debug.Log("VQ1 ready. Press Play. Automatic 80-second loop, H for operator controls. Keep original alcove alignment; verify on physical hardware.");
        }
        [MenuItem("Tools/VQ1 Hologram/Repair white screen in open scene")]
        public static void RepairWhiteScreen()
        {
            if(EditorApplication.isPlaying)
            {Debug.LogError("Stop Play before repairing the scene.");return;}
            var show=Object.FindAnyObjectByType<HologramExperience>();
            if(!show){Debug.LogError("Open Generated/VQ1_Hologram.unity first.");return;}
            var cam=Camera.main;
            if(!cam)cam=Object.FindAnyObjectByType<Camera>();
            if(!cam){Debug.LogError("No camera found in the open scene.");return;}
            ConfigureCamera(cam);
            Volume target=null;
            foreach(var v in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
                if(v.gameObject.name=="VQ1 • Display grade"){target=v;break;}
            if(!target){target=new GameObject("VQ1 • Display grade").AddComponent<Volume>();}
            if(!target.sharedProfile)
            {
                Directory.CreateDirectory(Folder);AssetDatabase.Refresh();
                var created=new VolumeProfile();created.name="VQ1 Display Grade";
                SaveAsset(created,Folder+"/DisplayGrade.asset");target.sharedProfile=created;
            }
            var profile=target.sharedProfile;
            ConfigureProfile(profile);
            foreach(var component in profile.components)
                if(!AssetDatabase.Contains(component))AssetDatabase.AddObjectToAsset(component,profile);
            target.isGlobal=true;target.weight=1;target.priority=100;
            EditorUtility.SetDirty(profile);EditorUtility.SetDirty(target);
            EditorSceneManager.MarkSceneDirty(show.gameObject.scene);
            EditorSceneManager.SaveScene(show.gameObject.scene);AssetDatabase.SaveAssets();
            Debug.Log("VQ1 display repaired: black camera clear, sky disabled, fixed EV12, black outside mask. Press Play to verify.");
        }
        static void ConfigureCamera(Camera cam)
        {
            cam.tag="MainCamera";cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=Color.black;
            var hd=cam.GetComponent<HDAdditionalCameraData>();
            if(hd){hd.clearColorMode=HDAdditionalCameraData.ClearColorMode.Color;hd.backgroundColorHDR=Color.black;EditorUtility.SetDirty(hd);}
            EditorUtility.SetDirty(cam);
        }
        static T GetOrAdd<T>(VolumeProfile profile) where T:VolumeComponent
        {return profile.TryGet<T>(out var component)?component:profile.Add<T>(false);}
        static void ConfigureProfile(VolumeProfile profile)
        {
            var bloom=GetOrAdd<Bloom>(profile);bloom.intensity.Override(.17f);bloom.threshold.Override(1.1f);
            // The source template has HDRI exposure +14.6 and powerful studio lights.
            // A fixed EV0 override washes out the sky and alcove. Emissive hologram
            // materials already have exposure weight zero, so retain their visibility.
            var exposure=GetOrAdd<Exposure>(profile);exposure.mode.Override(ExposureMode.Fixed);
            exposure.fixedExposure.Override(12);exposure.compensation.Override(0);
            var environment=GetOrAdd<VisualEnvironment>(profile);environment.skyType.Override(0);
            var fog=GetOrAdd<Fog>(profile);fog.enabled.Override(false);
            var tonemap=GetOrAdd<Tonemapping>(profile);tonemap.mode.Override(TonemappingMode.ACES);
            // Keep the template mask's enable state and dimensions, but avoid its
            // default white fill if the installation enables clipping.
            var mask=GetOrAdd<global::SDFCubeMaskVolume>(profile);mask.outsideColor.Override(Color.black);
        }
        static Material MakeMaterial(string name,Color color,float emission)
        {
            var shader=Shader.Find("HDRP/Unlit");
            if(!shader)throw new System.InvalidOperationException("HDRP/Unlit missing. Let the template finish importing HDRP 17.6 first.");
            var mat=new Material(shader){name=name};mat.SetColor("_UnlitColor",color);
            mat.SetColor("_EmissiveColor",color*emission);mat.SetFloat("_EmissiveExposureWeight",0);
            UnityEngine.Rendering.HighDefinition.HDMaterial.ValidateMaterial(mat);
            return SaveAsset(mat,Folder+"/"+name+".mat");
        }
        static TMP_FontAsset MakeFont()
        {
            var source=AssetDatabase.LoadAssetAtPath<Font>("Assets/TextMesh Pro/Fonts/LiberationSans.ttf");
            if(!source)throw new System.InvalidOperationException("Template LiberationSans.ttf missing. Restore the template assets.");
            var asset=TMP_FontAsset.CreateFontAsset(source,64,7,GlyphRenderMode.SDFAA,1024,1024,AtlasPopulationMode.Dynamic,true);
            asset.name="VQ1 Romanian";
            SaveAsset(asset,Folder+"/RomanianFont.asset");
            AssetDatabase.AddObjectToAsset(asset.material,asset);
            foreach(var atlas in asset.atlasTextures)if(atlas)AssetDatabase.AddObjectToAsset(atlas,asset);
            // Explicitly populate all text, including Romanian diacritics, before build.
            asset.TryAddCharacters("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789ĂÂÎȘȚăâîșț°•–/.,:+-() ");
            EditorUtility.SetDirty(asset);return asset;
        }
        static T SaveAsset<T>(T asset,string path) where T:Object
        {
            if(AssetDatabase.LoadMainAssetAtPath(path))AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(asset,path);return asset;
        }
    }
}
