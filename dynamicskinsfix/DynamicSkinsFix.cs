using System;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using BepInEx;
using HG;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using RoR2;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace DynamicSkinsFix {

    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]

    public class DynamicSkinsFix : BaseUnityPlugin {
        public const string PluginGUID = PluginAuthor + "." + PluginName;
        public const string PluginAuthor = "Miyowi";
        public const string PluginName = "DynamicSkinsFix";
        public const string PluginVersion = "1.0.4";

        private System.Random random = new System.Random();

        private static int callCount;

        private const int callLimit = 50;

        public void Awake() {
            Log.Init(Logger);

            DupeRenderer();
            AttachAcridComponent();
            On.RoR2.SkinCatalog.Init += SwapAcridRenderer;
            IL.RoR2.SkinDef.ApplyAsync += CallObsolete;
            IL.RoR2.SkinDef.Apply += ObsoleteRet;
            IL.RoR2.BodyCatalog.GetBodySkins += AccessOldArray;
        }

        private static void ObsoleteRet(ILContext il) {
            // return only if ApplyAsync is in the stacktrace (so most likely due to this mod)
            var shouldReturnDelegate = new Func<bool>(() => {
                callCount++;
                StackTrace trace = new StackTrace();
                foreach (StackFrame frame in trace.GetFrames()) {
                    MethodBase method = frame.GetMethod();
                    if (callCount > callLimit) {
                        Log.Warning("Potential infinite loop detected. Aborting to prevent game crash");
                        return true;
                    } else if (method.Name == "DMD<RoR2.SkinDef::ApplyAsync>" || method.Name == "ApplyAsync") {
                        return true;
                    }
                }
                return false;
            });

            ILCursor c = new ILCursor(il);
            if (c.TryGotoNext(MoveType.Before, x => x.MatchLdarg(0))) {
                ILLabel resumeLabel = c.DefineLabel();
                c.EmitDelegate(shouldReturnDelegate);
                c.Emit(OpCodes.Brfalse, resumeLabel);
                c.Emit(OpCodes.Ret);
                c.MarkLabel(resumeLabel);
            } else {
                Log.Error("ILHook failed. Some dynamic skins will not work, or worse");
            }
        }

        private static void CallObsolete(ILContext il) {
            var applyDelegate = delegate (RoR2.SkinDef self, GameObject modelObject) {
                self.Apply(modelObject);
            };

            ILCursor c = new ILCursor(il);
            if (c.TryGotoNext(MoveType.Before, x => x.MatchRet())) {
                c.Emit(OpCodes.Ldarg_0);
                c.Emit(OpCodes.Ldarg_1);
                c.EmitDelegate(applyDelegate);
            } else {
                Log.Error("ILHook failed. Some dynamic skins will not work, or worse");
            }
        }

        private static void AccessOldArray(ILContext il) {
            var rewriteDelegate = new Func<BodyIndex, SkinDef[]>((BodyIndex bodyIndex) => {
                SkinDef[] bodySkinDefs = SkinCatalog.GetBodySkinDefs(bodyIndex);
                if (bodySkinDefs.Length == 0) {
                    SkinDef[][] array = BodyCatalog.skins;
                    SkinDef[] defaultValue = Array.Empty<SkinDef>();
                    return ArrayUtils.GetSafe(array, (int)bodyIndex, in defaultValue);
                } else {
                    return bodySkinDefs;
                }
            });

            // branching over the entire original code and defining a new behaviour
            // run the standard method. if no results, run the method how it was before 1.3.9 changed it
            ILCursor c = new ILCursor(il);
            if (c.TryGotoNext(MoveType.Before, x => x.MatchLdarg(0))) {
                ILLabel skipLabel = c.DefineLabel();
                c.Emit(OpCodes.Br, skipLabel);
                if (c.TryGotoNext(MoveType.After, x => x.MatchRet())) {
                    c.MarkLabel(skipLabel);
                    c.Emit(OpCodes.Ldarg_0);
                    c.EmitDelegate(rewriteDelegate);
                    c.Emit(OpCodes.Ret);
                } else {
                    Log.Error("ILHook failed. Some dynamic skins will not work, or worse");
                }
            } else {
                Log.Error("ILHook failed. Some dynamic skins will not work, or worse");
            }
        }

        private static void AttachAcridComponent() {
            GameObject crocoPrefab = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Croco/CrocoBody.prefab").WaitForCompletion();
            crocoPrefab.AddComponent<FixAcridMissingGoo>();
        }

        private static void DupeRenderer() {
            GameObject crocoPrefab = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Croco/CrocoBody.prefab").WaitForCompletion();
            if (crocoPrefab != null) {
                GameObject mdlCroco = crocoPrefab.GetComponent<ModelLocator>().modelTransform.gameObject;
                Transform spineMesh = mdlCroco.transform.Find("CrocoSpineMesh");
                SkinnedMeshRenderer renderer = spineMesh.GetComponent<SkinnedMeshRenderer>();

                // duplicating CrocoSpineMesh
                // assigning the same name so that they have the same transformpath
                // and sharing the mesh with the original renderer
                // all so that renderer componentsinchildren[3] is valid
                Transform dupe = Instantiate(mdlCroco.transform.Find("CrocoSpineMesh"));
                SkinnedMeshRenderer newRenderer = dupe.GetComponent<SkinnedMeshRenderer>();
                newRenderer.transform.name = "CrocoSpineMesh";
                newRenderer.sharedMesh = renderer.sharedMesh;
                dupe.parent = mdlCroco.transform;
            } else {
                Log.Error("Could not find CrocoBody Prefab. Acrid fixes not applied.");
            }
        }


        private static IEnumerator SwapAcridRenderer(On.RoR2.SkinCatalog.orig_Init orig) {
            GameObject crocoPrefab = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Croco/CrocoBody.prefab").WaitForCompletion();
            GameObject modelTransformObj = crocoPrefab.GetComponent<ModelLocator>().modelTransform.gameObject;
            ModelSkinController modelSkinController = modelTransformObj.GetComponent<ModelSkinController>();
            Renderer[] componentsInChildren = modelTransformObj.GetComponentsInChildren<Renderer>(true);

            foreach (SkinDef skinDef in modelSkinController.skins) {
                bool interactedWithIndex3 = false;

                for (int i = 0; i < skinDef.meshReplacements.Length; i++) {
                    interactedWithIndex3 = componentsInChildren[3] == skinDef.meshReplacements[i].renderer;
                    if (interactedWithIndex3) {
                        break;
                    }
                }

                if (!interactedWithIndex3) {
                    for (int i = 0; i < skinDef.rendererInfos.Length; i++) {
                        interactedWithIndex3 = componentsInChildren[3] == skinDef.rendererInfos[i].renderer;
                        if (interactedWithIndex3) {
                            break;
                        }
                    }

                    for (int i = 0; i < skinDef.gameObjectActivations.Length; i++) {
                        interactedWithIndex3 = componentsInChildren[3].gameObject == skinDef.gameObjectActivations[i].gameObject;
                        if (interactedWithIndex3) {
                            break;
                        }
                    }
                }

                // only if we can confirm it is incorrectly incremented - we cannot always confirm this
                if (interactedWithIndex3) {

                    for (int i = 0; i < skinDef.meshReplacements.Length; i++) {
                        skinDef.meshReplacements[i].renderer = GetCorrectRenderer(skinDef.meshReplacements[i].renderer, componentsInChildren);
                    }

                    for (int i = 0; i < skinDef.meshReplacements.Length; i++) {
                        skinDef.rendererInfos[i].renderer = GetCorrectRenderer(skinDef.rendererInfos[i].renderer, componentsInChildren);
                    }

                    for (int i = 0; i < skinDef.gameObjectActivations.Length; i++) {
                        skinDef.gameObjectActivations[i].gameObject = GetCorrectGameObject(skinDef.gameObjectActivations[i], componentsInChildren);
                    }
                }
            }

            BodyCatalog.skins[(int)BodyCatalog.FindBodyIndex("CrocoBody")] = modelSkinController.skins;
            return orig();
        }

        private static Renderer GetCorrectRenderer(Renderer renderer, Renderer[] componentsInChildren) {
            // simply sub 1 to get correct index
            return componentsInChildren[Array.IndexOf(componentsInChildren, renderer) - 1];
        }
        private static GameObject GetCorrectGameObject(SkinDef.GameObjectActivation gameObjectActivation, Renderer[] componentsInChildren) {
            if (gameObjectActivation.gameObject == componentsInChildren[3].gameObject) {
                return componentsInChildren[2].gameObject;
            } else if (gameObjectActivation.gameObject == componentsInChildren[2].gameObject) {
                return componentsInChildren[1].gameObject;
            } else {
                return componentsInChildren[0].gameObject;
            }
        }

        /*
        private static void RemoveDupeRenderer() {
            
            Log.Info("Removing dupe renderer");
            GameObject crocoPrefab = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Croco/CrocoBody.prefab").WaitForCompletion();
            if (crocoPrefab != null) {
                GameObject mdlCroco = crocoPrefab.GetComponent<ModelLocator>().modelTransform.gameObject;
                GameObject dupe = mdlCroco.GetComponentsInChildren<Renderer>(true)[3].gameObject;
                Destroy(dupe);
            } else {
                Log.Error("Could not find CrocoBody Prefab for renderer removal. Acrid might look a little funny.");
            }
            
        }
        */

        private void FixedUpdate() {
            callCount = 0;
        }
    }
}
