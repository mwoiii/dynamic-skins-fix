using BepInEx;
using HG;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using RoR2;
using RoR2.ContentManagement;
using RoR2.SurvivorMannequins;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;

#pragma warning disable CS0618

namespace DynamicSkinsFix {

    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]

    public class DynamicSkinsFix : BaseUnityPlugin {
        public const string PluginGUID = PluginAuthor + "." + PluginName;
        public const string PluginAuthor = "Miyowi";
        public const string PluginName = "DynamicSkinsFix";
        public const string PluginVersion = "1.1.0";

        private static List<Action> applyLoadoutToMannequinInstanceOrigs = new List<Action>();

        private static HashSet<SkinDef> justCalledSkinDefApply = new HashSet<SkinDef>();

        public void Awake() {
            Log.Init(Logger);

            AcridFixes.Init();

            On.RoR2.SurvivorMannequins.SurvivorMannequinSlotController.ApplyLoadoutToMannequinInstance += SelectivelyResetSkinAndDelay;
            IL.RoR2.SkinDef.ApplyAsync += CallObsoleteApply;
            IL.RoR2.SkinDef.Apply += PreventCyclicalCall;
            IL.RoR2.BodyCatalog.GetBodySkins += AccessOldArray;
        }

        private static void SelectivelyResetSkinAndDelay(On.RoR2.SurvivorMannequins.SurvivorMannequinSlotController.orig_ApplyLoadoutToMannequinInstance orig, SurvivorMannequinSlotController self) {
            if (!self.mannequinInstanceTransform) {
                orig(self);
                return;
            }

            // setting up all the necessary data to determine the value of `shouldResetAndDelay`
            // and one million nullchecks
            var shouldResetAndDelay = false;
            var skinController = self.GetComponentInChildren<ModelSkinController>();
            var nextSkinIndex = (int)self.currentLoadout.bodyLoadoutManager.GetSkinIndex(SurvivorCatalog.GetBodyIndexFromSurvivorIndex(self.currentSurvivorDef.survivorIndex));
            if (skinController && skinController.skins != null && skinController.skins.Length > 0 && nextSkinIndex < skinController.skins.Length) {
                var baseSkin = skinController.skins[0];
                var nextSkin = skinController.skins[nextSkinIndex];
                nextSkin.BakeAsync().WaitForCompletion(20); // setting a call limit because ig vanilla bug they can just Not Finish but still work fine

                if (baseSkin?.runtimeSkin?.meshReplacementTemplates != null && nextSkin?.runtimeSkin?.meshReplacementTemplates != null) {
                    var baseMeshTemplates = baseSkin.runtimeSkin.meshReplacementTemplates;
                    var nextMeshTemplates = nextSkin.runtimeSkin.meshReplacementTemplates;

                    for (int i = 0; i < baseMeshTemplates.Length; i++) {
                        var baseMeshTemplate = baseMeshTemplates[i];
                        Mesh nextMesh = nextMeshTemplates.FirstOrDefault(x => x.transformPath == baseMeshTemplate.transformPath).meshReference.WaitForCompletion();

                        var activeRenderer = skinController.transform.Find(baseMeshTemplate.transformPath).AsValidOrNull()?.GetComponent<SkinnedMeshRenderer>();
                        var baseRenderer = baseSkin.rootObject?.transform.Find(baseMeshTemplate.transformPath).AsValidOrNull()?.GetComponent<SkinnedMeshRenderer>();

                        if (activeRenderer &&
                            baseRenderer &&
                            nextMesh &&
                            activeRenderer.bones.Length > baseRenderer.bones.Length &&              // if current mesh has extra bones
                            nextMesh.GetBindposeCount() > baseRenderer.bones.Length &&              // and the next mesh has extra bones
                            !nextMesh.bindposes.IsIdenticalTo(activeRenderer.sharedMesh.bindposes)  // and the current and next meshes are of a different armature
                           ) {                                                                      // then we should apply base skin first and delay next skin by 1 frame
                            shouldResetAndDelay = true;
                            break;
                        }
                    }
                }
            }

            if (shouldResetAndDelay) {
                skinController.ApplySkinAsync(0, AsyncReferenceHandleUnloadType.OnSceneUnload).WaitForCompletion(20);
                applyLoadoutToMannequinInstanceOrigs.Add(() => { 
                    orig(self);
                });
            } else {
                orig(self);
            }
        }

        private static void PreventCyclicalCall(ILContext il) {
            // skipping execution of the method if got here via the applyasync ilhook to prevent a crash
            var shouldReturnDelegate = new Func<SkinDef, bool>((skinDef) => {
                if (justCalledSkinDefApply.Contains(skinDef)) {
                    return true;
                }
                return false;
            });

            ILCursor c = new ILCursor(il);
            if (c.TryGotoNext(MoveType.Before, x => x.MatchLdarg(0))) {
                ILLabel resumeLabel = c.DefineLabel();
                c.Emit(OpCodes.Ldarg_0);
                c.EmitDelegate(shouldReturnDelegate);
                c.Emit(OpCodes.Brfalse, resumeLabel);
                c.Emit(OpCodes.Ret);
                c.MarkLabel(resumeLabel);
            } else {
                Log.Error("ILHook failed. Some dynamic skins will not work, or worse");
            }
        }

        private static void CallObsoleteApply(ILContext il) {
            var applyDelegate = delegate (SkinDef self, GameObject modelObject) {
                if (!justCalledSkinDefApply.Contains(self)) {
                    justCalledSkinDefApply.Add(self);
                }
                self.Apply(modelObject);
                if (justCalledSkinDefApply.Contains(self)) {
                    justCalledSkinDefApply.Remove(self);
                }
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

        private void Update() {
            if (applyLoadoutToMannequinInstanceOrigs.Count > 0) {
                foreach (var origCall in applyLoadoutToMannequinInstanceOrigs) {
                    origCall();
                }
                applyLoadoutToMannequinInstanceOrigs.Clear();
            }
        }
    }
}
