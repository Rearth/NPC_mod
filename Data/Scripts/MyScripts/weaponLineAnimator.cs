﻿using System;
 using System.Collections.Generic;
 using Sandbox.ModAPI;
 using VRage.Game;
 using VRage.Game.Components;
 using VRage.Utils;
 using VRageMath;
 using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;


 namespace NPCMod {
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class weaponLineAnimator : MySessionComponentBase {

        private static readonly int npc_weapon_anim_duration = 10;
        
        private static List<attackAnimation> activeAnims = new List<attackAnimation>();

        public override void UpdateBeforeSimulation() {
            var toRemove = new List<attackAnimation>();

            foreach (var anim in activeAnims) {
                anim.update();
                if (anim.isOverLifetime()) toRemove.Add(anim);
            }

            foreach (var elem in toRemove) {
                activeAnims.Remove(elem);
            }
        }

        public static void addAnim(Vector3 from, Vector3 to) {
            activeAnims.Add(new attackAnimation(from, to, npc_weapon_anim_duration));
        }

        private class attackAnimation {
            private Vector3 from;
            private Vector3 to;
            private float dist;
            private Vector3 dir;
            private int lifetime = 0;
            private int maxLifeTime;    //in ticks

            public attackAnimation(Vector3 from, Vector3 to, int maxLifeTime) {
                this.from = from;
                this.to = to;
                this.maxLifeTime = maxLifeTime;
                
                dist = (to - from).Length();
                dir = to - from;
                dir.Normalize();

                if (dist < 50) this.maxLifeTime /= 2;
            }

            internal void update() {
                lifetime++;
                var progress = (float) lifetime / maxLifeTime;

                var range = 3f;
                var startFrom = from + dir * progress * dist;
                
                try {
                    var Material = MyStringId.GetOrCompute("NPC_Rifle_Anim");
                    MyTransparentGeometry.AddLineBillboard(Material, Color.White.ToVector4(), startFrom, dir, range, 0.1f, BlendTypeEnum.SDR);
                }
                catch (NullReferenceException ex) {
                    MyLog.Default.Error("Error while drawing billboard: " + ex);
                }
            }

            internal bool isOverLifetime() {
                return this.lifetime > this.maxLifeTime;
            }
        }
    }
}