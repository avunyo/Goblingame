using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
namespace Goblingame.TestScene
{
    public sealed class CombatHUD : MonoBehaviour
    {
        public GoblinCombat player;
        public DamageableTarget target;
        public TrainingDummy dummy;
        public Image playerFill, targetFill;
        public Text playerText,targetText,status;
        public Text targetName,playerLevel,targetLevel,experienceText;
        public Image experienceFill;
        public RawImage targetPortrait;
        public GameObject bestiary;

        private EnemyActor selected;
        private PlayerProgression progression;
        private ThirdPersonFollow view;
        private Camera cam;
        private CanvasGroup enemyPanel;
        private RectTransform canvasRect, reticle;
        private Image[] reticleBits;
        private Font font;
        private float scan, panelAlpha;
        private readonly Dictionary<EnemyActor,Texture> portraits=new Dictionary<EnemyActor,Texture>();
        private Texture dummyPortrait;

        private sealed class Popup{public Text text;public Vector3 world;public float age,life;public Vector2 drift;}
        private readonly List<Popup> popups=new List<Popup>();

        private static readonly Color Cream=new Color(.93f,.87f,.74f), Gold=new Color(1f,.78f,.3f), Blood=new Color(.95f,.25f,.18f);

        private void OnEnable(){CombatEvents.Number+=OnNumber;}
        private void OnDisable(){CombatEvents.Number-=OnNumber;}

        private void Start()
        {
            cam=Camera.main;if(cam!=null)view=cam.GetComponent<ThirdPersonFollow>();
            if(player!=null)progression=player.GetComponent<PlayerProgression>();
            canvasRect=(RectTransform)transform;
            font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if(targetName!=null)
            {
                var panel=targetName.transform.parent;
                enemyPanel=panel.GetComponent<CanvasGroup>();if(enemyPanel==null)enemyPanel=panel.gameObject.AddComponent<CanvasGroup>();
                enemyPanel.alpha=0f;enemyPanel.blocksRaycasts=false;
            }
            var legacy=transform.Find("Health Panel");if(legacy!=null)legacy.gameObject.SetActive(false);
            BuildReticle();
            BuildPrompt();
            BuildInventory();
            UpdateControlsHint();
            RenderPortraits();
        }

        // ───────────── portraits rendered from the actual 3D models ─────────────
        private void RenderPortraits()
        {
            foreach(var enemy in EnemyActor.All)
            {
                if(enemy==null)continue;
                Texture tex=enemy.portrait!=null?enemy.portrait:PortraitStudio.Render(enemy.transform,256,256,true);
                if(tex!=null)portraits[enemy]=tex;
                if(bestiary!=null&&!string.IsNullOrEmpty(enemy.id))
                {
                    var card=bestiary.transform.Find(enemy.id+" Card");
                    var pic=card!=null?card.Find("Portrait")?.GetComponent<RawImage>():null;
                    if(pic!=null&&pic.texture==null)pic.texture=PortraitStudio.Render(enemy.transform,310,388,false);
                }
            }
            if(dummy!=null)dummyPortrait=PortraitStudio.Render(dummy.transform,256,256,true);
            if(player!=null)
            {
                var goblinPanel=transform.Find("Goblin Status/Portrait Frame");
                if(goblinPanel!=null)
                {
                    var pic=goblinPanel.Find("Portrait")?.GetComponent<RawImage>();
                    if(pic==null)
                    {
                        var sigil=goblinPanel.Find("Goblin Sigil");if(sigil!=null)sigil.gameObject.SetActive(false);
                        var rt=new GameObject("Portrait",typeof(RectTransform)).GetComponent<RectTransform>();rt.SetParent(goblinPanel,false);
                        rt.anchorMin=rt.anchorMax=rt.pivot=new Vector2(0,1);rt.anchoredPosition=new Vector2(8,-8);rt.sizeDelta=new Vector2(129,129);
                        pic=rt.gameObject.AddComponent<RawImage>();pic.raycastTarget=false;
                    }
                    if(pic.texture==null)pic.texture=PortraitStudio.Render(player.transform,256,256,true);
                }
            }
        }

