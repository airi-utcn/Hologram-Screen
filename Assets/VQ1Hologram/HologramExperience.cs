using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace VQ1Hologram
{
    public sealed class HologramExperience : MonoBehaviour
    {
        public Material cyan, amber, ice, dim, rockSurface;
        public TMP_FontAsset font;
        bool useVN3;
        KeplerOrbit vq1Orbit,vn3Orbit;
        LineRenderer asteroidOrbitLine,earthOrbitLine;
        BodyLabel asteroidLabel;
        int ViewIndex => chapter%2;
        string AsteroidName => useVN3?"2018 VN3":"2018 VQ1";
        float displayScale;
        readonly List<BodyLabel> bodyLabels=new List<BodyLabel>();
        sealed class BodyLabel { public Transform target; public TextMeshPro text; public LineRenderer leader; }
        [Min(10)] public float chapterSeconds=20;
        public bool automatic=true;
        [Range(0,40)] public float daysPerSecond=13;
        [Tooltip("World units per AU. Keep under 1.0 for the template clipping box.")]
        [Range(.5f,1.0f)] public float unitsPerAU=.88f;
        KeplerOrbit orbit,earth;
        Transform solar, asteroid, earthBody, pedestal;
        Transform[] chapterObjects;
        LineRenderer trail, progress;
        TextMeshPro title,kicker,fact,detail,footer,clock,discovery;
        public const string ProjectUrl="https://cgis.utcluj.ro";
        readonly string[] teamPages={
            "ECHIPA DESCOPERIRII • 1/2\nM. Popescu • O. Vaduvescu\nT. Wilson • T. Davison • D. Gorgan\nV. Bacu • T. Stefanut • A. Sabou",
            "ECHIPA DESCOPERIRII • 2/2\nD. Copandean Balazs • C. Nandra\nC. Boldea • A. Boldea • M. Predatu\nV. Pinter • A. Stanica"};
        readonly string[] vn3TeamPages={
            "2018 VN3 • ECHIPA DESCOPERIRII • 1/2\nC. Boldea • O. Vaduvescu\nT. Davison • L. Hudin • D. Gorgan\nV. Bacu • T. Stefanut • A. Sabou",
            "2018 VN3 • ECHIPA DESCOPERIRII • 2/2\nD. Copandean Balazs • C. Nandra\nA. Boldea • M. Predatu\nV. Pinter • A. Stanica"};
        Camera displayCamera;
        double jd;
        float elapsed, chapterTime, speed=1, yaw;
        int chapter;
        bool paused,help,spin=true;
        Vector3 cameraBase;
        float cameraFov,offset,zoom;
        readonly List<Material> ownedMaterials=new List<Material>();
        readonly string[] titles={"UN AN DE 413 ZILE","DINCOLO DE UN PLAN"};
        readonly string[] kickers={"ACELAȘI SOARE. ALT RITM.","SCHIMBĂ UNGHIUL. DESCOPERĂ DIFERENȚA."};
        readonly string[] facts={"DOUĂ ORBITE. UN DANS COSMIC.","11° CARE SCHIMBĂ PERSPECTIVA"};
        readonly string[] descriptions={
            "Portocaliu: asteroidul. Albastru: Pământul.\nFiecare își urmează drumul în jurul Soarelui.\nUrmărește cum li se schimbă pozițiile.",
            "Văzute de sus, orbitele par apropiate.\nDin lateral, diferența prinde contur.\nOrbita lui 2018 VQ1 este înclinată."};

        void Start()
        {
            if(!cyan || !amber || !ice || !dim || !rockSurface || !font)
            {Debug.LogError("VQ1: run Tools > VQ1 Hologram > Create experience in the template first.");enabled=false;return;}
            vq1Orbit=JsonUtility.FromJson<KeplerOrbit>(Resources.Load<TextAsset>("VQ1_Orbit").text);
            vn3Orbit=JsonUtility.FromJson<KeplerOrbit>(Resources.Load<TextAsset>("VN3_Orbit").text);
            orbit=vq1Orbit;
            earth=new KeplerOrbit{epoch=2451545,a=1.00000261,e=.01671123,i=-.00001531,om=0,w=102.93768193,ma=357.52688973,n=.9856076686};
            displayScale=Mathf.Min(unitsPerAU,1.04f/(float)(orbit.a*(1+orbit.e)));
            jd=orbit.epoch;
            displayCamera=Camera.main;
            if(!displayCamera)displayCamera=FindAnyObjectByType<Camera>();
            if(displayCamera)
            {
                cameraBase=displayCamera.transform.position;cameraFov=displayCamera.fieldOfView;
                offset=PlayerPrefs.GetFloat("VQ1_AlignY",0);zoom=PlayerPrefs.GetFloat("VQ1_AlignZoom",0);Align();
            }
            // All content remains inside the template's 2.768 x 6 x 5.891 clipping region.
            title=Text("Title",new Vector3(0,4.85f,-4.8f),2.45f,.38f,ice);
            kicker=Text("Kicker",new Vector3(0,4.10f,-4.8f),2.4f,.20f,cyan);
            fact=Text("Fact",new Vector3(0,1.80f,-4.8f),2.4f,.27f,ice);
            detail=Text("Explanation",new Vector3(0,1.27f,-4.8f),2.4f,.64f,ice);
            clock=Text("Simulation time",new Vector3(0,.80f,-4.8f),2.3f,.17f,cyan);
            footer=Text("Source",new Vector3(0,.44f,-4.8f),2.45f,.16f,ice);
            footer.text="NASA / JPL • Pământ aproximativ • Corpuri mărite";
            var link=Text("NEARBY project website",new Vector3(0,.62f,-4.8f),2.4f,.17f,cyan);link.text=ProjectUrl;
            discovery=Text("Discovery credit",new Vector3(0,2.03f,-4.8f),2.45f,.18f,cyan);
            discovery.text="ASTEROID DESCOPERIT ÎN PROIECTUL NEARBY";
            AddProjectLogo();
            progress=Line("Chapter progress",transform,dim,4,.006f,false);
            progress.SetPositions(new[]{new Vector3(-1.1f,.4f,-4.8f),new Vector3(1.1f,.4f,-4.8f),new Vector3(1.1f,.42f,-4.8f),new Vector3(-1.1f,.42f,-4.8f)});
            progress.loop=true;
            solar=Group("Orbital theatre",new Vector3(0,3.03f,-3.8f));
            asteroidOrbitLine=OrbitLine("Asteroid orbit",orbit,solar,amber);
            earthOrbitLine=OrbitLine("Earth orbit (approx.)",earth,solar,cyan);
            Ring("Ecliptic guide",solar,1.02f,dim,.003f);
            var sun=Body("Sun",solar,Vector3.zero,.12f,amber);
            AddBodyLabel(sun,"Soare",ice);
            earthBody=Body("Earth (approx.)",solar,Vector3.zero,.065f,cyan);
            // Graphic meridians reinforce that this is a reference globe.
            var globe=Group("Earth meridian",Vector3.zero,earthBody);globe.localRotation=Quaternion.Euler(90,0,0);Ring("Meridian",globe,.55f,ice,.025f);
            asteroid=Body(AsteroidName,solar,Vector3.zero,.065f,amber);
            AddBodyLabel(earthBody,"Pământ",cyan);
            asteroidLabel=AddBodyLabel(asteroid,"Asteroid\n"+AsteroidName,amber);
            asteroidLabel.text.rectTransform.sizeDelta=new Vector2(.8f,.24f);
            trail=Line("Recent orbit arc",solar,amber,90,.010f,false);
            pedestal=Group("Floating reference rings",new Vector3(0,2.25f,-3.8f));
            Ring("Base",pedestal,.55f,dim,.004f);Ring("Base detail",pedestal,.43f,cyan,.003f);
            chapterObjects=new[]{solar};
            SetChapter(0);
        }

        BodyLabel AddBodyLabel(Transform body,string label,Material color)
        {
            var text=Text(label+" label",Vector3.zero,.70f,.15f,color);text.text=label;
            var leader=Line(label+" leader",transform,color,2,.002f,false);
            leader.useWorldSpace=true;
            var entry=new BodyLabel{target=body,text=text,leader=leader};
            bodyLabels.Add(entry);return entry;
        }
        void LateUpdate()
        {
            if(!displayCamera || solar==null)return;
            var placed=new List<Vector3>();
            foreach(var label in bodyLabels)
            {
                var viewport=displayCamera.WorldToViewportPoint(label.target.position);
                bool visible=viewport.z>0 && solar.localScale.x>.2f;
                label.text.gameObject.SetActive(visible);label.leader.gameObject.SetActive(visible);
                if(!visible)continue;
                float depth=Vector3.Dot(transform.TransformPoint(new Vector3(0,0,-4.7f))-displayCamera.transform.position,displayCamera.transform.forward);
                viewport.z=depth;
                var p=transform.InverseTransformPoint(displayCamera.ViewportToWorldPoint(viewport));
                p.x=Mathf.Clamp(p.x+.16f,-.88f,.88f);p.y=Mathf.Clamp(p.y+.15f,2.30f,3.83f);p.z=-4.7f;
                for(int n=0;n<6;n++)
                {
                    bool overlap=false;
                    foreach(var other in placed)if(Mathf.Abs(p.x-other.x)<.70f && Mathf.Abs(p.y-other.y)<.17f){overlap=true;break;}
                    if(!overlap)break;
                    p.y+=.18f;if(p.y>3.83f){p.y=2.30f;p.x=Mathf.Clamp(p.x-.3f,-.88f,.88f);}
                }
                placed.Add(p);label.text.transform.localPosition=p;
                label.text.transform.rotation=displayCamera.transform.rotation;
                label.leader.SetPosition(0,label.target.position);
                label.leader.SetPosition(1,label.text.transform.position-Vector3.up*.07f);
            }
        }
        void AddProjectLogo()
        {
            var logo=Resources.Load<Texture2D>("NEARBY_Logo");
            if(!logo){Debug.LogWarning("NEARBY logo texture missing.");return;}
            var obj=GameObject.CreatePrimitive(PrimitiveType.Quad);obj.name="Official NEARBY logo";
            obj.transform.SetParent(transform,false);obj.transform.localPosition=new Vector3(0,4.45f,-4.8f);
            obj.transform.localScale=new Vector3(.95f,.95f*logo.height/logo.width,1);
            Destroy(obj.GetComponent<Collider>());
            var mat=new Material(ice);ownedMaterials.Add(mat);
            mat.SetTexture("_UnlitColorMap",logo);mat.SetColor("_UnlitColor",Color.white);
            mat.SetTexture("_EmissiveColorMap",logo);mat.SetColor("_EmissiveColor",Color.white*1.5f);
            mat.EnableKeyword("_EMISSIVE_COLOR_MAP");
            mat.SetFloat("_AlphaCutoffEnable",1);mat.SetFloat("_AlphaCutoff",.1f);mat.EnableKeyword("_ALPHATEST_ON");
            obj.GetComponent<Renderer>().sharedMaterial=mat;
        }
        Transform Group(string name,Vector3 local,Transform parent=null)
        {var g=new GameObject(name).transform;g.SetParent(parent?parent:transform,false);g.localPosition=local;return g;}
        Transform Body(string name,Transform parent,Vector3 local,float diameter,Material mat)
        {
            var obj=GameObject.CreatePrimitive(PrimitiveType.Sphere);obj.name=name;obj.transform.SetParent(parent,false);
            obj.transform.localPosition=local;obj.transform.localScale=Vector3.one*diameter;
            Destroy(obj.GetComponent<Collider>());obj.GetComponent<Renderer>().sharedMaterial=mat;return obj.transform;
        }
        LineRenderer Line(string name,Transform parent,Material mat,int count,float width,bool loop)
        {
            var obj=Group(name,Vector3.zero,parent);var line=obj.gameObject.AddComponent<LineRenderer>();
            line.useWorldSpace=false;line.sharedMaterial=mat;line.positionCount=count;line.widthMultiplier=width;line.loop=loop;
            line.shadowCastingMode=ShadowCastingMode.Off;line.receiveShadows=false;return line;
        }
        void Ring(string name,Transform parent,float radius,Material mat,float width)
        {
            var line=Line(name,parent,mat,128,width,true);
            for(int k=0;k<128;k++){float angle=k*Mathf.PI*2/128;line.SetPosition(k,new Vector3(Mathf.Cos(angle)*radius,0,Mathf.Sin(angle)*radius));}
        }
        Vector3 Position(KeplerOrbit el,double date)
        {el.Position(date,out double x,out double y,out double z);return new Vector3((float)x,(float)z,(float)y)*displayScale;}
        LineRenderer OrbitLine(string name,KeplerOrbit el,Transform parent,Material mat)
        {
            var line=Line(name,parent,mat,512,.005f,true);
            for(int k=0;k<512;k++)line.SetPosition(k,Position(el,el.epoch+k*el.Period/512));
            return line;
        }
        TextMeshPro Text(string name,Vector3 position,float width,float height,Material mat)
        {
            var obj=Group(name,position);var text=obj.gameObject.AddComponent<TextMeshPro>();
            text.font=font;text.enableAutoSizing=true;text.fontSizeMin=.1f;text.fontSizeMax=5;
            text.rectTransform.sizeDelta=new Vector2(width,height);text.alignment=TextAlignmentOptions.Center;
            text.textWrappingMode=TextWrappingModes.NoWrap;text.overflowMode=TextOverflowModes.Overflow;
            var textMat=new Material(font.material);ownedMaterials.Add(textMat);
            textMat.SetColor("_FaceColor",mat==cyan?new Color(.2f,.85f,1):new Color(.8f,.94f,1));
            text.fontSharedMaterial=textMat;return text;
        }
        void SetChapter(int index)
        {
            chapter=((index%4)+4)%4;chapterTime=0;
            useVN3=chapter>=2;orbit=useVN3?vn3Orbit:vq1Orbit;
            displayScale=Mathf.Min(unitsPerAU,1.04f/(float)(orbit.a*(1+orbit.e)));
            if(chapterObjects==null)return;
            solar.gameObject.SetActive(true);
            for(int k=0;k<512;k++)
            {
                asteroidOrbitLine.SetPosition(k,Position(orbit,orbit.epoch+k*orbit.Period/512));
                earthOrbitLine.SetPosition(k,Position(earth,earth.epoch+k*earth.Period/512));
            }
            asteroid.name="Asteroid "+AsteroidName;asteroidLabel.text.text="Asteroid\n"+AsteroidName;
            discovery.text="ASTEROID DESCOPERIT ÎN PROIECTUL NEARBY";
            title.text="ASTEROIDUL "+AsteroidName;
            kicker.text=ViewIndex==0?$"UN AN DE {Math.Round(orbit.Period):F0} ZILE • ORBITA ÎN JURUL SOARELUI":"ORBITA DIN LATERAL • DINCOLO DE UN PLAN";
            fact.text=ViewIndex==0?facts[0]:$"{orbit.i:F1}° CARE SCHIMBĂ PERSPECTIVA";
            detail.text=descriptions[ViewIndex].Replace("2018 VQ1",AsteroidName);
        }
        void Update()
        {
            HandleOperator();
            if(!paused){float dt=Time.deltaTime*speed;elapsed+=dt;chapterTime+=dt;jd+=dt*daysPerSecond;}
            if(automatic&&chapterTime>=chapterSeconds)SetChapter(chapter+1);
            if(spin&&!paused)yaw+=Time.deltaTime*9;
            float tilt=ViewIndex==0?58:12;
            solar.localRotation=Quaternion.Slerp(solar.localRotation,Quaternion.Euler(tilt,yaw,0),1-Mathf.Exp(-Time.deltaTime*2));
            asteroid.localPosition=Position(orbit,jd);earthBody.localPosition=Position(earth,jd);
            for(int k=0;k<90;k++)trail.SetPosition(k,Position(orbit,jd-(89-k)*.6));
            // A gentle reveal leaves source text readable throughout chapter changes.
            float reveal=automatic?Mathf.Min(Mathf.SmoothStep(0,1,chapterTime/1.2f),Mathf.SmoothStep(0,1,(chapterSeconds-chapterTime)/1.2f)):1;
            foreach(var obj in chapterObjects)obj.localScale=Vector3.one*Mathf.Lerp(.02f,1,reveal);
            float end=Mathf.Lerp(-1.1f,1.1f,Mathf.Clamp01(chapterTime/chapterSeconds));
            progress.SetPosition(1,new Vector3(end,.4f,-4.8f));progress.SetPosition(2,new Vector3(end,.42f,-4.8f));
            float creditPhase=chapterTime/Mathf.Max(chapterSeconds,.01f);
            detail.text=creditPhase<.3f?descriptions[ViewIndex].Replace("2018 VQ1",AsteroidName):(useVN3?vn3TeamPages:teamPages)[creditPhase<.65f?0:1];
            clock.text=ViewIndex==0?$"{Math.Round(orbit.Period):F0} ZILE ÎNTR-O PRIVIRE • TIMP ACCELERAT":$"ÎNCLINARE FAȚĂ DE ECLIPTICĂ: {orbit.i:F3}°";
        }
        void HandleOperator()
        {
            var k=Keyboard.current;if(k==null)return;
            if(k.hKey.wasPressedThisFrame)help=!help;
            if(k.shiftKey.isPressed)
            {
                bool changed=true;
                if(k.upArrowKey.wasPressedThisFrame)offset-=.05f;
                else if(k.downArrowKey.wasPressedThisFrame)offset+=.05f;
                else if(k.pageUpKey.wasPressedThisFrame)zoom-=.25f;
                else if(k.pageDownKey.wasPressedThisFrame)zoom+=.25f;
                else if(k.backspaceKey.wasPressedThisFrame){offset=0;zoom=0;}
                else changed=false;
                if(changed){Align();PlayerPrefs.SetFloat("VQ1_AlignY",offset);PlayerPrefs.SetFloat("VQ1_AlignZoom",zoom);PlayerPrefs.Save();}
                return;
            }
            if(k.spaceKey.wasPressedThisFrame)paused=!paused;
            if(k.aKey.wasPressedThisFrame){automatic=true;SetChapter(chapter);}
            if(k.digit1Key.wasPressedThisFrame){automatic=false;SetChapter(0);}
            if(k.digit2Key.wasPressedThisFrame){automatic=false;SetChapter(1);}
            if(k.digit3Key.wasPressedThisFrame){automatic=false;SetChapter(2);}
            if(k.digit4Key.wasPressedThisFrame){automatic=false;SetChapter(3);}
            if(k.rightArrowKey.wasPressedThisFrame)SetChapter(chapter+1);
            if(k.rKey.wasPressedThisFrame)spin=!spin;
            if(k.minusKey.wasPressedThisFrame)speed=Mathf.Max(.25f,speed-.25f);
            if(k.equalsKey.wasPressedThisFrame)speed=Mathf.Min(3,speed+.25f);
            if(k.backspaceKey.wasPressedThisFrame){jd=orbit.epoch;speed=1;paused=false;automatic=true;SetChapter(0);}
        }
        void Align()
        {if(displayCamera){displayCamera.transform.position=cameraBase+Vector3.up*offset;displayCamera.fieldOfView=Mathf.Clamp(cameraFov+zoom,5,35);}}
        void OnGUI()
        {
            if(!help)return;
            GUI.Box(new Rect(15,15,430,185),"OPERATOR • H ascunde\nSpace: pauză | A: automat | 1–4: capitol fix\nSăgeată dreapta: capitol următor | R: rotație\n-/+: viteză | Backspace: reset\nShift + sus/jos: aliniere verticală\nShift + PageUp/PageDown: zoom\nShift + Backspace: reset aliniere\nCtrl + E: selector ecran (template)");
        }
        void OnDestroy()
        {foreach(var mat in ownedMaterials)if(mat)Destroy(mat);}
    }
}
