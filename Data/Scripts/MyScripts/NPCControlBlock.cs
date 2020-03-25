using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Input;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;
using ProtoBuf;

namespace NPCMod {
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_CargoContainer), false, "NPC_Control_block")]
    public class NPCControlBlock : MyGameLogicComponent {
        public enum NPCMode {
            patrol = 0,
            attack = 1,
            stand = 2,
            follow = 3
        }

        private static bool controlsCreated;

        private static readonly Guid SETTINGSGUID = new Guid("98f1332d-83a1-4f7a-9c28-2f9fb1b90b77");

        private IMyCubeBlock block;
        private List<NPCBasicMover> npcsSpawned = new List<NPCBasicMover>();
        private NPCMode selectedMode;
        private Dictionary<int, Vector3> offsets = new Dictionary<int, Vector3>();
        private Dictionary<int, int> patrolProgress = new Dictionary<int, int>();
        private Vector3 initialPatrolRotation;
        private int ticks = 0;

        //delete marker
        private int deleteMarkerAt;
        private string deleteMarkerName;

        public bool recordingPoints;

        //recorded relative to block
        private List<Vector3> patrolPoints = new List<Vector3>();
        private Vector3 standPoint;

        //not-relative
        private Vector3 attackPoint;

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

        public override void UpdateAfterSimulation() {
            foreach (var npc in npcsSpawned) {
                try {
                    npc.doUpdate();
                }
                catch (Exception ex) {
                    MyLog.Default.WriteLine("caught NPC exception: " + ex);
                }
            }
        }

        public override void UpdateBeforeSimulation() {
            ticks++;
            if (ticks == 5) {
                try {
                    loadSettings();
                }
                catch (Exception ex) {
                    MyLog.Default.WriteLine("caught excetion while loading settings: " + ex);
                }
            }

            if (ticks < 6) return;

            updateNPCList();
            updateMode(block);
            updateInput();
            checkDeleteMarker();

            switch (selectedMode) {
                case NPCMode.patrol:
                    updatePatrols(block);
                    break;
                case NPCMode.attack:
                    attackPointOrder(block);
                    break;
                case NPCMode.stand:
                    standAtPoint(block);
                    break;
                case NPCMode.follow:
                    followPlayer(block);
                    break;
            }

            updateTerminalInformation();

            if (ticks % 600 == 0) saveSettings();
        }

        private void standAtPoint(IMyCubeBlock block) {
            foreach (var npc in npcsSpawned) {
                if (npc.getWaypointCount() == 0) {
                    npc.addWaypoint(standPoint, block, initialPatrolRotation);
                }
            }
        }

        private void attackPointOrder(IMyCubeBlock block) {
            foreach (var npc in npcsSpawned) {
                if (npc.getWaypointCount() == 0) {
                    npc.addWaypoint(attackPoint);
                }
            }
        }

        private void updatePatrols(IMyCubeBlock block) {
            if (patrolPoints.Count == 0) return;

            foreach (var npc in npcsSpawned) {
                if (!patrolProgress.ContainsKey(npc.ID))
                    patrolProgress[npc.ID] = 0;

                var progress = patrolProgress[npc.ID];
                var offset = patrolPoints[progress];
                
                npc.clearWaypointsConservingEnemy();
                npc.addWaypoint(offset, this.block, initialPatrolRotation);

                //check if npc is close enough
                var dist = Vector3.Distance(npc.getCurrentWaypoint(), npc.animator.grid.GetPosition());
                if (dist < 2) //point reached
                    progress++;
                progress = progress % patrolPoints.Count;
                patrolProgress[npc.ID] = progress;
            }
        }

        private void followPlayer(IMyCubeBlock myCubeBlock) {
            var playerPos = MyAPIGateway.Session.Player.GetPosition();
            playerPos = NPCBasicMover.toSurfacePos(playerPos, MyAPIGateway.Session.Player.Character, playerPos);
            foreach (var npc in npcsSpawned) {
                var offset = offsets[npc.ID];
                var npcPositionTarget = playerPos + offset;
                if (Vector3.Distance(npc.getCurrentWaypoint(), npcPositionTarget) < 1f) {
                    return;
                }

                npc.clearWaypointsConservingEnemy();
                npc.addWaypoint(npcPositionTarget);
            }
        }

        private void addcustomInfo(IMyTerminalBlock terminalBlock, StringBuilder details) {
            details.Clear();
            var text = readStorage(terminalBlock, TerminalGroupSelector.NPCTEXTGUID);
            details.Append(text);
        }

        private void updateInput() {
            if (recordingPoints && MyAPIGateway.Input.IsNewKeyPressed(MyKeys.U) &&
                MyAPIGateway.Input.IsAnyShiftKeyPressed()) {
                //record new point
                var pos = MyAPIGateway.Session.Player.GetPosition() - block.GetPosition();
                var globalPos = MyAPIGateway.Session.Player.GetPosition();
                switch (selectedMode) {
                    case NPCMode.patrol:
                        patrolPoints.Add(pos);
                        createPatrolPointHUD(globalPos);
                        initialPatrolRotation = block.WorldMatrix.Forward;
                        break;
                    case NPCMode.attack:
                        var tempPoint = getAttackPos();
                        if (tempPoint.Equals(Vector3.Zero)) return;

                        attackPoint = tempPoint;
                        createTempMarker("Attack point", 5, tempPoint);
                        stopRecordingPoints();
                        break;
                    case NPCMode.stand:
                        standPoint = pos;
                        createTempMarker("Guard point", 5, globalPos);
                        stopRecordingPoints();
                        initialPatrolRotation = block.WorldMatrix.Forward;
                        break;
                    case NPCMode.follow:
                        MyLog.Default.WriteLine("invalid state, recorded point in follow mode");
                        break;
                }
            }
        }

        private Vector3 getAttackPos() {
            var playerPos = MyAPIGateway.Session.Player.Character.GetPosition();
            var dir = MyAPIGateway.Session.Player.Character.WorldMatrix.Forward;
            IHitInfo hit;
            var isHit = MyAPIGateway.Physics.CastRay(playerPos, playerPos + dir * 2000f, out hit);
            if (isHit) {
                return hit.Position;
            }

            return Vector3.Zero;
        }

        private void createPatrolPointHUD(Vector3 pos) {
            var count = patrolPoints.Count;
            MyVisualScriptLogicProvider.AddGPS("Patrol Point #" + count, "", pos, Color.Green, 0,
                MyAPIGateway.Session.Player.IdentityId);
        }

        private void createTempMarker(string text, int time, Vector3 pos) {
            deleteMarkerAt = ticks + time * 60;
            deleteMarkerName = text;
            MyVisualScriptLogicProvider.AddGPS(text, "", pos, Color.Green, time,
                MyAPIGateway.Session.Player.IdentityId);
        }

        private void checkDeleteMarker() {
            if (ticks == deleteMarkerAt) {
                MyVisualScriptLogicProvider.RemoveGPS(deleteMarkerName, MyAPIGateway.Session.Player.IdentityId);
            }
        }

        private void resetData() {
            patrolPoints.Clear();
            standPoint = Vector3.Zero;
            attackPoint = Vector3.Zero;
            patrolProgress.Clear();
        }

        public void startRecordingPoints() {
            resetData();
            deleteOldWaypoints(false);

            recordingPoints = true;
            MyVisualScriptLogicProvider.AddGPS("Adding Waypoints", "", block.GetPosition(), Color.Aqua, 0,
                MyAPIGateway.Session.Player.IdentityId);
        }

        public void stopRecordingPoints() {
            recordingPoints = false;
            MyVisualScriptLogicProvider.RemoveGPS("Adding Waypoints", MyAPIGateway.Session.Player.IdentityId);
            for (int i = 0; i < patrolPoints.Count + 1; i++) {
                MyVisualScriptLogicProvider.RemoveGPS("Patrol Point #" + i, MyAPIGateway.Session.Player.IdentityId);
            }

            saveSettings();
        }

        private void updateTerminalInformation() {
            var text = "Settings: ";
            text += "\nMode: " + selectedMode;
            text += "\nNPCs: " + npcsSpawned.Count;
            text += "\nRecording: " + recordingPoints;

            switch (selectedMode) {
                case NPCMode.patrol:
                    text += "\nWaypoint Count: " + patrolPoints.Count;
                    break;
                case NPCMode.stand:
                    if (standPoint == Vector3.Zero) {
                        text += "\nPlease record a point";
                    }
                    else {
                        text += "\nGuarding point";
                    }

                    break;
                case NPCMode.follow:
                    text += "\nFollowing Player: " + MyAPIGateway.Session.Player.DisplayName;
                    break;
                case NPCMode.attack:
                    if (attackPoint == Vector3.Zero) {
                        text += "\nPlease record a point";
                    }
                    else {
                        text += "\nAttacking point";
                    }

                    break;
            }

            writeStorage((IMyTerminalBlock) block, TerminalGroupSelector.NPCTEXTGUID, text);

            ((IMyTerminalBlock) block).RefreshCustomInfo();
            TriggerTerminalRefresh((MyCubeBlock) block);
        }

        public void settingsChanged() {
            saveSettings();
        }

        public void addNPC(NPCBasicMover npc) {
            npcsSpawned.Add(npc);
            var matrix = MyAPIGateway.Session.Player.Character.WorldMatrix;
            var offset = calcOffset(npcsSpawned.Count, matrix.Forward * 2, matrix.Right * 2);
            offsets[npc.ID] = offset;
        }

        private void updateNPCList() {
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

        private void updateMode(IMyCubeBlock block) {
            try {
                var selected = getSelected(block as IMyTerminalBlock);
                var newMode = (NPCMode) (int.Parse(selected));

                if (newMode != selectedMode) {
                    //mode change
                    stopRecordingPoints();
                    resetData();
                    deleteOldWaypoints(newMode == NPCMode.follow);
                }

                selectedMode = newMode;
            }
            catch (Exception ex) {
                // ignored
            }
        }

        private void deleteOldWaypoints(bool walkFast) {
            foreach (var npcBasicMover in npcsSpawned) {
                npcBasicMover.clearWaypointsConservingEnemy();
                npcBasicMover.setWalkFast(walkFast);
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

        private static void TriggerTerminalRefresh(MyCubeBlock block) {
            MyOwnershipShareModeEnum shareMode;
            long ownerId;
            if (block.IDModule != null) {
                ownerId = block.IDModule.Owner;
                shareMode = block.IDModule.ShareMode;
            }
            else {
                return;
            }

            block.ChangeOwner(ownerId,
                shareMode == MyOwnershipShareModeEnum.None
                    ? MyOwnershipShareModeEnum.Faction
                    : MyOwnershipShareModeEnum.None);
            block.ChangeOwner(ownerId, shareMode);
        }

        [ProtoContract]
        public struct serializableNPCData {
            [ProtoMember(1)] public int id;
            [ProtoMember(2)] public Vector3 offset;
            [ProtoMember(3)] public int progress;
        }

        //just to save/load data
        [ProtoContract]
        public class settingsData {
            [ProtoMember(1)] public NPCMode selectedMode;
            [ProtoMember(2)] public List<serializableNPCData> data;
            [ProtoMember(4)] public Vector3 initialPatrolRotation;
            [ProtoMember(5)] public List<Vector3> patrolPoints;
            [ProtoMember(6)] public Vector3 standPoint;
            [ProtoMember(7)] public Vector3 attackPoint;
            [ProtoMember(8)] public List<Vector3> spawnLocations;

            public settingsData() {
            }

            public settingsData(NPCMode selectedMode, Dictionary<int, Vector3> offsets,
                Dictionary<int, int> patrolProgress, Vector3 initialPatrolRotation, List<Vector3> patrolPoints,
                Vector3 standPoint, Vector3 attackPoint, List<Vector3> spawnLocations) {
                this.selectedMode = selectedMode;
                this.initialPatrolRotation = initialPatrolRotation;
                this.patrolPoints = patrolPoints;
                this.standPoint = standPoint;
                this.attackPoint = attackPoint;
                this.spawnLocations = spawnLocations;

                var datas = new List<serializableNPCData>();
                foreach (var elem in offsets) {
                    var item = new serializableNPCData()
                        {id = elem.Key, offset = elem.Value, progress = patrolProgress[elem.Key]};
                    datas.Add(item);
                }

                data = datas;
            }
        }

        private void saveSettings() {
            try {
                var spawnLocations = npcsSpawned.Select(elem => elem.animator.grid.GetPosition())
                    .Select(dummy => (Vector3) dummy).ToList();
                var settings = new settingsData(selectedMode, offsets, patrolProgress, initialPatrolRotation,
                    patrolPoints, standPoint, attackPoint, spawnLocations);
                var text = Convert.ToBase64String(MyAPIGateway.Utilities.SerializeToBinary(settings));

                writeStorage((IMyTerminalBlock) block, SETTINGSGUID, text);
            }
            catch (Exception e) {
                MyLog.Default.WriteLine("caught exception while saving settings: " + e);
            }
        }

        private void loadSettings() {
            MyLog.Default.WriteLine("loading settings");
            var text = readStorage((IMyTerminalBlock) block, SETTINGSGUID);
            if (text.Length <= 1) return;
            var res = MyAPIGateway.Utilities.SerializeFromBinary<settingsData>(Convert.FromBase64String(text));

            selectedMode = res.selectedMode;
            initialPatrolRotation = res.initialPatrolRotation;
            if (res.patrolPoints != null)
                patrolPoints = res.patrolPoints;
            standPoint = res.standPoint;
            attackPoint = res.attackPoint;

            foreach (var data in res.data) {
                offsets[data.id] = data.offset;
                patrolProgress[data.id] = data.progress;
            }

            foreach (var point in res.spawnLocations) {
                createNPCFromSave(point);
            }

            block.GameLogic.GetAs<NPCControlBlock>()?.settingsChanged();
        }

        private void createNPCFromSave(Vector3 pos) {
            var color = block.GetDiffuseColor();
            var entity = MainNPCLoop.spawnNPC(block.OwnerId, color, pos);

            var gridBlocks = new List<IMySlimBlock>();
            (entity as IMyCubeGrid)?.GetBlocks(gridBlocks);
            var npc = NPCBasicMover.getEngineer(gridBlocks.First());
            block.GameLogic.GetAs<NPCControlBlock>()?.addNPC(npc);
        }
    }
}