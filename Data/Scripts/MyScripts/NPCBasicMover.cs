using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Sandbox.Engine.Physics;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Interfaces;
using VRage.Library.Utils;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using IMyCubeBlock = VRage.Game.ModAPI.Ingame.IMyCubeBlock;
using IMySlimBlock = VRage.Game.ModAPI.Ingame.IMySlimBlock;

namespace NPCMod {
    public partial class NPCBasicMover {
        public Vector3 CurrentMovementTarget { get; set; }
        public int ID = MyRandom.Instance.Next(0, int.MaxValue);

        internal readonly NPCDataAnimator animator;

        private Vector3 intermediateTarget = Vector3.Zero;
        private bool flying = false;
        private IHitInfo cachedSurfaceHit = null;
        private List<waypoint> waypointList = new List<waypoint>();
        private int lifetime = MyRandom.Instance.Next(50);
        private NPCDataAnimator.MovementMode movementMode = NPCDataAnimator.MovementMode.Standing;
        private int stuckTimer = 0;
        private Vector3 lastPos;

        public NPCBasicMover(NPCDataAnimator animator, int range, float damage,
            float attacksPerSecond) {
            this.animator = animator;
            this.range = range;
            this.damage = damage;
            this.attacksPerSecond = attacksPerSecond;
        }

        public static NPCBasicMover getEngineer(VRage.Game.ModAPI.IMySlimBlock npc) {
            var npcDataAnimator = new NPCDataAnimator(npc.CubeGrid, npc, 10f);
            var basicMover = new NPCBasicMover(npcDataAnimator, 120, 1f,
                0.5f);
            npc.CubeGrid.Physics.Friction = 1.5f;

            return basicMover;
        }

        internal bool isValid() {
            return animator?.grid != null && animator.npc != null && !animator.grid.MarkedForClose &&
                   !animator.grid.Closed && animator.grid.InScene;
        }

        internal void doUpdate() {
            if (!isValid()) return;

            updateWaypoints();

            animator.updateRender(movementMode);

            updateCombat();
            updateMovement();
        }

        internal void updateWaypoints() {
            lifetime++;

            checkStuck();
            waypointReachedCheck();
            assignFirstWaypoint();

            if (activeEnemy != null &&
                Vector3.Distance(activeEnemy.GetPosition(), animator.grid.GetPosition()) > range * 2f) {
                removeCurrentTarget();
            }

            if (lifetime % 50 == 0 && activeEnemy == null) {
                var enemy = findNearbyEnemy(animator.grid.GetPosition(), range * 1.4f, animator.grid);

                if (enemy == null) return;

                MyLog.Default.WriteLine("found enemy: " + enemy);

                if (Vector3.Distance(animator.grid.GetPosition(), enemy.GetPosition()) > range * 1.5f) return;

                activeEnemy = enemy;
                targetedBlock = null;
                waypointList.Insert(0, new waypoint {trackedEntity = enemy});
            }
        }

        private void checkStuck() {
            var curPos = animator.grid.GetPosition();
            if (Vector3.Distance(curPos, lastPos) < 0.1f) {
                stuckTimer++;
                stuckTimer++;
            }
            else {
                lastPos = curPos;
                stuckTimer = 0;
            }

            if (stuckTimer > 10) {
                removeCurrentTarget();
                stuckTimer = 0;
            }
        }

        public void addWaypoint(Vector3 target) {
            if (target.Equals(Vector3.Zero) || Vector3.Distance(target, animator.grid.GetPosition()) < 2.5f) return;
            waypointList.Add(new waypoint {targetPos = target});
        }

        public void addWaypoint(IMyEntity target) {
            if (target == null) return;
            waypointList.Add(new waypoint {trackedEntity = target});
        }

        private void waypointReachedCheck() {
            if (Vector3.Distance(animator.grid.GetPosition(), CurrentMovementTarget) < 3f) {
                //point reached
                removeCurrentTarget();
                if (waypointList.Count > 0 && waypointList[0].targetPos != Vector3.Zero) {
                    waypointList[0] = new waypoint()
                        {targetPos = toSurfacePos(waypointList[0].targetPos, animator.grid, waypointList[0].targetPos)};
                }
            }
        }

        public int getWaypointCount() {
            return waypointList.Count;
        }

        public void clearWaypoints() {
            waypointList.Clear();
        }

        public void clearWaypointsConservingEnemy() {
            if (waypointList.Count > 1 && waypointList.First().trackedEntity != null) {
                waypointList.RemoveRange(1, waypointList.Count - 2);
            }
            else {
                waypointList.Clear();
            }
        }