        // ───────────── inventory: belt, hearts, weapon arc, bag screen ─────────────
        private InventoryHUD inventoryHud;
        private void BuildInventory()
        {
            if(player==null)return;
            var inv=player.GetComponent<GoblinInventory>();if(inv==null)inv=player.gameObject.AddComponent<GoblinInventory>();
            inventoryHud=GetComponent<InventoryHUD>();if(inventoryHud==null)inventoryHud=gameObject.AddComponent<InventoryHUD>();
            inventoryHud.player=player;inventoryHud.inventory=inv;
            // hearts and level now live above the belt; the old corner panel goes away
            var status=transform.Find("Goblin Status");if(status!=null)status.gameObject.SetActive(false);
            var controls=transform.Find("Controls") as RectTransform;
            if(controls!=null){controls.anchorMin=controls.anchorMax=controls.pivot=new Vector2(0,1);controls.anchoredPosition=new Vector2(24,-18);controls.sizeDelta=new Vector2(1250,90);var ct=controls.GetComponent<Text>();if(ct!=null){ct.alignment=TextAnchor.UpperLeft;ct.color=new Color(Cream.r,Cream.g,Cream.b,.75f);}}
        }

        // ───────────── lock-on reticle ─────────────
        private void BuildReticle()
        {
            reticle=new GameObject("Lock Reticle",typeof(RectTransform)).GetComponent<RectTransform>();
            reticle.SetParent(transform,false);reticle.sizeDelta=new Vector2(56,56);reticle.anchorMin=reticle.anchorMax=new Vector2(.5f,.5f);
            reticleBits=new Image[5];
            Vector2[] at={new Vector2(0,26),new Vector2(26,0),new Vector2(0,-26),new Vector2(-26,0),Vector2.zero};
            for(int i=0;i<5;i++)
            {
                var bit=new GameObject(i<4?"Tip":"Core",typeof(RectTransform)).GetComponent<RectTransform>();
                bit.SetParent(reticle,false);bit.anchoredPosition=at[i];bit.sizeDelta=i<4?new Vector2(9,9):new Vector2(6,6);bit.localRotation=Quaternion.Euler(0,0,45);
                reticleBits[i]=bit.gameObject.AddComponent<Image>();reticleBits[i].raycastTarget=false;reticleBits[i].color=Gold;
            }
            reticle.gameObject.SetActive(false);
        }

        private void UpdateReticle()
        {
            var locked=view!=null?view.LockTarget:null;
            bool menuOpen=bestiary!=null&&bestiary.activeSelf;
            if(menuOpen||locked==null||cam==null||locked.Health==null){reticle.gameObject.SetActive(false);return;}
            Vector3 screen=cam.WorldToScreenPoint(locked.Health.HitPoint+Vector3.up*.2f);
            if(screen.z<0){reticle.gameObject.SetActive(false);return;}
            reticle.gameObject.SetActive(true);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect,screen,null,out var local);
            reticle.anchoredPosition=local;
            float t=locked.TelegraphProgress;
            Color c=locked.IsTelegraphing?Color.Lerp(Gold,Blood,.5f+.5f*Mathf.Sin(Time.time*20f)):Gold;
            float spread=locked.IsTelegraphing?1f-.35f*t:1f+.06f*Mathf.Sin(Time.unscaledTime*3f);
            reticle.localScale=Vector3.one*spread;
            reticle.localRotation=Quaternion.Euler(0,0,locked.IsTelegraphing?0f:Time.unscaledTime*25f);
            foreach(var b in reticleBits)b.color=c;
        }

