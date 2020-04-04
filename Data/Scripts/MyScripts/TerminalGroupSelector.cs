using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.Screens.Helpers;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Input;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using VRageRender;

namespace NPCMod {
    public static class TerminalGroupSelector {
        public static readonly Guid NPCMODEGUID = new Guid("e7a2205f-f66e-44c0-925a-1173bb0d3853");
        public static readonly Guid NPCTEXTGUID = new Guid("4c9d472d-e28a-4f14-b486-0a08b0b64600");

        //controls needed:
        //mode selection (Patrol points/attack point/stand guard/follow player)
        //select/add/remove patrol points
        //set attack point
        //---- Spawning ----
        //Train basic npc
        //Train elite npc (later)
        //Train sniper npc (later)

        //also:
        //display current NPCs/target number
        //display training process

        public static void createControls() {
            MyLog.Default.WriteLine("creating controls for npc controller");

            var groupSelection = MyAPIGateway.TerminalControls
                .CreateControl<IMyTerminalControlCombobox, IMyCargoContainer>(
                    "NPC_Control_block");
            groupSelection.Title = MyStringId.GetOrCompute("NPC Mode:");
            groupSelection.ComboBoxContent = createListModeSelect;
            groupSelection.Setter = (block, id) => OnItemSelected(block, id.ToString());
            groupSelection.Getter = getSelectedID;
            groupSelection.Tooltip = MyStringId.GetOrCompute("TODO");
            MyAPIGateway.TerminalControls.AddControl<IMyCargoContainer>(groupSelection);

            var spawnButton =
                MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyCargoContainer>(
                    "NPC_Control_block");
            spawnButton.Title = MyStringId.GetOrCompute("Spawn NPC");
            spawnButton.Action = OnSpawnClicked;

            var recordPoints =
                MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyCargoContainer>(
                    "NPC_Control_block");
            recordPoints.Title = MyStringId.GetOrCompute("Record Points");
            recordPoints.Action = OnRecordPoints;
            recordPoints.Enabled = startRecordEnabled;

            var stopRecord =
                MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyCargoContainer>(
                    "NPC_Control_block");
            stopRecord.Title = MyStringId.GetOrCompute("Stop recording");
            stopRecord.Action = OnStopRecord;
            stopRecord.Enabled = stopRecordEnabled;

            var spawnAction = MyAPIGateway.TerminalControls.CreateAction<IMyCargoContainer>("NPC_Control_block");
            spawnAction.Name = new StringBuilder("Spawn NPC");
            spawnAction.Action = OnSpawnClicked;

            MyAPIGateway.TerminalControls.AddControl<IMyCargoContainer>(spawnButton);
            MyAPIGateway.TerminalControls.AddControl<IMyCargoContainer>(recordPoints);
            MyAPIGateway.TerminalControls.AddControl<IMyCargoContainer>(stopRecord);
            MyAPIGateway.TerminalControls.AddAction<IMyCargoContainer>(spawnAction);
        }

        private static bool stopRecordEnabled(IMyTerminalBlock block) {
            var state = block.GameLogic.GetAs<NPCControlBlock>()?.recordingPoints;
            return state != null && (bool) state;
        }

        private static bool startRecordEnabled(IMyTerminalBlock block) {
            var state = !block.GameLogic.GetAs<NPCControlBlock>()?.recordingPoints;
            return state != null && (bool) state;
        }

        private static void OnStopRecord(IMyTerminalBlock block) {
            block.GameLogic.GetAs<NPCControlBlock>()?.stopRecordingPoints();
            block.GameLogic.GetAs<NPCControlBlock>()?.settingsChanged();
        }

        private static void OnRecordPoints(IMyTerminalBlock block) {
            block.GameLogic.GetAs<NPCControlBlock>()?.startRecordingPoints();
            block.GameLogic.GetAs<NPCControlBlock>()?.settingsChanged();
        }

        private static void OnSpawnClicked(IMyTerminalBlock block) {
            
            block.GameLogic.GetAs<NPCControlBlock>()?.spawnNPCTriggered();
        }

