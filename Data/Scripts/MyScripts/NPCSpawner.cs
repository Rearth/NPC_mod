using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.Entity;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace NPCMod {
    public static class NPCSpawner {
        private static readonly SerializableBlockOrientation EntityOrientation =
            new SerializableBlockOrientation(Base6Directions.Direction.Forward, Base6Directions.Direction.Up);

        private static readonly MyObjectBuilder_CubeGrid CubeGridBuilder = new MyObjectBuilder_CubeGrid() {
            EntityId = 0,
            GridSizeEnum = MyCubeSize.Small,
            IsStatic = false,
            Skeleton = new List<BoneInfo>(),
            LinearVelocity = Vector3.Zero,
            AngularVelocity = Vector3.Zero,
            ConveyorLines = new List<MyObjectBuilder_ConveyorLine>(),
            BlockGroups = new List<MyObjectBuilder_BlockGroup>(),
            Handbrake = false,
            XMirroxPlane = null,
            YMirroxPlane = null,
            ZMirroxPlane = null,
            PersistentFlags = MyPersistentEntityFlags2.InScene,
            Name = "NPC",
            DisplayName = "NPC",
            CreatePhysics = true,
            DestructibleBlocks = true,
            PositionAndOrientation = new MyPositionAndOrientation(Vector3D.Zero, Vector3D.Forward, Vector3D.Up),

            CubeBlocks = new List<MyObjectBuilder_CubeBlock>() {
                new MyObjectBuilder_CubeBlock() {
                    EntityId = 0,
                    BlockOrientation = EntityOrientation,
                    SubtypeName = string.Empty,
                    Name = string.Empty,
                    Min = Vector3I.Zero,
                    Owner = 0,
                    ShareMode = MyOwnershipShareModeEnum.None,
                    DeformationRatio = 0,
                }
            }
        };


        public static MyEntity EmptyEntity(string displayName, string model, MyEntity parent, bool parented = false) {
            try {
                var ent = new MyEntity();
                ent.Init(null, model, null, null, null);
                ent.Render.CastShadows = false;
                ent.IsPreview = true;
                ent.Render.Visible = true;
                ent.Save = false;
                ent.SyncFlag = false;
                ent.NeedsWorldMatrix = false;
                ent.Flags |= EntityFlags.IsNotGamePrunningStructureObject;
                MyEntities.Add(ent);
                return ent;
            }
            catch (Exception ex) {
                MyLog.Default.WriteLine($"Exception in EmptyEntity: {ex}");
                return null;
            }
        }

        public static MyEntity SpawnBlock(string subtypeId, string name, Color color, bool isVisible = false,
            bool hasPhysics = false, bool isStatic = false, bool toSave = false, bool destructible = false,
            long ownerId = 0) {
            try {
                CubeGridBuilder.Name = name;
                CubeGridBuilder.DisplayName = name;
                CubeGridBuilder.CubeBlocks[0].SubtypeName = subtypeId;
                CubeGridBuilder.CreatePhysics = hasPhysics;
                CubeGridBuilder.IsStatic = isStatic;
                CubeGridBuilder.DestructibleBlocks = destructible;
                CubeGridBuilder.CubeBlocks[0].Owner = ownerId;
                CubeGridBuilder.CubeBlocks[0].ColorMaskHSV = color.ColorToHSV();
                var ent = (MyEntity) MyAPIGateway.Entities.CreateFromObjectBuilder(CubeGridBuilder);

                ent.Flags &= ~EntityFlags.Save;
                ent.Render.Visible = isVisible;
                MyAPIGateway.Entities.AddEntity(ent);

                return ent;
            }
            catch (Exception ex) {
                MyLog.Default.WriteLine("Exception in Spawn");
                MyLog.Default.WriteLine($"{ex}");
                return null;
            }
        }
    }
}