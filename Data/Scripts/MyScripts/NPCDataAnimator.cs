using Sandbox.Game.Entities;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRageMath;

namespace NPCMod {
    public class NPCDataAnimator {
        public enum MovementMode {
            Walking, Attacking, Standing
        }
        
        private readonly MyEntitySubpart top_left_leg;
        private readonly MyEntitySubpart top_right_leg;
        private readonly MyEntitySubpart bottom_left_leg;
        private readonly MyEntitySubpart bottom_right_leg;
        private readonly MyEntitySubpart gun_muzzle;
        internal readonly IMyCubeGrid grid;
        internal readonly IMySlimBlock npc;

        private float animProgress;
        private bool ascending = true;
        public readonly float moveSpeed;
        private static readonly float maxAngle = 160;
        private static readonly float bottomExtraAngle = -0.1f;

        public NPCDataAnimator(IMyCubeGrid grid, IMySlimBlock npc, float moveSpeed) {
            this.grid = grid;
            this.npc = npc;
            this.moveSpeed = moveSpeed;
            top_left_leg = npc.FatBlock.GetSubpart("leg_top_left");
            top_right_leg = npc.FatBlock.GetSubpart("leg_top_right");
            //gun_muzzle = npc.FatBlock.GetSubpart("muzzle_projectile");
            bottom_left_leg = top_left_leg.GetSubpart("leg_bottom_left");
            bottom_right_leg = top_right_leg.GetSubpart("leg_bottom_right");
        }

        public Vector3 getMuzzlePosition() {
            return grid.GetPosition() + grid.WorldMatrix.Up * 1.5f + grid.WorldMatrix.Forward * 0.6f;
        }

        public float getSpeed(MovementMode mode) {
            if (mode == MovementMode.Attacking) {
                return moveSpeed * 0.4f;
            } else if (mode == MovementMode.Walking) {
                return moveSpeed;
            }

            return 0f;
        }

        private float getAnimationSpeed() {
            return grid.Physics.LinearVelocity.Length() * 0.75f;
        }

        public void updateRender(MovementMode mode) {

            if (mode == MovementMode.Standing) {
                setAngleOnPart(0, top_left_leg);
                setAngleOnPart(0, top_right_leg);
                setAngleOnPart((0), bottom_left_leg);
                setAngleOnPart((0), bottom_right_leg);
                return;
            }

            var useSpeed = getAnimationSpeed();
            
            if (ascending) {
                animProgress += (1 / 60f) * useSpeed / 2;
            }
            else {
                animProgress -= (1 / 60f) * useSpeed / 2;
            }

            if (animProgress >= 1) ascending = false;
            if (animProgress <= 0) ascending = true;

            var rot = -maxAngle + (maxAngle * 2 * animProgress);
            rot /= 360;

            setAngleOnPart(rot, top_left_leg);
            setAngleOnPart(-rot, top_right_leg);
            setAngleOnPart(-(rot + bottomExtraAngle) * 0.8f, bottom_left_leg);
            setAngleOnPart((rot - bottomExtraAngle) * 0.8f, bottom_right_leg);
        }

        private void setOffsetOnPart(float offsetChange, MyEntitySubpart part) {
            offsetChange *= 0.15f;
            part.PositionComp.LocalMatrix *= Matrix.CreateTranslation(0, offsetChange, 0);
        }

        private void setAngleOnPart(float angle, MyEntitySubpart part) {
            var initial = part.PositionComp.LocalMatrix;
            var current_mat = MatrixD.CreateRotationX(angle * 4);

            current_mat.M41 = initial.M41;
            current_mat.M42 = initial.M42;
            current_mat.M43 = initial.M43;

            if (!part.Closed) {
                part.PositionComp.LocalMatrix = current_mat;
            }
        }
    }
}