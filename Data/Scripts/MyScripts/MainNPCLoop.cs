﻿﻿using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace NPCMod {
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class MainNPCLoop : MySessionComponentBase {
        private Boolean inited;
        private List<NPCBasicMover> npcBasicMovers = new List<NPCBasicMover>();
        private List<NPCBasicMover> waiting = new List<NPCBasicMover>();

        public static int ticks = 0;

        private static readonly float moveSpeed = 4f;
        private static readonly int npcWeaponRange = 80;
        private static readonly float npcWeaponDamage = 1;
        private static readonly float npcAttacksPerSecond = 0.5f;
        
        public static bool DEBUG = false;
        

        private void init() {
            MyLog.Default.WriteLine("initializing movement animator, getting all npcs");

            inited = true;
            var npcs = getAllNPCs();

            MyLog.Default.WriteLine("found NPCs: " + npcs.Count);

            foreach (var npc in npcs) {
                try {
                    if (existsAlready(npc.CubeGrid)) continue;

                    var npcDataAnimator = new NPCDataAnimator(npc.CubeGrid, npc, moveSpeed);
                    var basicMover = new NPCBasicMover(npcDataAnimator, npcWeaponRange, npcWeaponDamage,
                        npcAttacksPerSecond);
                    npc.CubeGrid.Physics.Friction = 1f;

                    waiting.Add(basicMover);
                }
                catch (KeyNotFoundException ex) {
                    MyLog.Default.WriteLine("error: " + ex);
                }
            }
        }

        public override void UpdateAfterSimulation() {
            try {
                if (!inited) {
                    init();
                    return;
                }

                ticks++;
                if (ticks % 240 == 0) {
                    init();
                }

                if (ticks % 3 == 0 && waiting.Count > 0) {
                    npcBasicMovers.Add(waiting[0]);
                    waiting.Remove(waiting[0]);
                }

                IMyEntity targetEntity;
                var target = findTargetBeacon(out targetEntity);

                var toRemove = new List<NPCBasicMover>();

                foreach (var npcBasicMover in npcBasicMovers) {
                    //remove invalids
                    if (!npcBasicMover.isValid()) {
                        toRemove.Add(npcBasicMover);
                        continue;
                    }

                    npcBasicMover.MovementTarget = target;
                    npcBasicMover.ActiveEnemy = targetEntity;
                    npcBasicMover.doUpdate();
                }

                foreach (var elem in toRemove) {
                    npcBasicMovers.Remove(elem);
                }
            }

            catch (Exception ex) {
                MyLog.Default.WriteLine("caught exception in main loop: " + ex);
            }
        }

        private bool existsAlready(IMyCubeGrid grid) {
            foreach (var elem in npcBasicMovers) {
                if (elem.animator.grid.Equals(grid)) {
                    return true;
                }
            }

            return false;
        }

        private Vector3 findTargetBeacon(out IMyEntity targetEntity) {
            var entitiesFound = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(entitiesFound, entity => entity.GetType() == typeof(MyCubeGrid));

            foreach (var entity in entitiesFound) {
                var grid = entity as IMyCubeGrid;
                if (grid != null && grid.CustomName.Equals("target")) {
                    List<IHitInfo> hits = new List<IHitInfo>();
                    //MyPhysics.CollisionLayers.VoxelCollisionLayer = 28
                    MyAPIGateway.Physics.CastRay(grid.WorldMatrix.Translation, grid.Physics.Gravity * 15f, hits, 13);
                    targetEntity = entity;
                    if (hits.Count > 0) {
                        return hits[0].Position;
                    }

                    return grid.WorldMatrix.Translation;
                }
            }

            targetEntity = null;
            return Vector3.Zero;
        }

        private List<IMySlimBlock> getAllNPCs() {
            var res = new List<IMySlimBlock>();

            var entitiesFound = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(entitiesFound, entity => entity.GetType() == typeof(MyCubeGrid));

            foreach (var entity in entitiesFound) {
                var grid = entity as IMyCubeGrid;
                var blocks = new List<IMySlimBlock>();
                var blocksAll = new List<IMySlimBlock>();

                grid?.GetBlocks(blocks, block => block.BlockDefinition.Id.SubtypeId.String == "NPC_Test");
                grid?.GetBlocks(blocksAll);

                if (blocksAll.Count != 1) continue;
                foreach (var block in blocks) {
                    res.Add(block);
                }
            }

            return res;
        }
    }
}