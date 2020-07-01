using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage;
using VRage.Game.ModAPI.Ingame;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

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

            createTerminalActions();

            var activeSelection = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlOnOffSwitch, IMyCargoContainer>("NPC_Control_block");
            activeSelection.Title = MyStringId.GetOrCompute("Active: ");
            activeSelection.OnText = MyStringId.GetOrCompute("On");
            activeSelection.OffText = MyStringId.GetOrCompute("Off");
            activeSelection.Getter = getIsActive;
            activeSelection.Setter = setBlockActive;
            activeSelection.Visible = isNPCControlBlock;

            var groupSelection = MyAPIGateway.TerminalControls
                .CreateControl<IMyTerminalControlCombobox, IMyCargoContainer>(
                    "NPC_Control_block");
            groupSelection.Title = MyStringId.GetOrCompute("NPC Mode:");
            groupSelection.ComboBoxContent = createListModeSelect;
            groupSelection.Setter = (block, id) => OnItemSelected(block, id.ToString());
            groupSelection.Getter = getSelectedID;
            groupSelection.Tooltip = MyStringId.GetOrCompute("TODO");
            groupSelection.Visible = isNPCControlBlock;

            var spawnButton =
                MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyCargoContainer>(
                    "NPC_Control_block");
            spawnButton.Title = MyStringId.GetOrCompute("Spawn NPC");
            spawnButton.Action = OnSpawnClicked;
            spawnButton.Enabled = spawnEnabled;
            spawnButton.Visible = isNPCControlBlock;
            spawnButton.Tooltip = MyStringId.GetOrCompute("Requires contract tokens, purchasable at trade stations");

            var recordPoints =
                MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyCargoContainer>(
                    "NPC_Control_block");
            recordPoints.Title = MyStringId.GetOrCompute("Record Points");
            recordPoints.Action = OnRecordPoints;
            recordPoints.Enabled = startRecordEnabled;
            recordPoints.Visible = isNPCControlBlock;

            var stopRecord =
                MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyCargoContainer>(
                    "NPC_Control_block");
            stopRecord.Title = MyStringId.GetOrCompute("Stop recording");
            stopRecord.Action = OnStopRecord;
            stopRecord.Enabled = stopRecordEnabled;
            stopRecord.Visible = isNPCControlBlock;
            
            MyAPIGateway.TerminalControls.AddControl<IMyCargoContainer>(activeSelection);
            MyAPIGateway.TerminalControls.AddControl<IMyCargoContainer>(groupSelection);
            MyAPIGateway.TerminalControls.AddControl<IMyCargoContainer>(spawnButton);
            MyAPIGateway.TerminalControls.AddControl<IMyCargoContainer>(recordPoints);
            MyAPIGateway.TerminalControls.AddControl<IMyCargoContainer>(stopRecord);
        }

        private static void createTerminalActions() {
            var activeAction = MyAPIGateway.TerminalControls.CreateAction<IMyCargoContainer>("NPC_Control_block_toggle_active_toggle");
            activeAction.Name = new StringBuilder("Toggle Active");
            activeAction.ValidForGroups = true;
            activeAction.Action = toggleBlockActive;
            activeAction.Enabled = isNPCControlBlock;
            activeAction.Icon = "Textures\\GUI\\Icons\\Actions\\Toggle.dds";
            activeAction.Writer = getActiveText;

            var activeActionOn = MyAPIGateway.TerminalControls.CreateAction<IMyCargoContainer>("NPC_Control_block_toggle_active_on");
            activeActionOn.Name = new StringBuilder("Activate");
            activeActionOn.ValidForGroups = true;
            activeActionOn.Action = block => setBlockActive(block, true);
            activeActionOn.Enabled = isNPCControlBlock;
            activeActionOn.Icon = "Textures\\GUI\\Icons\\Actions\\SwitchOn.dds";
            activeActionOn.Writer = getActiveText;

            var activeActionOff = MyAPIGateway.TerminalControls.CreateAction<IMyCargoContainer>("NPC_Control_block_toggle_active_off");
            activeActionOff.Name = new StringBuilder("Deactivate");
            activeActionOff.ValidForGroups = true;
            activeActionOff.Action = block => setBlockActive(block, false);
            activeActionOff.Enabled = isNPCControlBlock;
            activeActionOff.Icon = "Textures\\GUI\\Icons\\Actions\\SwitchOff.dds";
            activeActionOff.Writer = getActiveText;

            var spawnAction = MyAPIGateway.TerminalControls.CreateAction<IMyCargoContainer>("NPC_Control_block_spawn");
            spawnAction.Name = new StringBuilder("Recruit NPC");
            spawnAction.ValidForGroups = true;
            spawnAction.Action = OnSpawnClicked;
            spawnAction.Enabled = block => isNPCControlBlock(block) && spawnEnabled(block);
            spawnAction.Icon = "Textures\\GUI\\Icons\\Actions\\CharacterToggle.dds";

            MyAPIGateway.TerminalControls.AddAction<IMyCargoContainer>(activeAction);
            MyAPIGateway.TerminalControls.AddAction<IMyCargoContainer>(activeActionOn);
            MyAPIGateway.TerminalControls.AddAction<IMyCargoContainer>(activeActionOff);
            MyAPIGateway.TerminalControls.AddAction<IMyCargoContainer>(spawnAction);
        }

        private static void getActiveText(IMyTerminalBlock arg, StringBuilder builder) {
            var control = arg.GameLogic.GetAs<NPCControlBlock>();
            builder.Append(control.isActive ? "On" : "Off");
        }

        private static void toggleBlockActive(IMyTerminalBlock arg) {
            var control = arg.GameLogic.GetAs<NPCControlBlock>();
            control.isActive = !control.isActive;
            control.settingsChanged();
        }

        private static bool isNPCControlBlock(IMyTerminalBlock arg) {
            
            var control = arg.GameLogic.GetAs<NPCControlBlock>();
            return control != null;
        }

        private static void setBlockActive(IMyTerminalBlock arg, bool enabled) {
            var control = arg.GameLogic.GetAs<NPCControlBlock>();
            control.isActive = enabled;
            control.settingsChanged();
        }

        private static bool getIsActive(IMyTerminalBlock arg) {
            var control = arg.GameLogic.GetAs<NPCControlBlock>();
            return control != null && control.isActive;
        }

        private static bool spawnEnabled(IMyTerminalBlock arg) {

            if (!isNPCControlBlock(arg)) return false;

            var control = arg.GameLogic.GetAs<NPCControlBlock>();

            if (control.isAtLimit()) return false;
            
            var tokenString = control.getTokenString();
            return arg.GetInventory().ContainItems((MyFixedPoint) 1f, new MyItemType("MyObjectBuilder_Component", tokenString));
        }

        private static bool stopRecordEnabled(IMyTerminalBlock block) {

            if (!isNPCControlBlock(block)) return false;
            var state = block.GameLogic.GetAs<NPCControlBlock>()?.recordingPoints;
            return state != null && (bool) state;
        }

        private static bool startRecordEnabled(IMyTerminalBlock block) {
            if (!isNPCControlBlock(block)) return false;
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

        public static void OnItemSelected(IMyTerminalBlock block, string selected) {
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
    }
}