        // ───────────── "press E" prompt ─────────────
        private Text promptText; private CanvasGroup promptGroup;
        private readonly Dictionary<string,Texture> portraitsById=new Dictionary<string,Texture>();
        private void BuildPrompt()
        {
            var rt=new GameObject("Interaction Prompt",typeof(RectTransform)).GetComponent<RectTransform>();rt.SetParent(transform,false);
            rt.anchorMin=rt.anchorMax=new Vector2(.5f,0f);rt.pivot=new Vector2(.5f,0f);rt.anchoredPosition=new Vector2(0,232);rt.sizeDelta=new Vector2(900,50);
            var back=rt.gameObject.AddComponent<Image>();back.color=new Color(.04f,.04f,.05f,.72f);back.raycastTarget=false;
            promptGroup=rt.gameObject.AddComponent<CanvasGroup>();promptGroup.alpha=0f;promptGroup.blocksRaycasts=false;
            var t=new GameObject("Text",typeof(RectTransform)).GetComponent<RectTransform>();t.SetParent(rt,false);t.anchorMin=Vector2.zero;t.anchorMax=Vector2.one;t.offsetMin=t.offsetMax=Vector2.zero;
            promptText=t.gameObject.AddComponent<Text>();promptText.font=font;promptText.fontSize=26;promptText.alignment=TextAnchor.MiddleCenter;promptText.color=Cream;promptText.raycastTarget=false;
        }
        private void UpdatePrompt()
        {
            if(promptGroup==null)return;
            string text=Interaction.Current;
            if(text!=null)promptText.text=text;
            promptGroup.alpha=Mathf.MoveTowards(promptGroup.alpha,text!=null?1f:0f,Time.unscaledDeltaTime*6f);
        }

        // ───────────── floating numbers ─────────────
        private void OnNumber(Vector3 world,float amount,CombatEvents.Kind kind,bool onPlayer)
        {
            Popup p=null;
            foreach(var q in popups)if(q.age>=q.life){p=q;break;}
            if(p==null)
            {
                var rt=new GameObject("Popup",typeof(RectTransform)).GetComponent<RectTransform>();rt.SetParent(transform,false);rt.sizeDelta=new Vector2(260,60);
                var text=rt.gameObject.AddComponent<Text>();text.font=font;text.alignment=TextAnchor.MiddleCenter;text.raycastTarget=false;text.horizontalOverflow=HorizontalWrapMode.Overflow;
                var outline=rt.gameObject.AddComponent<Outline>();outline.effectColor=new Color(0,0,0,.85f);outline.effectDistance=new Vector2(2,-2);
                p=new Popup{text=text};popups.Add(p);
            }
            p.world=world;p.age=0f;p.life=.9f;p.drift=new Vector2(Random.Range(-30f,30f),0);
            p.text.gameObject.SetActive(true);
            switch(kind)
            {
                case CombatEvents.Kind.Parried:p.text.text="ПАРИРОВАНО!";p.text.color=Gold;p.text.fontSize=34;p.life=1.1f;break;
                case CombatEvents.Kind.Blocked:p.text.text="БЛОК  -"+Mathf.CeilToInt(amount);p.text.color=new Color(.75f,.8f,.85f);p.text.fontSize=24;break;
                case CombatEvents.Kind.Reward:p.text.text="+"+Mathf.CeilToInt(amount)+" ОПЫТА";p.text.color=new Color(.78f,.55f,1f);p.text.fontSize=30;p.life=1.4f;break;
                case CombatEvents.Kind.Dodged:p.text.text="УКЛОН!";p.text.color=new Color(.55f,.9f,1f);p.text.fontSize=28;p.life=.8f;break;
                case CombatEvents.Kind.Heal:p.text.text="+"+Mathf.CeilToInt(amount);p.text.color=new Color(.45f,.9f,.3f);p.text.fontSize=26;break;
                default:
                    p.text.text="-"+Mathf.CeilToInt(amount);
                    p.text.color=onPlayer?Blood:amount>=20f?Gold:Cream;
                    p.text.fontSize=onPlayer?30:amount>=20f?40:30;break;
            }
        }

