using UnityEngine;
namespace Goblingame.TestScene
{
    public sealed class EnemyHealthLabel : MonoBehaviour
    {
        public DamageableTarget target;
        public UnityEngine.UI.Image fill;
        private Camera view;
        private void LateUpdate(){if(view==null)view=Camera.main;if(view!=null)transform.rotation=view.transform.rotation;if(target!=null)fill.fillAmount=target.Fraction;}
    }
}