        private void removeCurrentTarget() {
            CurrentMovementTarget = Vector3.Zero;
            activeEnemy = null;
            if (waypointList.Count > 0)
                waypointList.RemoveAt(0);
        }

        public Vector3 getCurrentWaypoint() {
            return CurrentMovementTarget;
        }

        private void assignFirstWaypoint() {
            if (waypointList.Count <= 0) return;

            var waypoint = waypointList[0];

            if (waypoint.trackedEntity != null &&
                (waypoint.trackedEntity.Closed || waypoint.trackedEntity.MarkedForClose)) {
                removeCurrentTarget();
            }

            if (waypoint.targetPos != Vector3.Zero) CurrentMovementTarget = waypoint.targetPos;
            if (waypoint.trackedEntity != null) {
                var dist = waypoint.trackedEntity.GetPosition() - animator.grid.GetPosition();
                CurrentMovementTarget = dist.Length() > 30 ? waypoint.trackedEntity.GetPosition() : animator.grid.GetPosition();
            }
        }

        private static IMyPlayer findByCharacter(IMyCharacter character) {
            var players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);

            foreach (var player in players) {
                if (player.Character.EntityId.Equals(character.EntityId)) {
                    return player;
                }
            }

            return null;
        }

        private static IMyEntity findNearbyEnemy(Vector3 position, float range, IMyCubeGrid npcGrid) {
            if (npcGrid.BigOwners.Count <= 0) {
                MyLog.Default.WriteLine(npcGrid + " | " + npcGrid.BigOwners);
                return null;
            }

            var owner = npcGrid.BigOwners[0];
            var faction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(owner);

            var entitiesFound = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(entitiesFound, entity => entity is IMyCubeGrid || entity is IMyCharacter);

            foreach (var entity in entitiesFound) {
                long entityOwner = 0;

                var character = entity as IMyCharacter;
                if (character != null) {
                    var player = findByCharacter(character);
                    if (player != null) {
                        entityOwner = player.IdentityId;
                    }
                }
                else {
                    var grid = entity as IMyCubeGrid;
                    if (grid == null || grid.BigOwners.Count < 1) continue;

                    var blocks = new List<VRage.Game.ModAPI.IMySlimBlock>();
                    grid.GetBlocks(blocks);
                    var isNPC = blocks.Count == 1 && blocks[0].BlockDefinition.Id.SubtypeId.String == "NPC_Test";
                    
                    if (blocks.Count < 3 && !isNPC) continue;

                    entityOwner = grid.BigOwners[0];
                    //gridFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(gridOwner);
                }

                if (entityOwner == 0) continue;
                var isEnemy = faction.IsEnemy(entityOwner);

                var dist = Vector3.Distance(position, entity.GetPosition());

                //MyLog.Default.WriteLine("found entity: " + entity + " | " + entity.DisplayName + " | " + entity.GetType() + " | faction: " + gridFaction.Name + " enemy: " + isEnemy + " dist: " + dist);
                //MyLog.Default.Flush();
                if (dist < range && isEnemy) {
                    return entity;
                }
            }

            return null;
        }

        private void updateMovement() {
            bool hasTarget = !CurrentMovementTarget.Equals(Vector3.Zero);


            var grid = animator.grid;
            var gridWorldMatrix = grid.WorldMatrix;

            var ownPos = grid.WorldMatrix.Translation;
            var down = grid.Physics.Gravity;
            down.Normalize();

            if (Vector3.Distance(CurrentMovementTarget, ownPos) < 1f) {
                CurrentMovementTarget = Vector3.Zero;
            }

            var downRayTarget = ownPos + down * 1.5f;
            var forwardDir = gridWorldMatrix.Backward;

            List<IHitInfo> hits = new List<IHitInfo>();
            var skipCast = true;

            if (MainNPCLoop.ticks % 10 == 0 || cachedSurfaceHit == null) {
                //TODO raycasts parallel/async using asyncraycast or parallelfor later
                MyAPIGateway.Physics.CastRay(ownPos - down * 0.3f, downRayTarget, hits);
                skipCast = false;
                flying = hits.Count == 0;
            }

            if ((hits.Count > 0 || skipCast) && !flying) {
                doValidMoment(skipCast, hits, grid, forwardDir, hasTarget, ownPos);
            }
            else {
                animator.relativeMoveSpeed = 0f;
            }
        }

