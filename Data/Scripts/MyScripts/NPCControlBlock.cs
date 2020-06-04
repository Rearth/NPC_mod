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
using VRage;
using VRage.Game.ModAPI.Ingame;
using IMyCubeBlock = VRage.Game.ModAPI.IMyCubeBlock;
using IMyCubeGrid = VRage.Game.ModAPI.IMyCubeGrid;
using IMyEntity = VRage.ModAPI.IMyEntity;
using IMySlimBlock = VRage.Game.ModAPI.IMySlimBlock;

namespace NPCMod {
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_CargoContainer), false, "NPC_Control_block",
        "NPC_Control_elite")]
    public class NPCControlBlock : MyGameLogicComponent {
        public enum NPCMode {
            patrol = 0,
            attack = 1,
            stand = 2,
            follow = 3
        }


        private static readonly Guid SETTINGSGUID = new Guid("98f1332d-83a1-4f7a-9c28-2f9fb1b90b77");
        public static readonly int maxcount = 10;

        private static bool controlsCreated;

        private IMyCubeBlock block;
        private List<NPCBasicMover> npcsSpawned = new List<NPCBasicMover>();
        private NPCMode selectedMode;
        private Dictionary<int, Vector3> offsets = new Dictionary<int, Vector3>();
        private Dictionary<int, int> patrolProgress = new Dictionary<int, int>();
        private int ticks;
        private int targetCount;
        private int spawnProgress;
        private string npc_subtype;

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
                //addStoreTrades();
            }

            var terminalBlock = ((IMyTerminalBlock) block);
            if (terminalBlock != null) terminalBlock.AppendingCustomInfo += addcustomInfo;
            if (block.BlockDefinition.SubtypeId.Equals("NPC_Control_block")) npc_subtype = "NPC_Basic";
            if (block.BlockDefinition.SubtypeId.Equals("NPC_Control_elite")) npc_subtype = "NPC_Elite";
        }

