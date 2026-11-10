using UnityEngine;

namespace Goblingame.TestScene
{
    [DefaultExecutionOrder(200)]
    public sealed class GoblinIdleEyes : MonoBehaviour
    {
        [SerializeField] private SkinnedMeshRenderer face;
        private Animator animator;
        private GoblinCombat combat;
        private GoblinMovement movement;
        private Mesh original, instance;
        private Vector2[] restUV, animatedUV;
        private Vector2 gaze;
        private Transform head;
        private Quaternion neutralHead;
        private Vector3 neutralForward, modelUp;

        // Centres of the painted pupils in GoblinBaseColor.png, in bottom-left UV space.
        public static float PupilWeight(Vector2 uv)
        {
            float d=Mathf.Min(Vector2.Distance(uv,new Vector2(.442f,.780f)),
                Vector2.Distance(uv,new Vector2(.566f,.774f)));
            return 1f-Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(.0075f,.0125f,d));
        }
        private void Awake()
        {
            animator=GetComponentInChildren<Animator>();combat=GetComponent<GoblinCombat>();movement=GetComponent<GoblinMovement>();
            if(face==null||face.sharedMesh==null||!face.sharedMesh.isReadable){enabled=false;return;}
            original=face.sharedMesh;instance=Instantiate(original);instance.name="Goblin eyes (runtime)";instance.MarkDynamic();
            restUV=original.uv;animatedUV=(Vector2[])restUV.Clone();face.sharedMesh=instance;
        }
        private void Start()
        {
            if(animator==null)return;
            head=animator.GetBoneTransform(HumanBodyBones.Head);
            if(head==null)return;
            neutralHead=Quaternion.Inverse(animator.transform.rotation)*head.rotation;
            neutralForward=animator.transform.InverseTransformDirection(transform.forward);
            modelUp=animator.transform.InverseTransformDirection(transform.up);
        }
        private void LateUpdate()
        {
            if(instance==null||animator==null||head==null)return;
            var state=animator.GetCurrentAnimatorStateInfo(0);
            bool relaxed=state.IsName("Base Layer.Idle")&&!animator.IsInTransition(0)
                &&(combat==null||(!combat.InCombat&&!combat.IsDead))&&(movement==null||!movement.IsMoving);
            // Read the animated head relative to the model, so turning the entire
            // character or correcting the run/idle heading cannot steer its pupils.
            Quaternion headInModel=Quaternion.Inverse(animator.transform.rotation)*head.rotation;
            Vector3 looking=headInModel*Quaternion.Inverse(neutralHead)*neutralForward;
            float yaw=Vector3.SignedAngle(Vector3.ProjectOnPlane(neutralForward,modelUp),Vector3.ProjectOnPlane(looking,modelUp),modelUp);
            float pitch=Mathf.Asin(Mathf.Clamp(Vector3.Dot(looking.normalized,modelUp),-1,1))*Mathf.Rad2Deg;
            Vector2 desired=relaxed?new Vector2(Mathf.Clamp(yaw/12f,-1,1)*.0032f,Mathf.Clamp(pitch/15f,-1,1)*.0015f):Vector2.zero;
            gaze=Vector2.Lerp(gaze,desired,1f-Mathf.Exp(-Time.deltaTime/.1f));
            for(int i=0;i<restUV.Length;i++)animatedUV[i]=restUV[i]-gaze*PupilWeight(restUV[i]);
            instance.uv=animatedUV;
        }
        private void OnDisable(){if(instance!=null&&restUV!=null)instance.uv=restUV;gaze=Vector2.zero;}
        private void OnDestroy(){if(face!=null&&face.sharedMesh==instance)face.sharedMesh=original;if(instance!=null)Destroy(instance);}
    }
}