        private void doValidMoment(bool skipCast, List<IHitInfo> hits, IMyCubeGrid grid, Vector3D forwardDir, bool hasTarget,
            Vector3D ownPos) {
            
            if (!skipCast) {
                cachedSurfaceHit = hits[0];
            }

            rotateRelativeToGround(cachedSurfaceHit.Normal, grid);

            drawDebugDir(animator.grid.WorldMatrix.Translation, forwardDir);

            if (hasTarget) {
                if (MainNPCLoop.ticks % 40 == 0) {
                    intermediateTarget =
                        getIntermediateMovementTarget(grid, CurrentMovementTarget, intermediateTarget);
                }


                if (intermediateTarget.Equals(Vector3.Zero)) intermediateTarget = CurrentMovementTarget;

                drawDebugLine(grid.WorldMatrix.Translation, intermediateTarget);
                rotateToTarget(ownPos, grid, intermediateTarget);

                var dirToTarget = intermediateTarget - ownPos;
                dirToTarget.Normalize();

                var curDir = grid.Physics.LinearVelocity;
                var relativeSpeed = curDir - cachedSurfaceHit.HitEntity.Physics.LinearVelocity;
                var speed = animator.getSpeed(movementMode);

                var correctedDir = dirToTarget * speed * 1.5 - curDir;
                correctedDir = projectOnPlane(correctedDir, cachedSurfaceHit.Normal);
                
                animator.relativeMoveSpeed = relativeSpeed.Length();

                var missingVector = dirToTarget * speed - grid.Physics.LinearVelocity;
                
                if (missingVector.Length() > 0.5f) {
                    drawDebugLine(grid.WorldMatrix.Translation, correctedDir);
                    correctedDir.Normalize();
                    grid.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE, correctedDir * 3000, null,
                        null);
                }


                //MyLog.Default.WriteLine("speeds: " + grid.Physics.Speed + " | " + grid.Physics.LinearVelocity + " | " + grid.Physics.LinearVelocityLocal);
            }

