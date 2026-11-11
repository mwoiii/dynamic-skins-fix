using RoR2;
using UnityEngine;

namespace DynamicSkinsFix {
    public class FixAcridMissingGoo : MonoBehaviour {
        public void Start() {
            ParticleSystemRenderer gooParticle = GetComponent<ModelLocator>()?.modelTransform.GetComponent<ChildLocator>()?.FindChild("Head")?.Find("Goo")?.GetComponent<ParticleSystemRenderer>();
            if (gooParticle != null && gooParticle.sharedMaterial == null) {
                gooParticle.gameObject.SetActive(false);
            }
            Destroy(this);
        }
    }
}
