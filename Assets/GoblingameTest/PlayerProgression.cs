using UnityEngine;
namespace Goblingame.TestScene
{
    public sealed class PlayerProgression : MonoBehaviour
    {
        [SerializeField] private int level=1, experience;
        public int Level=>level;
        public int Experience=>experience;
        public int Required=>100+(level-1)*50;
        public void AddExperience(int amount){experience+=Mathf.Max(0,amount);while(experience>=Required){experience-=Required;level++;}}
    }
}
