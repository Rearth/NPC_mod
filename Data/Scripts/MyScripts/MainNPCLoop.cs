using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Ingame;
using VRage.Library.Utils;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;
using IMyCubeGrid = VRage.Game.ModAPI.IMyCubeGrid;
using IMyEntity = VRage.ModAPI.IMyEntity;
using IMySlimBlock = VRage.Game.ModAPI.IMySlimBlock;

namespace NPCMod {
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class MainNPCLoop : MySessionComponentBase {
        public static int ticks = 0;

        public static bool DEBUG = false;

        public static IMyEntity spawnNPC(long owner, Vector3 color, Vector3 position, string subTypeID) {
            var id = MyRandom.Instance.Next(100, 1000000);
            var entity =
                NPCGridUtilities.SpawnBlock(subTypeID, "npc_" + id, color, true, true, false, true, true, owner) as
                    IMyCubeGrid;

            if (entity != null) {
                var matrix = entity.WorldMatrix;
                matrix.Translation = position;
                entity.WorldMatrix = matrix;
                entity.ChangeGridOwnership(owner, MyOwnershipShareModeEnum.None);
            }

            return entity;
        }

        public override void UpdateAfterSimulation() {
            ticks++;
        }
    }
}