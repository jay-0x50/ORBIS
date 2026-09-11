using UnityEngine;

namespace Orbis.Game
{
    /// <summary>Facial lighting basis only. Never rotates or adds Humanoid bones.</summary>
    [DefaultExecutionOrder(900)]
    public sealed class ExplorerFaceLighting:MonoBehaviour
    {
        Renderer target;Transform head;Vector3 localForward,localRight;MaterialPropertyBlock block;
        void Awake()
        {
            target=GetComponent<Renderer>();var animator=GetComponentInParent<Animator>();
            if(target==null||animator==null||!animator.isHuman){enabled=false;return;}
            head=animator.GetBoneTransform(HumanBodyBones.Head);
            var left=animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);var right=animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            if(head==null||left==null||right==null){enabled=false;return;}
            Vector3 horizontalRight=Vector3.ProjectOnPlane(right.position-left.position,Vector3.up).normalized;
            localRight=head.InverseTransformDirection(horizontalRight);
            localForward=head.InverseTransformDirection(Vector3.Cross(horizontalRight,Vector3.up).normalized);
            block=new MaterialPropertyBlock();Synchronize();
        }
        void LateUpdate()=>Synchronize();
        public void Synchronize()
        {
            if(target==null||head==null)return;
            target.GetPropertyBlock(block); // Keep M3 appearance/element properties on the same renderer.
            target.SetPropertyBlock(WriteBasis(block));
        }
        MaterialPropertyBlock WriteBasis(MaterialPropertyBlock value)
        {
            value.SetVector("_FaceForwardWS",head.TransformDirection(localForward).normalized);
            value.SetVector("_FaceRightWS",head.TransformDirection(localRight).normalized);return value;
        }
    }
}
