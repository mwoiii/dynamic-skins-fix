using RoR2;
using System;
using System.Collections;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.AddressableAssets;

#pragma warning disable CS0618

namespace DynamicSkinsFix {
    public static class AcridFixes {
        // I don't know man I don't know why this stuff is here
        public static void Init() {
            DupeRenderer();
            On.RoR2.SkinCatalog.Init += SwapAcridRenderer;
        }

        public static void DupeRenderer() {
            GameObject crocoPrefab = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Croco/CrocoBody.prefab").WaitForCompletion();
            if (crocoPrefab) {
                crocoPrefab.AddComponent<FixAcridMissingGoo>();
                GameObject mdlCroco = crocoPrefab.GetComponent<ModelLocator>().modelTransform.gameObject;
                Transform spineMesh = mdlCroco.transform.Find("CrocoSpineMesh");
                SkinnedMeshRenderer renderer = spineMesh.GetComponent<SkinnedMeshRenderer>();

                // duplicating CrocoSpineMesh
                // assigning the same name so that they have the same transformpath
                // and sharing the mesh with the original renderer
                // all so that renderer componentsinchildren[3] is valid
                Transform dupe = UnityEngine.Object.Instantiate(mdlCroco.transform.Find("CrocoSpineMesh"));
                SkinnedMeshRenderer newRenderer = dupe.GetComponent<SkinnedMeshRenderer>();
                newRenderer.transform.name = "CrocoSpineMesh";
                newRenderer.sharedMesh = renderer.sharedMesh;
                dupe.parent = mdlCroco.transform;
            } else {
                Log.Error("Could not find CrocoBody Prefab. Acrid fixes not applied.");
            }
        }

        public static IEnumerator SwapAcridRenderer(On.RoR2.SkinCatalog.orig_Init orig) {
            GameObject crocoPrefab = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Croco/CrocoBody.prefab").WaitForCompletion();
            GameObject modelTransformObj = crocoPrefab.GetComponent<ModelLocator>().modelTransform.gameObject;
            ModelSkinController modelSkinController = modelTransformObj.GetComponent<ModelSkinController>();
            Renderer[] renderers = modelTransformObj.GetComponentsInChildren<Renderer>(true);
            foreach (SkinDef skinDef in modelSkinController.skins) {
                bool interactedWithIndex3 = false;

                for (int i = 0; i < skinDef.meshReplacements.Length; i++) {
                    interactedWithIndex3 = renderers[3] == skinDef.meshReplacements[i].renderer;
                    if (interactedWithIndex3) {
                        break;
                    }
                }

                if (!interactedWithIndex3) {
                    for (int i = 0; i < skinDef.rendererInfos.Length; i++) {
                        interactedWithIndex3 = renderers[3] == skinDef.rendererInfos[i].renderer;
                        if (interactedWithIndex3) {
                            break;
                        }
                    }

                    for (int i = 0; i < skinDef.gameObjectActivations.Length; i++) {
                        interactedWithIndex3 = renderers[3].gameObject == skinDef.gameObjectActivations[i].gameObject;
                        if (interactedWithIndex3) {
                            break;
                        }
                    }
                }

                // only if we can confirm it is incorrectly incremented - we cannot always confirm this
                if (interactedWithIndex3) {

                    for (int i = 0; i < skinDef.meshReplacements.Length; i++) {
                        skinDef.meshReplacements[i].renderer = GetCorrectRenderer(skinDef.meshReplacements[i].renderer, renderers);
                    }

                    for (int i = 0; i < skinDef.rendererInfos.Length; i++) {
                        skinDef.rendererInfos[i].renderer = GetCorrectRenderer(skinDef.rendererInfos[i].renderer, renderers);
                    }

                    for (int i = 0; i < skinDef.gameObjectActivations.Length; i++) {
                        skinDef.gameObjectActivations[i].gameObject = GetCorrectGameObject(skinDef.gameObjectActivations[i], renderers);
                    }
                }
            }

            BodyCatalog.skins[(int)BodyCatalog.FindBodyIndex("CrocoBody")] = modelSkinController.skins;
            return orig();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Renderer GetCorrectRenderer(Renderer renderer, Renderer[] componentsInChildren) {
            // sub 1 to get correct index
            return componentsInChildren[Array.IndexOf(componentsInChildren, renderer) - 1];
        }

        private static GameObject GetCorrectGameObject(SkinDef.GameObjectActivation gameObjectActivation, Renderer[] renderers) {
            if (gameObjectActivation.gameObject == renderers[3].gameObject) {
                return renderers[2].gameObject;
            } else if (gameObjectActivation.gameObject == renderers[2].gameObject) {
                return renderers[1].gameObject;
            } else {
                return renderers[0].gameObject;
            }
        }
    }
}
