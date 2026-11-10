using UnityEngine;
namespace Goblingame.TestScene
{
    public sealed class CombatHUD : MonoBehaviour
    {
        public GoblinCombat player;
        public DamageableTarget target;
        public TrainingDummy dummy;
        public UnityEngine.UI.Image playerFill, targetFill;
        public UnityEngine.UI.Text playerText,targetText,status;
        public UnityEngine.UI.Text targetName,playerLevel,targetLevel,experienceText;
        public UnityEngine.UI.Image experienceFill;
        public UnityEngine.UI.RawImage targetPortrait;
        public GameObject bestiary;
        private EnemyActor[] enemies;
        private EnemyActor selected;
        private PlayerProgression progression;
        private float scan;
        private void Start(){enemies=FindObjectsByType<EnemyActor>(FindObjectsSortMode.None);if(player!=null)progression=player.GetComponent<PlayerProgression>();}
        private void Update()
        {
            if(bestiary!=null&&UnityEngine.InputSystem.Keyboard.current!=null&&Application.isFocused&&UnityEngine.InputSystem.Keyboard.current.tabKey.wasPressedThisFrame)bestiary.SetActive(!bestiary.activeSelf);
            if(player==null)return;
            scan-=Time.deltaTime;
            if(scan<=0&&targetName!=null)
            {
                scan=.2f;selected=null;float nearest=12f;
                foreach(var enemy in enemies)
                {
                    if(enemy==null||enemy.Health==null)continue;
                    Vector3 d=enemy.transform.position-player.transform.position;d.y=0;
                    float score=d.magnitude+(1-Vector3.Dot(player.transform.forward,d.normalized))*2;
                    if(score<nearest){nearest=score;selected=enemy;}
                }
                target=selected!=null?selected.Health:dummy!=null?dummy.GetComponent<DamageableTarget>():null;
                targetName.text=selected!=null?selected.displayName.ToUpperInvariant():"ТРЕНИРОВОЧНЫЙ МАНЕКЕН";
                if(targetLevel!=null)targetLevel.text=selected!=null?selected.level.ToString():"1";
                if(targetPortrait!=null){targetPortrait.texture=selected!=null?selected.portrait:null;targetPortrait.enabled=targetPortrait.texture!=null;}
            }
            playerFill.fillAmount=Mathf.MoveTowards(playerFill.fillAmount,player.Health/player.MaxHealth,Time.deltaTime*2);
            if(target==null)return;
            targetFill.fillAmount=Mathf.MoveTowards(targetFill.fillAmount,target.Fraction,Time.deltaTime*2);
            playerText.text=Mathf.CeilToInt(player.Health)+" / "+Mathf.CeilToInt(player.MaxHealth);
            targetText.text=Mathf.CeilToInt(target.Health)+" / "+Mathf.CeilToInt(target.MaxHealth);
            status.text=player.IsDead?"ТЫ ПОВЕРЖЕН — R, ЧТОБЫ ВСТАТЬ":selected!=null?selected.role.ToUpperInvariant()+"  ·  "+selected.Status:dummy!=null?dummy.Status:"";
            if(progression!=null&&experienceFill!=null){experienceFill.fillAmount=progression.Experience/(float)progression.Required;playerLevel.text=progression.Level.ToString();experienceText.text="ОПЫТ  "+progression.Experience+" / "+progression.Required;}
        }
    }
}