        private static long getSelectedID(IMyTerminalBlock block) {
            if (block.Storage == null) {
                block.Storage = new MyModStorageComponent();
            }

            string val;
            long id;
            if (block.Storage.TryGetValue(NPCMODEGUID, out val) && long.TryParse(val, out id))
                return id;
            return 0;
        }

        private static void createListModeSelect(List<MyTerminalControlComboBoxItem> obj) {
            obj.Add(new MyTerminalControlComboBoxItem() {Key = 0L, Value = MyStringId.GetOrCompute("Patrol")});
            obj.Add(new MyTerminalControlComboBoxItem() {Key = 1L, Value = MyStringId.GetOrCompute("Attack point")});
            obj.Add(new MyTerminalControlComboBoxItem() {Key = 2L, Value = MyStringId.GetOrCompute("Stand guard")});
            obj.Add(new MyTerminalControlComboBoxItem() {Key = 3L, Value = MyStringId.GetOrCompute("Follow player")});
        }

        private static void OnItemSelected(IMyTerminalBlock block, string selected) {
            MyLog.Default.WriteLine("selected: " + selected + " on " + block);
            NPCControlBlock.writeStorage(block, NPCMODEGUID, selected);
            block.GameLogic.GetAs<NPCControlBlock>()?.settingsChanged();
        }

        private static String getSelected(IMyTerminalBlock block) {
            return NPCControlBlock.readStorage(block, NPCMODEGUID);
        }

        private static void FillGroupList(List<MyTerminalControlListBoxItem> itemList,
            List<MyTerminalControlListBoxItem> selectedItemList, IMyTerminalBlock myTerminalBlock) {
            var patrol = new MyTerminalControlListBoxItem(MyStringId.GetOrCompute("Patrol"),
                MyStringId.GetOrCompute("Orders the mercenaries to patrol between the below defined points"), null);
            var attackPoint = new MyTerminalControlListBoxItem(MyStringId.GetOrCompute("Attack Point"),
                MyStringId.GetOrCompute("Orders the mercenaries to attack a specific point"), null);
            var standGuard = new MyTerminalControlListBoxItem(MyStringId.GetOrCompute("Hold Position"),
                MyStringId.GetOrCompute("Orders the mercenaries to stand guard at a specific position"), null);

            itemList.Add(patrol);
            itemList.Add(attackPoint);
            itemList.Add(standGuard);

            var selected = getSelected(myTerminalBlock);
            MyLog.Default.WriteLine("found selected val: " + selected);
            if (selected.Equals(patrol.Text.String)) selectedItemList.Add(patrol);
            else if (selected.Equals(attackPoint.Text.String)) selectedItemList.Add(attackPoint);
            else if (selected.Equals(standGuard.Text.String)) selectedItemList.Add(standGuard);

            if (selectedItemList.Count < 1) {
                selectedItemList.Add(standGuard);
                OnItemSelected(myTerminalBlock, standGuard.Text.String);
            }
        }

        public static Vector3 HsvToRgb(Vector3 hsv) {
            float num1 = hsv.X * 360f;
            float y = hsv.Y;
            double z = (double) hsv.Z;
            int num2 = Convert.ToInt32(Math.Floor((double) num1 / 60.0)) % 6;
            float num3 = (float) ((double) num1 / 60.0 - Math.Floor((double) num1 / 60.0));
            float num4 = (float) z;
            float num5 = (float) (z * (1.0 - (double) y));
            float num6 = (float) (z * (1.0 - (double) num3 * (double) y));
            float num7 = (float) (z * (1.0 - (1.0 - (double) num3) * (double) y));
            switch (num2) {
                case 0:
                    return new Vector3(num4, num7, num5);
                case 1:
                    return new Vector3(num6, num4, num5);
                case 2:
                    return new Vector3(num5, num4, num7);
                case 3:
                    return new Vector3(num5, num6, num4);
                case 4:
                    return new Vector3(num7, num5, num4);
                default:
                    return new Vector3(num4, num5, num6);
            }
        }
    }
}