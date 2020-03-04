using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Interfaces;
using VRage.Library.Utils;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using IMySlimBlock = VRage.Game.ModAPI.Ingame.IMySlimBlock;

namespace NPCMod {
    public partial class NPCBasicMover {
        public Vector3 MovementTarget { get; set; }

        internal readonly NPCDataAnimator animator;

        private IMyEntity activeEnemy;
        private IMySlimBlock targetedBlock = null;
        private Vector3 intermediateTarget = Vector3.Zero;
        private NPCDataAnimator.MovementMode movementMode = NPCDataAnimator.MovementMode.Standing;
        
        public NPCBasicMover(NPCDataAnimator animator, int range, float damage,
            float attacksPerSecond) {
            this.animator = animator;
            this.range = range;
            this.damage = damage;
            this.attacksPerSecond = attacksPerSecond;
        }


        internal bool isValid() {
            return animator?.grid != null && animator.npc != null && !animator.grid.MarkedForClose &&
                   !animator.grid.Closed && animator.grid.InScene;
        }

        internal void doUpdate() {
            if (!isValid()) return;

            animator.updateRender(movementMode);

            updateCombat();
            updateMovement();
        }

        private void updateMovement() {
            bool hasTarget = !MovementTarget.Equals(Vector3.Zero);


            var grid = animator.grid;
            var gridWorldMatrix = grid.WorldMatrix;

            var ownPos = grid.WorldMatrix.Translation;
            var down = grid.Physics.Gravity;
            down.Normalize();

            var downRayTarget = ownPos + down * 0.75f;
            var forwardDir = gridWorldMatrix.Backward;

            List<IHitInfo> hits = new List<IHitInfo>();
            //TODO raycasts parallel/async using asyncraycast or parallelfor later
            MyAPIGateway.Physics.CastRay(ownPos, downRayTarget, hits);

            if (hits.Count > 0) {
                var res = hits[0].Normal;
                rotateRelativeToGround(res, grid);

                drawDebugDir(animator.grid.WorldMatrix.Translation, forwardDir);

                if (hasTarget) {
                    if (MainNPCLoop.ticks % 90 == 0) {
                        intermediateTarget = getIntermediateMovementTarget(grid, MovementTarget, intermediateTarget);
                    }
                    
                    
                    if (intermediateTarget.Equals(Vector3.Zero)) intermediateTarget = MovementTarget;

                    drawDebugLine(grid.WorldMatrix.Translation, intermediateTarget);
                    rotateToTarget(ownPos, grid, intermediateTarget);

                    var dirToTarget = intermediateTarget - ownPos;
                    dirToTarget.Normalize();

                    var curDir = grid.Physics.LinearVelocity;
                    var rel = curDir - hits[0].HitEntity.Physics.LinearVelocity;
                    var speed = animator.getSpeed(movementMode);
                    if (rel.Length() < speed * 1.5f) {
                        drawDebugLine(grid.WorldMatrix.Translation, dirToTarget);
                        grid.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE, dirToTarget * 5000, null,
                            null);
                    }


                    MyLog.Default.WriteLine("speeds: " + grid.Physics.Speed + " | " +
                                            grid.Physics.LinearVelocity +
                                            " | " + grid.Physics.LinearVelocityLocal);
                }

                if (MainNPCLoop.DEBUG)
                    animator.grid.Physics.DebugDraw();
            }
        }

        private Vector3 toSurfacePos(Vector3 point, IMyCubeGrid grid, Vector3 fallback) {
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

            var castFrom = grid.GetPosition() + grid.WorldMatrix.Up * 1.0;
            //var dir = grid.WorldMatrix.Backward;
            var dir = target - castFrom;
            dir.Normalize();

            var plane = Vector3.Cross(dir, grid.WorldMatrix.Right);
            var projectedForward = projectOnPlane(dir, plane);
            projectedForward.Normalize();

            for (int i = 0; i <= 330; i += 30) {
                var degInRad = (float) (i * Math.PI / 180f);

                var newDir = Vector3D.Rotate(projectedForward, MatrixD.CreateFromQuaternion(Quaternion.CreateFromAxisAngle(grid.WorldMatrix.Up, degInRad)));
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

            if (hits.Count > 0) {
                //found something blocking, check if too steep
                target = castFrom + (hits[0].Position - castFrom) * 0.9f;
                return (getAngleBetweenVectors(hits[0].Normal, -grid.Physics.Gravity) * 180 / Math.PI < 44);
            }

            //nothing blocking
            target = castTo;
            return true;
        }

        private void rotateToTarget(Vector3D ownPos, IMyCubeGrid grid, Vector3 targetPos) {
            var gridWorldMatrix = grid.WorldMatrix;
            var dir = targetPos - ownPos;
            dir.Normalize();

            var plane = Vector3.Cross(dir, grid.WorldMatrix.Right);
            var projectedForward = projectOnPlane(grid.WorldMatrix.Backward, plane);
            var pitchAngle = signedAngle(projectedForward, dir, plane);

            MyLog.Default.WriteLine("pitch angle: " + pitchAngle * 180 / Math.PI);
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

            //check if angle between surface is too great
            if (getAngleBetweenVectors(up, gridWorldMatrix.Up) * 180 / Math.PI > 3) {
                var newRot = Vector3.Lerp(gridWorldMatrix.Up, up, 0.6f);
                newRot.Normalize();
                gridWorldMatrix.Up = newRot;
                grid.WorldMatrix = gridWorldMatrix;
                drawDebugDir(grid.WorldMatrix.Translation, up);
            }
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