        private void UpdatePopups()
        {
            foreach(var p in popups)
            {
                if(p.age>=p.life){if(p.text.gameObject.activeSelf)p.text.gameObject.SetActive(false);continue;}
                p.age+=Time.unscaledDeltaTime;
                float t=p.age/p.life;
                Vector3 screen=cam!=null?cam.WorldToScreenPoint(p.world):Vector3.zero;
                if(screen.z<0){p.text.enabled=false;continue;}
                p.text.enabled=true;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect,screen,null,out var local);
                var rt=(RectTransform)p.text.transform;
                rt.anchorMin=rt.anchorMax=new Vector2(.5f,.5f);
                rt.anchoredPosition=local+p.drift*t+Vector2.up*(70f*t);
                float pop=t<.12f?Mathf.Lerp(1.6f,1f,t/.12f):1f;
                rt.localScale=Vector3.one*pop;
                var c=p.text.color;c.a=t<.6f?1f:1f-(t-.6f)/.4f;p.text.color=c;
            }
        }

        // ───────────── main ─────────────
        private void Update()
        {
            var kb=UnityEngine.InputSystem.Keyboard.current;
            if(bestiary!=null&&kb!=null&&Application.isFocused&&kb.tabKey.wasPressedThisFrame&&(inventoryHud==null||!inventoryHud.ScreenOpen))
            {
                bestiary.SetActive(!bestiary.activeSelf);
                Cursor.lockState=bestiary.activeSelf?CursorLockMode.None:CursorLockMode.Locked;Cursor.visible=bestiary.activeSelf;
            }
            UpdatePopups();
            UpdatePrompt();
            if(player==null)return;
            UpdateReticle();

            scan-=Time.unscaledDeltaTime;
            if(scan<=0f){scan=.15f;PickTarget();}

            bool show=target!=null&&!player.IsDead;
            panelAlpha=Mathf.MoveTowards(panelAlpha,show?1f:0f,Time.unscaledDeltaTime*4f);
            if(enemyPanel!=null)enemyPanel.alpha=panelAlpha;

            playerFill.fillAmount=Mathf.MoveTowards(playerFill.fillAmount,player.Health/player.MaxHealth,Time.deltaTime*2f);
            playerText.text=Mathf.CeilToInt(player.Health)+" / "+Mathf.CeilToInt(player.MaxHealth);
            playerFill.color=Color.Lerp(new Color(.75f,.18f,.1f),new Color(.29f,.66f,.11f),Mathf.InverseLerp(.15f,.45f,player.Health/player.MaxHealth));
            if(progression!=null&&experienceFill!=null){experienceFill.fillAmount=Mathf.MoveTowards(experienceFill.fillAmount,progression.Experience/(float)progression.Required,Time.deltaTime);playerLevel.text=progression.Level.ToString();experienceText.text="ОПЫТ  "+progression.Experience+" / "+progression.Required;}

            if(player.IsDead){status.text="ТЫ ПОВЕРЖЕН — R, ЧТОБЫ ВСТАТЬ";return;}
            if(target==null)return;
            targetFill.fillAmount=Mathf.MoveTowards(targetFill.fillAmount,target.Fraction,Time.deltaTime*2f);
            targetText.text=Mathf.CeilToInt(target.Health)+" / "+Mathf.CeilToInt(target.MaxHealth);
            if(selected!=null)
            {
                string rank=selected.elite?"ЭЛИТНЫЙ ПРОТИВНИК":selected.role.ToUpperInvariant();
                status.text=rank+"  ·  "+selected.Status;
                status.color=selected.IsTelegraphing?Blood:Cream;
            }
            else if(dummy!=null){status.text=dummy.Status;status.color=Cream;}
        }

        private void PickTarget()
        {
            EnemyActor pick=view!=null?view.LockTarget:null;
            if(pick==null)
            {
                float best=11f;
                foreach(var enemy in EnemyActor.All)
                {
                    if(enemy==null||enemy.Health==null||enemy.Health.IsDead||!enemy.IsEngaged)continue;
                    float d=Vector3.Distance(enemy.transform.position,player.transform.position);
                    if(d<best){best=d;pick=enemy;}
                }
            }
            selected=pick;
            if(selected!=null)
            {
                target=selected.Health;
                targetName.text=selected.displayName.ToUpperInvariant();
                if(targetLevel!=null)targetLevel.text=selected.level.ToString();
                if(!portraits.TryGetValue(selected,out var tex))
                {
                    // mobs that crawled out of a lair: reuse the portrait of their kind, or shoot one now
                    if(string.IsNullOrEmpty(selected.id)||!portraitsById.TryGetValue(selected.id,out tex))tex=selected.portrait!=null?selected.portrait:PortraitStudio.Render(selected.transform,256,256,true);
                    if(tex!=null){portraits[selected]=tex;if(!string.IsNullOrEmpty(selected.id))portraitsById[selected.id]=tex;}
                }
                SetPortrait(tex);
            }
            else if(dummy!=null&&Vector3.Distance(dummy.transform.position,player.transform.position)<4f)
            {
                target=dummy.GetComponent<DamageableTarget>();
                targetName.text="ТРЕНИРОВОЧНЫЙ МАНЕКЕН";if(targetLevel!=null)targetLevel.text="1";
                SetPortrait(dummyPortrait);
            }
            else target=null;
        }

        private void SetPortrait(Texture tex)
        {
            if(targetPortrait==null)return;
            targetPortrait.texture=tex;targetPortrait.enabled=tex!=null;
        }

        private void UpdateControlsHint()
        {
            var controls=transform.Find("Controls");if(controls==null)return;
            var t=controls.GetComponent<Text>();if(t==null)return;
            t.text="WASD — движение   Shift — бег   Пробел — прыжок   Мышь — камера   Ctrl + колесо — зум   Колесо — сменить оружие   1–9 — пояс   I — сумка\n"+
                   "ЛКМ — серия ударов (с бега — рывок, в прыжке — удар с воздуха)   Q — удар головой, затем ЛКМ продолжает\n"+
                   "ПКМ — блок (вовремя = парирование)   F / СКМ — фокус   R — уклонение (на бегу — подкат) / встать   E — сундук, рунный круг   Tab — противники";
            t.fontSize=15;
        }
    }

    /// <summary>Renders a character into a texture once (used for HUD portraits and bestiary cards).</summary>
    public static class PortraitStudio
    {
        public static Texture Render(Transform subject,int width,int height,bool headShot)
        {
            if(subject==null)return null;
            var own=new List<Renderer>();
            foreach(var r in subject.GetComponentsInChildren<Renderer>())
                if(r.enabled&&r.GetComponent<TextMesh>()==null&&!(r is ParticleSystemRenderer))own.Add(r);
            if(own.Count==0)return null;
            Bounds b=own[0].bounds;foreach(var r in own)b.Encapsulate(r.bounds);

            var hidden=new List<Renderer>();var canvases=new List<Canvas>();
            foreach(var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if(r.enabled&&!own.Contains(r)){r.enabled=false;hidden.Add(r);}
            foreach(var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if(c.enabled){c.enabled=false;canvases.Add(c);}

            var go=new GameObject("Portrait Camera");
            var cam=go.AddComponent<Camera>();
            var rt=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32){name=subject.name+" Portrait",antiAliasing=4};
            Texture2D result=null;
            try
            {
                cam.enabled=false;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.11f,.10f,.09f,1f);
                cam.fieldOfView=headShot?24f:30f;cam.nearClipPlane=.05f;cam.farClipPlane=50f;cam.targetTexture=rt;
                Vector3 forward=subject.forward;forward.y=0;if(forward.sqrMagnitude<.01f)forward=Vector3.forward;forward.Normalize();
                float h=b.size.y;
                Vector3 focus=headShot?new Vector3(b.center.x,b.max.y-h*.2f,b.center.z):b.center;
                float frame=headShot?h*.45f:h*1.05f;
                float dist=frame*.5f/Mathf.Tan(cam.fieldOfView*.5f*Mathf.Deg2Rad)+b.extents.z*.5f;
                Vector3 side=Vector3.Cross(Vector3.up,forward);
                go.transform.position=focus+forward*dist+side*dist*.25f+Vector3.up*h*.03f;
                go.transform.LookAt(focus);
                cam.Render();
                // copy into a normal texture: render-texture contents are not guaranteed to persist
                var prev=RenderTexture.active;RenderTexture.active=rt;
                result=new Texture2D(width,height,TextureFormat.RGBA32,false){name=subject.name+" Portrait"};
                result.ReadPixels(new Rect(0,0,width,height),0,0);result.Apply();
                RenderTexture.active=prev;
            }
            catch(System.Exception e){Debug.LogWarning("Portrait render failed for "+subject.name+": "+e.Message);}
            finally
            {
                foreach(var r in hidden)if(r!=null)r.enabled=true;
                foreach(var c in canvases)if(c!=null)c.enabled=true;
                cam.targetTexture=null;Object.Destroy(go);rt.Release();Object.Destroy(rt);
            }
            return result;
        }
    }
}
