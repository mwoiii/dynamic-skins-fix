using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DynamicSkinsFix {
    public static class Utils {

        public static void WaitForCompletion(this IEnumerator coroutine, int callLimit = int.MaxValue) {
            while (callLimit > 0 && coroutine.MoveNext()) {
                callLimit--;
            }
        }

        public static bool IsIdenticalTo(this Matrix4x4[] bindposes1, Matrix4x4[] bindposes2) {
            if (bindposes1.Length != bindposes2.Length) {
                return false;
            }

            for (int i = 0; i < bindposes1.Length; i++) {
                if (bindposes1[i] != bindposes2[i]) {
                    return false;
                }
            }

            return true;
        }
    }
}
