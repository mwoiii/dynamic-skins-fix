using BepInEx;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using System.Diagnostics;
using System.Reflection;
using UnityEngine;
using UnityEngine.PlayerLoop;

namespace DynamicSkinsFix
{

    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]

    public class DynamicSkinsFix : BaseUnityPlugin
    {
        public const string PluginGUID = PluginAuthor + "." + PluginName;
        public const string PluginAuthor = "Miyowi";
        public const string PluginName = "DynamicSkinsFix";
        public const string PluginVersion = "1.0.0";

        private System.Random random = new System.Random();

        private static int callCount;

        private const int callLimit = 50;

        public void Awake()
        {
            Log.Init(Logger);

            IL.RoR2.SkinDef.ApplyAsync += CallObsolete;
            IL.RoR2.SkinDef.Apply += ObsoleteRet;
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

        private void FixedUpdate() {
            callCount = 0;
        }
    }
}
