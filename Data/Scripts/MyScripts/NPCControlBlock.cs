using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;
using IMySlimBlock = VRage.Game.ModAPI.Ingame.IMySlimBlock;

namespace NPCMod {
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_CargoContainer), false, "NPC_Control_block")]
    public class NPCControlBlock : MyGameLogicComponent {
        public enum NPCMode {
            patrol = 0,
            attack = 1,
            stand = 2,
            follow = 3
        }

        private static bool controlsCreated = false;

        private IMyCubeBlock block;
        private List<NPCBasicMover> npcsSpawned = new List<NPCBasicMover>();
        private NPCMode selectedMode;
        private Dictionary<NPCBasicMover, Vector3> offsets = new Dictionary<NPCBasicMover, Vector3>();

        public static Dictionary<long, NPCBasicMover> queuedNPCs = new Dictionary<long, NPCBasicMover>();

        public override void Init(MyObjectBuilder_EntityBase objectBuilder) {
            MyLog.Default.WriteLine("executing init for npc control block");
            NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

        public override void UpdateOnceBeforeFrame() {
            MyLog.Default.WriteLine("executing once before frame for npc control block!");
            block = Entity as IMyCargoContainer;
            NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME;

            if (!controlsCreated) {
                TerminalGroupSelector.createControls();
                controlsCreated = true;
            }
            
            var terminalBlock = ((IMyTerminalBlock) block);
            if (terminalBlock != null) terminalBlock.AppendingCustomInfo += addcustomInfo;
        }

        private void addcustomInfo(IMyTerminalBlock terminalBlock, StringBuilder details) {
            details.Clear();
            var text = readStorage(terminalBlock, TerminalGroupSelector.NPCTEXTGUID);
            details.Append("NPC data:");
            details.Append(text);
        }

        public override void UpdateBeforeSimulation() {
            addNPC();
            updateMode(block);

            if (selectedMode == NPCMode.follow) {
                followPlayer(block);
            }

            updateTerminalInformation();
        }

        public override void UpdateAfterSimulation() {
            foreach (var npc in npcsSpawned) {
                npc.doUpdate();
            }
        }

        private void updateTerminalInformation() {
            var text = "Settings: ";
            text += "\nMode: " + selectedMode;
            text += "\nNPCs: " + npcsSpawned.Count;
            writeStorage((IMyTerminalBlock) block, TerminalGroupSelector.NPCTEXTGUID, text);

            ((IMyTerminalBlock) block).RefreshCustomInfo();
        }

        private void addNPC() {
            NPCBasicMover npc;
            if (queuedNPCs.TryGetValue(block.EntityId, out npc) && !npcsSpawned.Contains(npc)) {
                npcsSpawned.Add(npc);
                queuedNPCs.Remove(block.EntityId);
                var matrix = MyAPIGateway.Session.Player.Character.WorldMatrix;
                var offset = calcOffset(npcsSpawned.Count, matrix.Forward * 2, matrix.Right * 2);
                offsets[npc] = offset;
            }

            var toRemove = new List<NPCBasicMover>();
            foreach (var elem in npcsSpawned) {
                if (!elem.isValid()) toRemove.Add(elem);
            }

            foreach (var elem in toRemove) {
                npcsSpawned.Remove(elem);
            }
        }

        private Vector3 calcOffset(int index, Vector3 forward, Vector3 right) {
            var xOffset = index % 10;
            if (index % 2 == 0) xOffset = -xOffset;
            var yOffset = index / 10;
            if (index / 10 % 2 == 0) yOffset = -yOffset;

            var offset = right * xOffset + forward * yOffset;
            return offset;
        }

        private void followPlayer(IMyCubeBlock myCubeBlock) {
            var playerPos = MyAPIGateway.Session.Player.GetPosition();
            playerPos = NPCBasicMover.toSurfacePos(playerPos, MyAPIGateway.Session.Player.Character, playerPos);
            foreach (var npc in npcsSpawned) {
                var offset = offsets[npc];
                var npcPositionTarget = playerPos + offset;
                npc.clearWaypointsConservingEnemy();
                npc.addWaypoint(npcPositionTarget);
            }
        }

        private void updateMode(IMyCubeBlock block) {
            try {
                var selected = getSelected(block as IMyTerminalBlock);
                selectedMode = (NPCMode) (int.Parse(selected));
            }
            catch (Exception ex) {
                // ignored
            }
        }

        public static String readStorage(IMyTerminalBlock block, Guid key) {
            if (block.Storage == null) {
                block.Storage = new MyModStorageComponent();
            }

            string val;
            if (block.Storage.TryGetValue(key, out val))
                return val;
            return "";
        }

        public static void writeStorage(IMyTerminalBlock block, Guid key, string val) {
            if (block.Storage == null) {
                block.Storage = new MyModStorageComponent();
            }

            block.Storage[key] = val;
        }

        private static String getSelected(IMyTerminalBlock block) {
            return readStorage(block, TerminalGroupSelector.NPCMODEGUID);
        }
    }
}