//        private void addStoreTrades() {
//            MyLog.Default.WriteLine("adding store trades");
//            
//            var entitiesFound = new HashSet<IMyEntity>();
//            MyAPIGateway.Entities.GetEntities(entitiesFound, entity => entity.GetType() == typeof(MyCubeGrid));
//
//            foreach (var entity in entitiesFound) {
//                MyLog.Default.WriteLine("found grid: " + entity);
//                var grid = entity as IMyCubeGrid;
//                var blocks = new List<IMySlimBlock>();
//
//                grid?.GetBlocks(blocks, block => block.BlockDefinition.Id.SubtypeName.Equals("StoreBlock"));
//
//                foreach (var storeBlock in blocks) {
//                    MyLog.Default.WriteLine("found store: " + storeBlock);
//                    var store = storeBlock.FatBlock as IMyStoreBlock;
//                    if (store == null) continue;
//                    long id;
//                    var definition = MyDefinitionId.Parse("MyObjectBuilder_Component/NPC_Token_Basic");
//                    var res = store.InsertOffer(
//                        new MyStoreItemDataSimple(definition, 5, 5000), out id);
//                    MyLog.Default.WriteLine(res.ToString());
//                }
//            }
//        }

        public override void UpdateAfterSimulation() {
            //addStoreTrades();
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

            updateNPCSpawn();
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
                    NPCBasicMover.drawDebugLine(this.block.WorldMatrix.Translation, localToWorldPos(block, standPoint),
                        Color.Gold);
                    break;
                case NPCMode.follow:
                    followPlayer(block);
                    break;
            }

            updateTerminalInformation();

            if (ticks % 600 == 0) saveSettings();
        }

        private void updateNPCSpawn() {
            var curCount = npcsSpawned.Count;
            spawnProgress += 1;
            if (curCount < targetCount && spawnProgress > 200) {
                var spawnAt = block.GetPosition();
                spawnAt += block.WorldMatrix.Up * 2;
                createNPCatPos(spawnAt, npc_subtype);
                spawnProgress = 0;
            }
        }

        private void standAtPoint(IMyCubeBlock block) {
            foreach (var npc in npcsSpawned) {
                if (npc.getWaypointCount() == 0) {
                    npc.addWaypoint(standPoint, block);
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
                var first = patrolPoints[progress];

                npc.clearWaypointsConservingEnemy();
                npc.addWaypoint(first, this.block);

                //check if npc is close enough
                var dist = Vector3.Distance(localToWorldPos(block, first), npc.animator.grid.GetPosition());

                if (dist < 1.8f || npc.wasStuck) {
                    //point reached
                    progress++;
                    npc.wasStuck = false;
                    npc.stuckTimer = 0;
                }

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
                var globalPos = MyAPIGateway.Session.Player.GetPosition();
                var pos = worldToLocalPos(block, globalPos);
                switch (selectedMode) {
                    case NPCMode.patrol:
                        patrolPoints.Add(pos);
                        createPatrolPointHUD(globalPos);
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
                        break;
                    case NPCMode.follow:
                        MyLog.Default.WriteLine("invalid state, recorded point in follow mode");
                        break;
                }
            }
        }

        public static Vector3 worldToLocalPos(IMyCubeBlock referenceBlock, Vector3 worldPosition) {
            Vector3D referenceWorldPosition = referenceBlock.WorldMatrix.Translation;
            Vector3D worldDirection = worldPosition - referenceWorldPosition;
            return Vector3D.TransformNormal(worldDirection,
                MatrixD.Transpose(referenceBlock.WorldMatrix)); //note that we transpose to go from world -> body
        }

        public static Vector3 localToWorldPos(IMyCubeBlock referenceBlock, Vector3 localPosition) {
            return Vector3D.Transform(localPosition, referenceBlock.WorldMatrix);
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
            text += "\nTarget Count: " + targetCount;
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

        private void addNPC(NPCBasicMover npc) {
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

        public string getTokenString() {
            if (npc_subtype.Equals("NPC_Basic")) {
                return "NPC_Token_Basic";
            }
            else {
                return "NPC_Token_Elite";
            }
        }

        public bool isAtLimit() {
            return targetCount >= maxcount;
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
            [ProtoMember(3)] public List<Vector3> patrolPoints;
            [ProtoMember(4)] public Vector3 standPoint;
            [ProtoMember(5)] public Vector3 attackPoint;
            [ProtoMember(6)] public List<Vector3> spawnLocations;
            [ProtoMember(7)] public int targetCount;

            public settingsData() {
            }

            public settingsData(NPCMode selectedMode, Dictionary<int, Vector3> offsets,
                Dictionary<int, int> patrolProgress, List<Vector3> patrolPoints,
                Vector3 standPoint, Vector3 attackPoint, List<Vector3> spawnLocations, int targetCount) {
                this.selectedMode = selectedMode;
                this.patrolPoints = patrolPoints;
                this.standPoint = standPoint;
                this.attackPoint = attackPoint;
                this.spawnLocations = spawnLocations;
                this.targetCount = targetCount;

                var datas = new List<serializableNPCData>();
                foreach (var elem in offsets) {
                    int progress;
                    patrolProgress.TryGetValue(elem.Key, out progress);
                    var item = new serializableNPCData()
                        {id = elem.Key, offset = elem.Value, progress = progress};
                    datas.Add(item);
                }

                data = datas;
            }
        }

        private void saveSettings() {
            try {
                var spawnLocations = npcsSpawned.Select(elem => elem.animator.grid.GetPosition())
                    .Select(dummy => (Vector3) dummy).ToList();
                var settings = new settingsData(selectedMode, offsets, patrolProgress,
                    patrolPoints, standPoint, attackPoint, spawnLocations, targetCount);
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
            if (res.patrolPoints != null)
                patrolPoints = res.patrolPoints;
            standPoint = res.standPoint;
            attackPoint = res.attackPoint;
            targetCount = res.targetCount;

            foreach (var data in res.data) {
                offsets[data.id] = data.offset;
                patrolProgress[data.id] = data.progress;
            }

            foreach (var point in res.spawnLocations) {
                createNPCatPos(point, npc_subtype);
            }

            block.GameLogic.GetAs<NPCControlBlock>()?.settingsChanged();
        }

        private void createNPCatPos(Vector3 pos, string subtype) {
            var color = block.SlimBlock.ColorMaskHSV;
            var entity = MainNPCLoop.spawnNPC(block.OwnerId, color, pos, subtype);

            var gridBlocks = new List<IMySlimBlock>();
            (entity as IMyCubeGrid)?.GetBlocks(gridBlocks);
            NPCBasicMover npc = null;
            switch (subtype) {
                case "NPC_Basic":
                    npc = NPCBasicMover.getEngineer(gridBlocks.First());
                    break;
                case "NPC_Elite":
                    npc = NPCBasicMover.getElite(gridBlocks.First());
                    break;
            }

            block.GameLogic.GetAs<NPCControlBlock>()?.addNPC(npc);
        }

        public void spawnNPCTriggered() {
            targetCount++;
            var subtype = getTokenString();
            var item = block.GetInventory().FindItem(new MyItemType("MyObjectBuilder_Component", subtype));
            var item2 = block.GetInventory().GetItemByID(item.Value.ItemId);
            MyLog.Default.WriteLine("data: " + item2.Content.TypeId + " | " + item2.Content.SubtypeName);

            block.GetInventory().RemoveItemAmount(item2, (MyFixedPoint) 1f);
        }
    }
}