            if (MainNPCLoop.DEBUG)
                animator.grid.Physics.DebugDraw();
        }

        public static Vector3 toSurfacePos(Vector3 point, IMyEntity grid, Vector3 fallback) {
            List<IHitInfo> hits = new List<IHitInfo>();
            var grav = grid.Physics.Gravity;
            grav.Normalize();
            var from = point + grav * 5;
            var to = point - grav * 15;
            MyAPIGateway.Physics.CastRay(from, to, hits);

            if (hits.Count > 0) {
                return hits[0].Position;
            }

            return fallback;
        }

        private Vector3 getIntermediateMovementTarget(IMyCubeGrid grid, Vector3 target, Vector3 oldIntermediate) {
            var castFrom = grid.GetPosition() + grid.WorldMatrix.Up * 0.3f;
            //var dir = grid.WorldMatrix.Backward;
            var dir = target - castFrom;
            dir.Normalize();

            var plane = Vector3.Cross(dir, grid.WorldMatrix.Right);
            var projectedForward = projectOnPlane(dir, plane);
            projectedForward.Normalize();

            for (int i = 0; i <= 330; i += 30) {
                var degInRad = (float) (i * Math.PI / 180f);

                var newDir = Vector3D.Rotate(projectedForward,
                    MatrixD.CreateFromQuaternion(Quaternion.CreateFromAxisAngle(grid.WorldMatrix.Up, degInRad)));
                newDir.Normalize();
                var targetPos = target;
                var range = 35 + (3 * i / 30);
                if (validDir(castFrom, newDir, grid, ref targetPos, range)) {
                    return i == 0 ? target : targetPos;
                }
            }

            return target;
        }

        private bool validDir(Vector3 castFrom, Vector3 dir, IMyCubeGrid grid, ref Vector3 target, float range) {
            var castTo = castFrom + dir * range;
            List<IHitInfo> hits = new List<IHitInfo>();
            MyAPIGateway.Physics.CastRay(castFrom, castTo, hits);

            drawDebugLine(castFrom, castTo, Color.Yellow);

            IHitInfo hit;
            if (findFirstNonNPC(hits, out hit)) {
                //found something blocking, check if too steep
                target = castFrom + (hit.Position - castFrom) * 0.9f;
                return (getAngleBetweenVectors(hit.Normal, -grid.Physics.Gravity) * 180 / Math.PI < 44);
            }

            //nothing blocking
            target = castTo;
            return true;
        }

        private bool findFirstNonNPC(List<IHitInfo> hits, out IHitInfo hit, bool findEnemies = false) {
            foreach (var elem in hits) {
                if (elem.HitEntity?.Name == null) continue;
                if (elem.HitEntity.Name.StartsWith("npc_")) {
                    if (!findEnemies) continue;
                    if (isEnemy(elem.HitEntity)) {
                        hit = elem;
                        return true;
                    }
                    continue;
                }
                
                hit = elem;
                return true;
            }

            hit = null;
            return false;
        }

        private void rotateToTarget(Vector3D ownPos, IMyCubeGrid grid, Vector3 targetPos) {
            var gridWorldMatrix = grid.WorldMatrix;
            var dir = targetPos - ownPos;
            dir.Normalize();

            var plane = Vector3.Cross(dir, grid.WorldMatrix.Right);
            var projectedForward = projectOnPlane(grid.WorldMatrix.Backward, plane);
            var pitchAngle = signedAngle(projectedForward, dir, plane);

            //MyLog.Default.WriteLine("pitch angle: " + pitchAngle * 180 / Math.PI);
            gridWorldMatrix = MatrixD.CreateRotationY(pitchAngle) * gridWorldMatrix;
            grid.WorldMatrix = gridWorldMatrix;
        }

        private void rotateRelativeToGround(Vector3 res, IMyCubeGrid grid) {
            var up = res; //up is normal of surface
            up.Normalize();

            var gridWorldMatrix = grid.WorldMatrix;

            //check if terrain is too steep, if yes, then align to gravity
            if (getAngleBetweenVectors(up, -grid.Physics.Gravity) * 180 / Math.PI > 45) {
                up = -grid.Physics.Gravity;
            }
            var newRot = Vector3.Lerp(gridWorldMatrix.Up, up, 0.6f);
            newRot.Normalize();
            gridWorldMatrix.Up = newRot;
            grid.WorldMatrix = gridWorldMatrix;
            drawDebugDir(grid.WorldMatrix.Translation, up);

            //check if angle between surface is too great
//            if (getAngleBetweenVectors(up, gridWorldMatrix.Up) * 180 / Math.PI > 3) {
//                var newRot = Vector3.Lerp(gridWorldMatrix.Up, up, 0.6f);
//                newRot.Normalize();
//                gridWorldMatrix.Up = newRot;
//                grid.WorldMatrix = gridWorldMatrix;
//                drawDebugDir(grid.WorldMatrix.Translation, up);
//            }
        }

        private struct waypoint {
            internal IMyEntity trackedEntity;
            internal Vector3 targetPos;
        }

        private Vector3 projectOnPlane(Vector3 vector, Vector3 planeNormal) {
            float sqrMag = Vector3.Dot(planeNormal, planeNormal);
            var dot = Vector3.Dot(vector, planeNormal);

            return new Vector3(vector.X - planeNormal.X * dot / sqrMag,
                vector.Y - planeNormal.Y * dot / sqrMag,
                vector.Z - planeNormal.Z * dot / sqrMag);
        }

        private double getAngleBetweenVectors(Vector3D a, Vector3D b) {
            var dot = Vector3.Dot(a, b);
            dot = (float) (dot / (a.Length() * b.Length()));
            var acos = Math.Acos(dot);
            return acos;
        }

        private double signedAngle(Vector3 from, Vector3 to, Vector3 axis) {
            var angle = getAngleBetweenVectors(@from, to);
            float cross_x = @from.Y * to.Z - @from.Z * to.Y;
            float cross_y = @from.Z * to.X - @from.X * to.Z;
            float cross_z = @from.X * to.Y - @from.Y * to.X;
            float sign = Math.Sign(axis.X * cross_x + axis.Y * cross_y + axis.Z * cross_z);
            return angle * sign;
        }

        public static void drawDebugLine(Vector3 from, Vector3 to, Vector4 color) {
            if (MainNPCLoop.DEBUG)
                MySimpleObjectDraw.DrawLine(@from, to, MyStringId.GetOrCompute("Square"), ref color, 0.05f);
        }

        public static void drawDebugLine(Vector3 from, Vector3 to) {
            drawDebugLine(@from, to, Color.Red.ToVector4());
        }

        public static void drawDebugDir(Vector3 from, Vector3 dir, float dist = 2f) {
            dir.Normalize();
            var to = @from + dir * dist;
            drawDebugLine(@from, to, Color.Aquamarine.ToVector4());
        }
    }
}